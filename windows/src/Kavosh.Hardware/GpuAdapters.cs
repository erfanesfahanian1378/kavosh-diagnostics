using Vortice.DXGI;

namespace Kavosh.Hardware;

public sealed record GpuAdapterInfo(string Name, uint VendorId, ulong DedicatedVideoMemory, ulong SharedSystemMemory, long Luid, bool IsSoftware)
{
    /// <summary>Integrated GPUs have little or no memory of their own.</summary>
    public bool IsIntegrated => DedicatedVideoMemory < 600UL * 1024 * 1024;

    public string VendorName => VendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 or 0x1022 => "AMD",
        0x8086 => "Intel",
        0x5143 => "Qualcomm",
        0x1414 => "Microsoft",
        _ => "",
    };
}

/// <summary>Lists the graphics adapters through DXGI (the same list Direct3D uses).</summary>
public static class GpuAdapters
{
    /// <summary>All hardware adapters, the high-performance one first.</summary>
    public static List<GpuAdapterInfo> List()
    {
        var result = new List<GpuAdapterInfo>();
        if (!OperatingSystem.IsWindows()) return result;
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            var seen = new HashSet<long>();
            using var factory6 = factory.QueryInterfaceOrNull<IDXGIFactory6>();
            for (uint i = 0; ; i++)
            {
                IDXGIAdapter1? adapter = null;
                if (factory6 is not null)
                {
                    if (factory6.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out IDXGIAdapter1? a).Failure) break;
                    adapter = a;
                }
                else if (factory.EnumAdapters1(i, out IDXGIAdapter1? a).Failure)
                {
                    break;
                }
                else
                {
                    adapter = a;
                }
                if (adapter is null) break;
                using (adapter)
                {
                    var desc = adapter.Description1;
                    long luid = ((long)desc.Luid.HighPart << 32) | desc.Luid.LowPart;
                    if (!seen.Add(luid)) continue;
                    bool software = (desc.Flags & AdapterFlags.Software) != 0 || desc.VendorId == 0x1414 && desc.DeviceId == 0x8C;
                    if (software) continue;
                    result.Add(new GpuAdapterInfo(desc.Description.Trim(), desc.VendorId, (ulong)desc.DedicatedVideoMemory,
                        (ulong)desc.SharedSystemMemory, luid, software));
                }
            }
        }
        catch
        {
            // No DXGI (very old system or a remote session without a GPU).
        }
        return result;
    }
}
