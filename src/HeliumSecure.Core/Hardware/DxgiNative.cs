using System.Runtime.InteropServices;

namespace HeliumSecure.Core.Hardware;

[ComImport]
[Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIFactory1
{
    [PreserveSig]
    int EnumAdapters(uint adapter, out IntPtr ppAdapter);

    [PreserveSig]
    int MakeWindowAssociation(IntPtr windowHandle, uint flags);

    [PreserveSig]
    int GetWindowAssociation(out IntPtr pWindowHandle);

    [PreserveSig]
    int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);

    [PreserveSig]
    int CreateSoftwareAdapter(IntPtr module, out IntPtr ppAdapter);

    [PreserveSig]
    int EnumAdapters1(uint adapter, out IDXGIAdapter1 ppAdapter);

    [PreserveSig]
    int IsCurrent();
}

[ComImport]
[Guid("29038f61-3839-4626-91fd-086879011a05")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIAdapter1
{
    [PreserveSig]
    int SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);

    [PreserveSig]
    int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);

    [PreserveSig]
    int GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);

    [PreserveSig]
    int GetParent(ref Guid riid, out IntPtr ppParent);

    [PreserveSig]
    int EnumOutputs(uint output, out IntPtr ppOutput);

    [PreserveSig]
    int GetDesc(out IntPtr pDesc);

    [PreserveSig]
    int CheckInterfaceSupport(ref Guid interfaceName, out long pUMDVersion);

    [PreserveSig]
    int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DXGI_ADAPTER_DESC1
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Description;
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public UIntPtr DedicatedVideoMemory;
    public UIntPtr DedicatedSystemMemory;
    public UIntPtr SharedSystemMemory;
    public LUID AdapterLuid;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;

    public ulong Value => ((ulong)(uint)HighPart << 32) | LowPart;
}

internal static class DxgiNative
{
    public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 ppFactory);

    public static List<GpuAdapterInfo> EnumerateAdapters()
    {
        var list = new List<GpuAdapterInfo>();
        try
        {
            var iid = IID_IDXGIFactory1;
            int hr = CreateDXGIFactory1(ref iid, out var factory);
            if (hr != 0 || factory == null) return list;

            uint i = 0;
            while (factory.EnumAdapters1(i, out var adapter) == 0 && adapter != null)
            {
                try
                {
                    if (adapter.GetDesc1(out var desc) == 0)
                    {
                        // Filter out software / WARP adapters
                        if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0 &&
                            desc.VendorId != 0 &&
                            !desc.Description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                        {
                            var info = new GpuAdapterInfo
                            {
                                Name = desc.Description.Trim(),
                                VendorId = desc.VendorId,
                                DeviceId = desc.DeviceId,
                                Vendor = GpuVendorHelper.FromPciVendorId(desc.VendorId),
                                DedicatedVideoMemoryBytes = (ulong)desc.DedicatedVideoMemory,
                                AdapterIndex = (int)i,
                                AdapterLuid = desc.AdapterLuid.Value
                            };

                            if (info.Vendor == GpuVendor.Unknown)
                            {
                                info.Vendor = GpuVendorHelper.FromName(info.Name);
                            }

                            list.Add(info);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
                i++;
            }
            Marshal.ReleaseComObject(factory);
        }
        catch { }
        return list;
    }
}
