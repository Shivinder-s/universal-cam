using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using UniversalCam.Core.Protocol;

namespace UniversalCam.Core.Transport;

/// <summary>
/// TCP server for USB-tunnelled connections from the iPhone.
///
/// When an iPhone is connected via USB, libimobiledevice / Apple Devices on Windows
/// creates a TCP tunnel: any connection to 127.0.0.1:{port} is forwarded to
/// port <see cref="Port"/> on the iPhone. Our iPhone app (USBTransport.swift) listens
/// on that port, so we connect outbound to localhost to reach it.
///
/// However, the iPhone app acts as the TCP *listener* and the PC must *connect*.
/// So we connect to 127.0.0.1:7780 (the libimobiledevice-forwarded port).
///
/// Wire protocol is identical to QuicTransport — same frame layout, same control JSON.
/// </summary>
public sealed class TcpUsbTransport : ITransport
{
    public const int Port = 7780;

    public event EventHandler<ControlMessage>? ControlMessageReceived;
    public event EventHandler<MediaFrame>?     FrameReceived;
    public event EventHandler<TransportState>? StateChanged;

    public TransportState State { get; private set; } = TransportState.Idle;

    private TcpClient?        _client;
    private NetworkStream?    _stream;
    private readonly FrameParser _parser = new();
    private CancellationTokenSource? _cts;

    public TcpUsbTransport()
    {
        _parser.ControlMessageParsed += (_, msg) => ControlMessageReceived?.Invoke(this, msg);
        _parser.FrameParsed          += (_, frame) => FrameReceived?.Invoke(this, frame);
    }

    /// Attempt to connect to the iPhone via the USB tunnel (localhost proxy).
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            SetState(TransportState.Listening); // "Listening" = waiting for USB plug-in / trying to connect
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _client = new TcpClient();
            await _client.ConnectAsync(IPAddress.Loopback, Port, _cts.Token);
            _stream = _client.GetStream();

            SetState(TransportState.Connected);
            _ = ReceiveLoopAsync(_cts.Token);
        }
        catch (OperationCanceledException) { SetState(TransportState.Idle); }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpUsbTransport] Connect failed: {ex.Message}");
            SetState(TransportState.Error);
        }
    }

    public async Task SendControlAsync(ControlMessage message, CancellationToken ct = default)
    {
        if (_stream is null) return;
        try
        {
            var json = message.ToJsonBytes();
            // Prefix with stream type 0x00, suffix with newline
            var packet = new byte[json.Length + 2];
            packet[0] = FrameHeader.StreamTypeControl;
            json.CopyTo(packet, 1);
            packet[^1] = 0x0A;
            await _stream.WriteAsync(packet, ct);
            await _stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpUsbTransport] Send failed: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (!ct.IsCancellationRequested && _stream is not null)
            {
                var read = await _stream.ReadAsync(buffer, ct);
                if (read == 0) break;
                _parser.Feed(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[TcpUsbTransport] Receive error: {ex.Message}");
        }
        finally
        {
            SetState(TransportState.Disconnected);
            _parser.Reset();
        }
    }

    private void SetState(TransportState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        await Task.CompletedTask;
    }
}
