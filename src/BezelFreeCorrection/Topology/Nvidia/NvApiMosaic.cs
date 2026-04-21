using System;
using System.Runtime.InteropServices;

namespace BezelFreeCorrection.Topology.Nvidia;

// Mosaic-specific NVAPI structs and function wrappers. Structures and ids
// are taken verbatim from nvapi.h / nvapi_lite_surround.h / nvapi_interface.h
// in the NVIDIA/nvapi SDK. Only the subset needed to discover the physical
// panel layout inside an active Surround / Mosaic grid is declared.
internal static class NvApiMosaic
{
    public const int MaxDisplays = 64;

    // Version 1 per-display settings (width/height/bpp/freq). Used inside
    // GRID_TOPO_V2 which intentionally still embeds the V1 setting layout.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct DisplaySettingV1
    {
        public uint Version;
        public uint Width;
        public uint Height;
        public uint Bpp;
        public uint Freq;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct GridTopoDisplayV2
    {
        public uint Version;
        public uint DisplayId;
        public int  OverlapX;
        public int  OverlapY;
        public uint Rotation;
        public uint CloneGroup;
        public uint PixelShiftType;
    }

    // Mirrors NV_MOSAIC_GRID_TOPO_V2. The C header uses a 6-bit bitfield
    // followed by 26 reserved bits — all packed into one uint, which is how
    // we represent it here.
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct GridTopoV2
    {
        public uint Version;
        public uint Rows;
        public uint Columns;
        public uint DisplayCount;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxDisplays)]
        public GridTopoDisplayV2[] Displays;

        public DisplaySettingV1 DisplaySettings;
    }

    // Function ids from nvapi_interface.h.
    private const uint FuncIdEnumDisplayGrids               = 0xdf2887af;
    private const uint FuncIdGetDisplayViewportsByResolution = 0xdc6dc8d3;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvApi.Status EnumDisplayGridsDelegate(
        [In, Out] GridTopoV2[]? pGridTopologies,
        ref uint pGridCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate NvApi.Status GetDisplayViewportsByResolutionDelegate(
        uint displayId,
        uint srcWidth,
        uint srcHeight,
        [Out] NvApi.NvRect[] viewports,
        out byte bezelCorrected);

    private static EnumDisplayGridsDelegate?                _enumDisplayGrids;
    private static GetDisplayViewportsByResolutionDelegate? _getViewportsByResolution;

    // Returns the active Mosaic grids. An empty array means no Mosaic is
    // currently configured (the GPU still works, just as separate displays).
    public static GridTopoV2[] EnumDisplayGrids()
    {
        _enumDisplayGrids ??= NvApi.GetDelegate<EnumDisplayGridsDelegate>(FuncIdEnumDisplayGrids);

        uint count = 0;
        var status = _enumDisplayGrids(null, ref count);
        if (status != NvApi.Status.Ok || count == 0)
            return Array.Empty<GridTopoV2>();

        var grids = new GridTopoV2[count];
        var version = NvApi.MakeVersion<GridTopoV2>(2);
        for (var i = 0; i < grids.Length; i++)
        {
            grids[i].Version = version;
            grids[i].Displays = new GridTopoDisplayV2[MaxDisplays];
        }

        status = _enumDisplayGrids(grids, ref count);
        if (status != NvApi.Status.Ok) return Array.Empty<GridTopoV2>();

        if (count < grids.Length) Array.Resize(ref grids, (int)count);
        return grids;
    }

    // Returns per-panel viewports in desktop-topology-local coordinates for
    // the span that the given displayId belongs to. srcWidth/srcHeight = 0
    // requests the current resolution, which is what the calibration UI
    // always wants.
    public static NvApi.NvRect[] GetDisplayViewports(uint displayId)
    {
        _getViewportsByResolution ??=
            NvApi.GetDelegate<GetDisplayViewportsByResolutionDelegate>(FuncIdGetDisplayViewportsByResolution);

        var viewports = new NvApi.NvRect[MaxDisplays];
        var status = _getViewportsByResolution(displayId, 0, 0, viewports, out _);
        if (status != NvApi.Status.Ok) return Array.Empty<NvApi.NvRect>();

        return viewports;
    }
}
