using System.Collections.Concurrent;
using System.Diagnostics;
using TaskNova.Core.Classification;
using TaskNova.Core.Models;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>
/// Collects the slow, once-per-process details (path, command line, user, description, icon, platform) on its own
/// low-priority STA thread. Signatures are checked afterwards, only for executables outside the Windows folder.
/// Results are picked up by the UI with <see cref="TryTakeDetails"/> / <see cref="TryTakeSignature"/>.
/// Everything is cached by path or SID, so a program started 50 times is read once.
/// </summary>
public sealed unsafe class ProcessEnricher : IDisposable
{
    private readonly BlockingCollection<(ProcessKey Key, string Name)> _queue = new(new ConcurrentQueue<(ProcessKey, string)>());
    private readonly ConcurrentQueue<(ProcessKey Key, string Path)> _signatureQueue = new();
    private readonly ConcurrentQueue<ProcessDetails> _details = new();
    private readonly ConcurrentQueue<SignatureInfo> _signatures = new();
    private readonly Dictionary<string, (string? Description, string? Company, byte[]? Icon)> _fileCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (bool Signed, string? Signer)> _signatureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _userCache = new(StringComparer.Ordinal);
    private readonly string _windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private readonly Thread _thread;
    private volatile bool _disposed;

    public ProcessEnricher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "TaskNova.Enricher", Priority = ThreadPriority.BelowNormal };
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();
    }

    /// <summary>Check signatures of non-Windows executables (security indicator).</summary>
    public bool CheckSignatures { get; set; } = true;

    public void Enqueue(ProcessKey key, string name)
    {
        if (!_disposed)
        {
            _queue.Add((key, name));
        }
    }

    public bool TryTakeDetails(out ProcessDetails details) => _details.TryDequeue(out details!);

    public bool TryTakeSignature(out SignatureInfo signature) => _signatures.TryDequeue(out signature!);

    private void Run()
    {
        if (OperatingSystem.IsWindows())
        {
            Ole32.CoInitializeEx(0, 0x2 /* COINIT_APARTMENTTHREADED */);
        }

        try
        {
            while (!_disposed)
            {
                if (_queue.TryTake(out var item, _signatureQueue.IsEmpty ? 500 : 0))
                {
                    try
                    {
                        _details.Enqueue(Read(item.Key, item.Name));
                    }
                    catch (Exception)
                    {
                        _details.Enqueue(new ProcessDetails { Key = item.Key, AccessDenied = true });
                    }

                    continue;
                }

                // Queue empty: one signature check at a time, so new processes always come first.
                if (CheckSignatures && _signatureQueue.TryDequeue(out var pending))
                {
                    _signatures.Enqueue(CheckSignature(pending.Key, pending.Path));
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Collection completed (dispose).
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                Ole32.CoUninitialize();
            }
        }
    }

    private ProcessDetails Read(ProcessKey key, string name)
    {
        if (!OperatingSystem.IsWindows() || key.Pid is 0 or 4 || ProcessClassifier.IsPseudo(name))
        {
            return new ProcessDetails { Key = key, Description = name };
        }

        var handle = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, false, key.Pid);
        if (handle == 0)
        {
            return new ProcessDetails { Key = key, AccessDenied = true };
        }

        using var process = new SafeKernelHandle(handle);
        var path = ImagePath(handle);
        var commandLine = CommandLine(handle);
        var user = UserName(handle);
        var platform = Platform(handle);

        string? description = null;
        string? company = null;
        byte[]? icon = null;
        if (path is not null)
        {
            if (!_fileCache.TryGetValue(path, out var file))
            {
                try
                {
                    var version = FileVersionInfo.GetVersionInfo(path);
                    file.Description = string.IsNullOrWhiteSpace(version.FileDescription) ? null : version.FileDescription.Trim();
                    file.Company = string.IsNullOrWhiteSpace(version.CompanyName) ? null : version.CompanyName.Trim();
                }
                catch (Exception)
                {
                    // No version resource or file gone.
                }

                file.Icon = IconExtractor.Extract(path);
                _fileCache[path] = file;
            }

            (description, company, icon) = file;

            if (CheckSignatures && !ProcessClassifier.IsUnder(path, _windowsDirectory))
            {
                _signatureQueue.Enqueue((key, path));
            }
        }

        return new ProcessDetails
        {
            Key = key,
            Path = path,
            CommandLine = commandLine,
            UserName = user,
            Description = description,
            Company = company,
            Platform = platform,
            IconPixels = icon,
            IconSize = icon is null ? 0 : IconExtractor.Size
        };
    }

    private SignatureInfo CheckSignature(ProcessKey key, string path)
    {
        if (!_signatureCache.TryGetValue(path, out var signature))
        {
            signature = Authenticode.GetSignature(path);
            _signatureCache[path] = signature;
        }

        return new SignatureInfo(key, signature.Signed, signature.Signer);
    }

    private static string? ImagePath(nint handle)
    {
        var buffer = stackalloc char[1024];
        uint size = 1024;
        return Kernel32.QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    private static string? CommandLine(nint handle)
    {
        uint needed = 0;
        var status = Ntdll.NtQueryInformationProcess(handle, Ntdll.ProcessCommandLineInformation, null, 0, out needed);
        if (needed == 0 || needed > 64 * 1024)
        {
            return null;
        }

        var buffer = stackalloc byte[(int)needed];
        status = Ntdll.NtQueryInformationProcess(handle, Ntdll.ProcessCommandLineInformation, buffer, needed, out _);
        if (status < 0)
        {
            return null;
        }

        var text = ((UNICODE_STRING*)buffer)->ToStringOrNull();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private string? UserName(nint process)
    {
        if (!Advapi32.OpenProcessToken(process, Advapi32.TokenQuery, out var token))
        {
            return null;
        }

        using var tokenHandle = new SafeKernelHandle(token);
        var buffer = stackalloc byte[256];
        if (!Advapi32.GetTokenInformation(token, Advapi32.TokenUser, buffer, 256, out _))
        {
            return null;
        }

        var sid = *(void**)buffer; // TOKEN_USER.User.Sid
        var length = Advapi32.GetLengthSid(sid);
        var key = Convert.ToHexString(new ReadOnlySpan<byte>(sid, (int)length));
        if (_userCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var name = stackalloc char[256];
        var domain = stackalloc char[256];
        uint nameLength = 256;
        uint domainLength = 256;
        var user = Advapi32.LookupAccountSid(null, sid, name, ref nameLength, domain, ref domainLength, out _)
            ? new string(name, 0, (int)nameLength)
            : null;
        if (user is not null)
        {
            _userCache[key] = user;
        }

        return user;
    }

    private static string? Platform(nint process)
    {
        if (!Kernel32.IsWow64Process2(process, out var processMachine, out var nativeMachine))
        {
            return null;
        }

        if (processMachine != 0)
        {
            return processMachine == 0x014C ? "32-bit" : "x86/ARM";
        }

        return nativeMachine switch
        {
            0xAA64 => "ARM64",
            0x8664 => "64-bit",
            _ => null
        };
    }

    public void Dispose()
    {
        _disposed = true;
        _queue.CompleteAdding();
    }
}
