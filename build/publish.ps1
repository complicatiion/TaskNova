<#
.SYNOPSIS
    Builds the TaskNova release: tasknova_v<version>_portable.zip (+ .sha256) and, if Inno Setup 6 is installed,
    tasknova_v<version>_setup.exe (+ .sha256).

.DESCRIPTION
    1. Runs the tests (skip with -SkipTests)
    2. Publishes tasknova.exe directly into artifacts\tasknova_v<version>_portable\TaskNova
       – Native AOT by default (needs the Visual Studio workload "Desktop development with C++"),
       – -NoAot: self-contained .NET build (single file, NOT compressed – compression costs RAM at every start)
    3. Checks that the license files are part of the output
    4. Writes the tasknova.portable marker, packs the portable ZIP from that folder and writes the SHA-256
    5. Compiles build\installer\tasknova.iss with ISCC from the same folder (the marker is excluded; skip with -SkipInstaller)

    There is no separate artifacts\publish folder: the portable folder is the single source for ZIP and setup, and
    tasknova.iss uses it as default SourceDir, so the setup can also be compiled in the Inno Setup IDE afterwards.

.EXAMPLE
    pwsh -File build\publish.ps1
    pwsh -File build\publish.ps1 -NoAot -SkipInstaller
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [switch] $NoAot,
    [switch] $SkipTests,
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
[xml] $props = Get-Content -Raw -Path (Join-Path $root 'Directory.Build.props')
$versionNode = $props.SelectSingleNode('//Version')
if ($null -eq $versionNode) { throw 'Version not found in Directory.Build.props.' }
$version = $versionNode.InnerText.Trim()

$name = "tasknova_v$version"
$artifacts = Join-Path $root 'artifacts'
$portableRoot = Join-Path $artifacts "$($name)_portable"
$appDir = Join-Path $portableRoot 'TaskNova'
$zip = Join-Path $artifacts "$($name)_portable.zip"

Write-Host "TaskNova $version · $Configuration · $Runtime · $(if ($NoAot) { 'self-contained' } else { 'Native AOT' })" -ForegroundColor Magenta

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'TaskNova.slnx') -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

# Old output of this version (and the publish folder of builds before 0.0.2) is removed.
foreach ($dir in $portableRoot, (Join-Path $artifacts 'publish')) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}
New-Item -ItemType Directory -Path $appDir -Force | Out-Null

$project = Join-Path $root 'src\TaskNova.App\TaskNova.App.csproj'
if ($NoAot) {
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true -p:PublishAot=false -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=none -o $appDir
}
else {
    dotnet publish $project -c $Configuration -r $Runtime -p:PublishAot=true -p:DebugType=none -p:StripSymbols=true -o $appDir
}
if ($LASTEXITCODE -ne 0) { throw 'Publishing TaskNova failed.' }

# No debug symbols in the release.
Get-ChildItem -Path $appDir -Filter '*.pdb' -Recurse | Remove-Item -Force

foreach ($required in 'tasknova.exe', 'README.md', 'licenses\LICENSE.md', 'licenses\README.md', 'licenses\third-party\AVALONIA.md') {
    if (-not (Test-Path (Join-Path $appDir $required))) { throw "Missing in output: $required" }
}

# Portable: marker next to tasknova.exe (settings and app history then live in .\data).
Set-Content -Path (Join-Path $appDir 'tasknova.portable') -Value 'Portable mode: settings and app history are stored in .\data' -Encoding ascii

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $appDir -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path -Leaf $zip)" -Encoding ascii
Write-Host "Portable: $zip" -ForegroundColor Green
Write-Host "SHA-256:  $hash"

if (-not $SkipInstaller) {
    $iscc = @(
        (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if ($null -eq $iscc) {
        Write-Warning 'Inno Setup 6 (ISCC.exe) not found – installer skipped. https://jrsoftware.org/isinfo.php'
    }
    else {
        & $iscc "/DAppVersion=$version" "/DSourceDir=$appDir" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot 'installer\tasknova.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
        $setup = Join-Path $artifacts "$($name)_setup.exe"
        $setupHash = (Get-FileHash -Path $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -Path "$setup.sha256" -Value "$setupHash  $(Split-Path -Leaf $setup)" -Encoding ascii
        Write-Host "Setup:    $setup" -ForegroundColor Green
        Write-Host "SHA-256:  $setupHash"
    }
}
