using System;

namespace UniversalCam.VirtualCamera;

/// <summary>
/// Windows 11 22H2+ virtual camera server using Media Foundation.
/// Uses IMFVirtualCamera for immediate device registration without driver signing.
/// </summary>
internal sealed class MfVirtualCameraServer : IVirtualCameraServer
{
    private readonly FrameBuffer _frameBuffer;
    private bool _disposed;

    public MfVirtualCameraServer(FrameBuffer frameBuffer)
    {
        _frameBuffer = frameBuffer ?? throw new ArgumentNullException(nameof(frameBuffer));
        Initialize();
    }

    private void Initialize()
    {
        // Media Foundation virtual camera initialization
        // For now: placeholder implementation
        // TODO: Implement IMFVirtualCamera integration using CsWin32-generated P/Invoke
        Console.WriteLine("[MfVirtualCameraServer] Initialized (Windows 11 22H2+)");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Console.WriteLine("[MfVirtualCameraServer] Disposing");
        _disposed = true;
    }
}
