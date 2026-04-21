using System;
using System.Runtime.InteropServices;

namespace BezelFreeCorrection.Topology.Nvidia;

// Thin interop layer over nvapi64.dll. NVAPI exports a single entry point
// (nvapi_QueryInterface) that returns typed function pointers keyed by a
// pre-hashed interface id. Specific function delegates are obtained from
// Mosaic-specific wrapper classes; this file handles only initialization
// and the common primitives.
internal static class NvApi
{
    // Status codes are listed in nvapi_lite_common.h. Only codes that the
    // calling code branches on are surfaced here.
    public enum Status
    {
        Ok = 0,
        Error = -1,
        LibraryNotFound = -2,
        NoImplementation = -3,
        ApiNotInitialized = -4,
        InvalidArgument = -5,
        NvidiaDeviceNotFound = -6,
        EndEnumeration = -7,
        IncompatibleStructVersion = -9,
        MosaicNotActive = -160,
        NotSupported = -177,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NvRect
    {
        public uint Left;
        public uint Top;
        public uint Right;
        public uint Bottom;
    }

    public static uint MakeVersion<T>(int version) where T : struct =>
        (uint)(Marshal.SizeOf<T>() | (version << 16));

    // ids from nvapi_interface.h
    private const uint FuncIdInitialize = 0x0150e828;
    private const uint FuncIdUnload     = 0xd22bdd7e;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate Status InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate Status UnloadDelegate();

    private static InitializeDelegate? _initialize;
    private static UnloadDelegate?     _unload;
    private static bool _initialized;
    private static bool _loadAttempted;
    private static bool _libraryMissing;

    // Returns true when NVAPI is present, reachable, and initialized.
    // Silently returns false on systems without NVIDIA drivers or on AMD/Intel GPUs
    // so callers can fall back without catching exceptions.
    public static bool TryInitialize()
    {
        if (_initialized) return true;
        if (_loadAttempted && _libraryMissing) return false;
        _loadAttempted = true;

        try
        {
            _initialize ??= GetDelegate<InitializeDelegate>(FuncIdInitialize);
            _unload     ??= GetDelegate<UnloadDelegate>(FuncIdUnload);
        }
        catch (DllNotFoundException)
        {
            _libraryMissing = true;
            return false;
        }

        if (_initialize == null) return false;
        if (_initialize() != Status.Ok) return false;

        _initialized = true;
        return true;
    }

    public static void Unload()
    {
        if (!_initialized) return;
        _unload?.Invoke();
        _initialized = false;
    }

    // Resolves an NVAPI function pointer for the given hashed id and wraps it
    // as a managed delegate. Throws DllNotFoundException when nvapi64.dll is
    // not present (bubbled up by the native loader via QueryInterfaceRaw).
    public static T GetDelegate<T>(uint functionId) where T : Delegate
    {
        var ptr = QueryInterfaceRaw(functionId);
        if (ptr == IntPtr.Zero)
            throw new InvalidOperationException(
                $"NVAPI function id 0x{functionId:x8} not available in this driver.");
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterfaceRaw(uint id);
}
