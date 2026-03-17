using System;

namespace UniversalCam.VirtualCamera;

/// <summary>
/// Windows 10+ virtual camera server using DirectShow.
/// Requires a native C++ DLL (UniversalCam.VirtualCamera.DirectShow.dll) registered via regsvr32.
/// Communicates with the DLL via SharedMemoryBridge.
/// </summary>
internal sealed class DsVirtualCameraServer : IVirtualCameraServer
{
    private readonly FrameBuffer _frameBuffer;
    private readonly SharedMemoryBridge _shmBridge;
    private bool _disposed;

    public DsVirtualCameraServer(FrameBuffer frameBuffer, SharedMemoryBridge shmBridge)
    {
        _frameBuffer = frameBuffer ?? throw new ArgumentNullException(nameof(frameBuffer));
        _shmBridge = shmBridge ?? throw new ArgumentNullException(nameof(shmBridge));
        Initialize();
    }

    private void Initialize()
    {
        // DirectShow virtual camera initialization
        // For now: placeholder implementation
        // TODO: Check if DLL is registered; if not, prompt UAC elevation for regsvr32
        Console.WriteLine("[DsVirtualCameraServer] Initialized (Windows 10+)");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Console.WriteLine("[DsVirtualCameraServer] Disposing");
        _disposed = true;
    }
}
