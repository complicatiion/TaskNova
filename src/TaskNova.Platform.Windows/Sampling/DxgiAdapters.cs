using TaskNova.Platform.Windows.Native;

namespace TaskNova.Platform.Windows.Sampling;

internal sealed record DxgiAdapter(string Luid, string Name, ulong DedicatedBytes);

/// <summary>
/// Adapter names and dedicated memory by LUID via DXGI (IDXGIFactory1::EnumAdapters1 / IDXGIAdapter1::GetDesc1),
/// called through the COM vtable directly – no COM interop layer, Native-AOT friendly.
/// The LUID key matches the PDH instance names ("0x00000000_0x0000D1A5").
/// </summary>
internal static unsafe class DxgiAdapters
{
    private static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint FlagSoftware = 2;

    public static IReadOnlyDictionary<string, DxgiAdapter> Read()
    {
        var result = new Dictionary<string, DxgiAdapter>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        void* factory = null;
        try
        {
            var iid = IidFactory1;
            if (Dxgi.CreateDXGIFactory1(&iid, &factory) < 0 || factory == null)
            {
                return result;
            }

            var factoryVtbl = *(void***)factory;
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<void*, uint, void**, int>)factoryVtbl[12];
            for (uint i = 0; i < 16; i++)
            {
                void* adapter = null;
                var hr = enumAdapters1(factory, i, &adapter);
                if (hr == DxgiErrorNotFound || hr < 0 || adapter == null)
                {
                    break;
                }

                try
                {
                    var adapterVtbl = *(void***)adapter;
                    var getDesc1 = (delegate* unmanaged[Stdcall]<void*, DXGI_ADAPTER_DESC1*, int>)adapterVtbl[10];
                    DXGI_ADAPTER_DESC1 desc;
                    if (getDesc1(adapter, &desc) >= 0 && (desc.Flags & FlagSoftware) == 0)
                    {
                        var luid = $"0x{(uint)desc.AdapterLuid.HighPart:X8}_0x{desc.AdapterLuid.LowPart:X8}";
                        var name = new string(desc.Description).Trim();
                        result[luid] = new DxgiAdapter(luid, name, desc.DedicatedVideoMemory);
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        catch (DllNotFoundException)
        {
            // Server Core without DXGI.
        }
        catch (EntryPointNotFoundException)
        {
        }
        finally
        {
            if (factory != null)
            {
                Release(factory);
            }
        }

        return result;
    }

    private static void Release(void* unknown)
    {
        var vtbl = *(void***)unknown;
        ((delegate* unmanaged[Stdcall]<void*, uint>)vtbl[2])(unknown);
    }
}
