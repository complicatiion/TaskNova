<#
.SYNOPSIS
    Builds the TaskNova release from the command line – the same as "Publish › Portable" in Visual Studio.

.DESCRIPTION
    1. Runs the tests (skip with -SkipTests)
    2. dotnet publish with the publish profile "Portable" (src\TaskNova.App\Properties\PublishProfiles\Portable.pubxml):
       Native AOT, win-x64, into artifacts\publish\TaskNova
    3. The target "TaskNovaRelease" in TaskNova.App.csproj then writes the portable marker, packs
       artifacts\tasknova_v<version>_portable.zip (+ .sha256) and compiles build\installer\tasknova.iss with Inno Setup
       into artifacts\tasknova_v<version>_setup.exe (+ .sha256).

.EXAMPLE
    pwsh -File build\publish.ps1
    pwsh -File build\publish.ps1 -NoAot -SkipInstaller
#>
[CmdletBinding()]
param(
    [switch] $NoAot,
    [switch] $SkipTests,
    [switch] $SkipInstaller,
    [switch] $SkipZip,
    [string] $InnoSetupCompiler
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'TaskNova.slnx') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$arguments = @(
    'publish', (Join-Path $root 'src\TaskNova.App\TaskNova.App.csproj'),
    '-p:PublishProfile=Portable'
)
if ($NoAot) { $arguments += '-p:PublishAot=false', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=false' }
if ($SkipInstaller) { $arguments += '-p:BuildInstaller=false' }
if ($SkipZip) { $arguments += '-p:BuildPortableZip=false' }
if ($InnoSetupCompiler) { $arguments += "-p:InnoSetupCompiler=$InnoSetupCompiler" }

dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Publishing TaskNova failed.' }

Get-ChildItem (Join-Path $root 'artifacts') -File | Where-Object Name -like 'tasknova_v*' | ForEach-Object { Write-Host $_.FullName -ForegroundColor Green }
