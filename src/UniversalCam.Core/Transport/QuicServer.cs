#pragma warning disable CA2252  // System.Net.Quic is preview in .NET 8; suppressed project-wide here
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UniversalCam.Core.Protocol;

namespace UniversalCam.Core.Transport;

/// <summary>
/// QUIC server (Wi-Fi transport). Listens on port 7779 for incoming connections
/// from the iPhone's QuicTransport.swift.
///
/// Uses System.Net.Quic (built into .NET 8) with a self-signed TLS certificate
/// for development. The iPhone's QuicTransport accepts any cert in DEBUG builds.
///
/// ALPN: "universalcam/1" — must match the iPhone's NWProtocolQUIC.Options(alpn:).
/// </summary>
public sealed class QuicServer : ITransport
{
    public const int Port = 7779;
    private const string Alpn = "universalcam/1";

    public event EventHandler<ControlMessage>? ControlMessageReceived;
    public event EventHandler<MediaFrame>?     FrameReceived;
    public event EventHandler<TransportState>? StateChanged;

    public TransportState State { get; private set; } = TransportState.Idle;

    private QuicListener?   _listener;
    private QuicConnection? _connection;
    private QuicStream?     _controlStream; // persistent PC→iPhone unidirectional stream
    private readonly FrameParser _parser = new();
    private CancellationTokenSource? _cts;

    public QuicServer()
    {
        _parser.ControlMessageParsed += (_, msg) => ControlMessageReceived?.Invoke(this, msg);
        _parser.FrameParsed          += (_, frame) => FrameReceived?.Invoke(this, frame);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (!QuicListener.IsSupported)
        {
            Console.WriteLine("[QuicServer] QUIC not supported on this platform. Falling back to TCP.");
            SetState(TransportState.Error);
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var cert = CreateSelfSignedCert();

        var listenerOptions = new QuicListenerOptions
        {
            ListenEndPoint           = new IPEndPoint(IPAddress.Any, Port),
            ApplicationProtocols     = new List<SslApplicationProtocol> { new(Alpn) },
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
            {
                DefaultStreamErrorCode    = 0,
                DefaultCloseErrorCode     = 0,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                {
                    ApplicationProtocols     = new List<SslApplicationProtocol> { new(Alpn) },
                    ServerCertificate        = cert,
                    ClientCertificateRequired = false,
                },
            }),
        };

        _listener = await QuicListener.ListenAsync(listenerOptions, _cts.Token);
        SetState(TransportState.Listening);
        Console.WriteLine($"[QuicServer] Listening on port {Port} (ALPN: {Alpn})");

        _ = AcceptLoopAsync(_cts.Token);
    }

    public async Task SendControlAsync(ControlMessage message, CancellationToken ct = default)
    {
        if (_controlStream is null) return;
        try
        {
            var json = message.ToJsonBytes();
            var packet = new byte[json.Length + 2];
            packet[0] = FrameHeader.StreamTypeControl;
            json.CopyTo(packet, 1);
            packet[^1] = 0x0A;
            await _controlStream.WriteAsync(packet, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QuicServer] Send failed: {ex.Message}");
        }
    }

    // MARK: - Private

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener is not null)
            {
                var connection = await _listener.AcceptConnectionAsync(ct);
                Console.WriteLine($"[QuicServer] iPhone connected: {connection.RemoteEndPoint}");

                // Only one connection at a time — close old control stream + connection
                _controlStream?.DisposeAsync().AsTask().Forget();
                _controlStream = null;
                _connection?.CloseAsync(0).AsTask().Forget();
                _connection = connection;

                // Open persistent unidirectional stream for all PC→iPhone control messages.
                // iOS advertises initialMaxStreamsUnidirectional=100, so this succeeds immediately.
                try
                {
                    _controlStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Unidirectional, ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[QuicServer] Failed to open control stream: {ex.Message}");
                    SetState(TransportState.Error);
                    continue;
                }

                SetState(TransportState.Connected);

                // Initiate handshake: iOS waits for welcome before sending hello
                _ = SendControlAsync(new Welcome());

                _ = ReadStreamsAsync(connection, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[QuicServer] Accept error: {ex.Message}");
            SetState(TransportState.Error);
        }
    }

    private async Task ReadStreamsAsync(QuicConnection connection, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var stream = await connection.AcceptInboundStreamAsync(ct);
                _ = ReadStreamAsync(stream, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[QuicServer] Stream accept error: {ex.Message}");
            SetState(TransportState.Disconnected);
            _parser.Reset();
        }
    }

    private async Task ReadStreamAsync(QuicStream stream, CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                _parser.Feed(buffer.AsSpan(0, read));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[QuicServer] Stream read error: {ex.Message}");
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=universalcam-dev",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        // Export and re-import with private key to make it usable for TLS
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }

    private void SetState(TransportState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_controlStream is not null) await _controlStream.DisposeAsync();
        if (_connection    is not null) await _connection.CloseAsync(0);
        if (_listener      is not null) await _listener.DisposeAsync();
    }
}

// Small helper to fire-and-forget tasks without warning CS4014
file static class TaskExtensions
{
    public static void Forget(this Task _) { }
}
