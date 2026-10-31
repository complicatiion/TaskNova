using System.Security.Cryptography.X509Certificates;
using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows;

/// <summary>
/// Authenticode check (WinVerifyTrust) of an executable – embedded signature first, then the Windows catalog
/// (most Windows binaries are catalog-signed). Offline: no revocation check. Slow: only on a background thread, cached.
/// </summary>
public static unsafe class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint ChoiceCatalog = 2;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private const uint RevocationCheckNone = 0x00000010;
    private const uint CacheOnlyUrlRetrieval = 0x00001000;

    /// <summary>Signature state and signer (simple name of the certificate); signer is null when unsigned.</summary>
    public static (bool Trusted, string? Signer) GetSignature(string file)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(file))
        {
            return (false, null);
        }

        try
        {
            if (VerifyEmbedded(file))
            {
                return (true, SignerOf(file));
            }

            var catalog = VerifyCatalog(file);
            if (catalog is not null)
            {
                return (true, SignerOf(catalog) ?? "Microsoft Windows");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, null);
        }

        return (false, SignerOf(file));
    }

    private static string? SignerOf(string file)
    {
        try
        {
#pragma warning disable SYSLIB0057 // Reads the signer of an already verified file.
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            return cert.GetNameInfo(X509NameType.SimpleName, false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool VerifyEmbedded(string file)
    {
        fixed (char* path = file)
        {
            var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)sizeof(WINTRUST_FILE_INFO), pcwszFilePath = path };
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = UiNone,
                fdwRevocationChecks = RevokeNone,
                dwUnionChoice = ChoiceFile,
                pInfo = &fileInfo,
                dwStateAction = StateActionVerify,
                dwProvFlags = RevocationCheckNone | CacheOnlyUrlRetrieval
            };

            return Verify(&data);
        }
    }

    /// <summary>Path of the catalog that vouches for the file, or null.</summary>
    private static string? VerifyCatalog(string file)
    {
        using var handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fixed (char* sha256 = "SHA256")
        {
            if (!Wintrust.CryptCATAdminAcquireContext2(out var admin, null, sha256, 0, 0))
            {
                return null;
            }

            try
            {
                uint hashSize = 0;
                Wintrust.CryptCATAdminCalcHashFromFileHandle2(admin, handle.DangerousGetHandle(), ref hashSize, null, 0);
                if (hashSize == 0 || hashSize > 128)
                {
                    return null;
                }

                var hash = stackalloc byte[(int)hashSize];
                if (!Wintrust.CryptCATAdminCalcHashFromFileHandle2(admin, handle.DangerousGetHandle(), ref hashSize, hash, 0))
                {
                    return null;
                }

                var catInfo = Wintrust.CryptCATAdminEnumCatalogFromHash(admin, hash, hashSize, 0, null);
                if (catInfo == 0)
                {
                    return null;
                }

                try
                {
                    var info = new CATALOG_INFO { cbStruct = (uint)sizeof(CATALOG_INFO) };
                    if (!Wintrust.CryptCATCatalogInfoFromContext(catInfo, &info, 0))
                    {
                        return null;
                    }

                    var catalogPath = new string(info.wszCatalogFile);
                    var tag = Convert.ToHexString(new ReadOnlySpan<byte>(hash, (int)hashSize));
                    fixed (char* catalog = catalogPath)
                    fixed (char* member = tag)
                    fixed (char* memberPath = file)
                    {
                        var catalogInfo = new WINTRUST_CATALOG_INFO
                        {
                            cbStruct = (uint)sizeof(WINTRUST_CATALOG_INFO),
                            pcwszCatalogFilePath = catalog,
                            pcwszMemberTag = member,
                            pcwszMemberFilePath = memberPath,
                            hMemberFile = handle.DangerousGetHandle(),
                            pbCalculatedFileHash = hash,
                            cbCalculatedFileHash = hashSize,
                            hCatAdmin = admin
                        };
                        var data = new WINTRUST_DATA
                        {
                            cbStruct = (uint)sizeof(WINTRUST_DATA),
                            dwUIChoice = UiNone,
                            fdwRevocationChecks = RevokeNone,
                            dwUnionChoice = ChoiceCatalog,
                            pInfo = &catalogInfo,
                            dwStateAction = StateActionVerify,
                            dwProvFlags = RevocationCheckNone | CacheOnlyUrlRetrieval
                        };

                        return Verify(&data) ? catalogPath : null;
                    }
                }
                finally
                {
                    Wintrust.CryptCATAdminReleaseCatalogContext(admin, catInfo, 0);
                }
            }
            finally
            {
                Wintrust.CryptCATAdminReleaseContext(admin, 0);
            }
        }
    }

    private static bool Verify(WINTRUST_DATA* data)
    {
        var action = GenericVerifyV2;
        var result = Wintrust.WinVerifyTrust(-1 /* INVALID_HANDLE_VALUE: no UI */, &action, data);
        data->dwStateAction = StateActionClose;
        Wintrust.WinVerifyTrust(-1, &action, data);
        return result == 0;
    }
}
