import Network
import Foundation

/// Low-level QUIC transport using Network.framework NWConnection.
/// Port 7779. Three logical streams multiplexed over one QUIC connection:
///   Stream 0 — Control  (JSON, newline-delimited, bidirectional)
///   Stream 1 — Video    (binary framed H.264 Annex B, phone → PC)
///   Stream 2 — Audio    (binary framed AAC-LC, phone → PC)
///
/// Wire frame format for video/audio streams:
///   [4 bytes: payload_length uint32 LE]
///   [8 bytes: pts_us int64 LE]
///   [1 byte:  flags]  video: bit0=keyframe, bit1=hevc; audio: channels (1 or 2)
///   [N bytes: payload]
final class QuicTransport {

    // MARK: - Constants

    static let port: UInt16 = 7779
    static let serviceType  = "_universalcam._tcp"

    // MARK: - Callbacks

    var onControlMessage: ((Data) -> Void)?
    var onStateChange:    ((NWConnection.State) -> Void)?

    // MARK: - Private

    private var connection: NWConnection?
    private let queue = DispatchQueue(label: "com.universalcam.quic", qos: .userInteractive)
    private var retryCount = 0

    // MARK: - Connection

    func connect(to host: String, port: UInt16 = QuicTransport.port) {
        let endpoint = NWEndpoint.hostPort(
            host: NWEndpoint.Host(host),
            port: NWEndpoint.Port(rawValue: port)!
        )
        let params = makeQUICParameters()
        let conn = NWConnection(to: endpoint, using: params)
        self.connection = conn
        conn.stateUpdateHandler = { [weak self] state in
            self?.onStateChange?(state)
            if case .failed = state { self?.scheduleReconnect(host: host, port: port) }
        }
        conn.start(queue: queue)
        receiveLoop(conn)
    }

    func disconnect() {
        connection?.cancel()
        connection = nil
        retryCount = 0
    }

    // MARK: - Send

    func sendControl(_ message: ControlMessage) {
        guard let data = try? JSONEncoder().encode(message),
              let conn = connection
        else { return }
        var payload = data
        payload.append(0x0A) // newline delimiter
        conn.send(content: payload, completion: .idempotent)
    }

    func sendVideoFrame(_ frame: VideoEncoder.EncodedFrame) {
        guard let conn = connection else { return }
        var flags: UInt8 = frame.isKeyframe ? 0x01 : 0x00
        let packet = makeFramePacket(payload: frame.data, ptsUs: frame.ptsUs, flags: flags)
        conn.send(content: packet, completion: .idempotent)
    }

    func sendAudioFrame(_ frame: AudioCapture.AudioFrame) {
        guard let conn = connection else { return }
        let flags = UInt8(frame.channels)
        let packet = makeFramePacket(payload: frame.data, ptsUs: frame.ptsUs, flags: flags)
        conn.send(content: packet, completion: .idempotent)
    }

    // MARK: - Receive Loop

    private func receiveLoop(_ conn: NWConnection) {
        conn.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, isComplete, error in
            if let data, !data.isEmpty {
                self?.onControlMessage?(data)
            }
            if !isComplete && error == nil {
                self?.receiveLoop(conn)
            }
        }
    }

    // MARK: - Helpers

    private func makeQUICParameters() -> NWParameters {
        // TODO: Replace with a real TLS certificate for production.
        // For development, use a self-signed cert or disable peer verification.
        let tlsOptions = NWProtocolTLS.Options()
        sec_protocol_options_set_verify_block(
            tlsOptions.securityProtocolOptions,
            { _, _, completion in completion(true) },  // Accept any cert in dev builds
            queue
        )
        let quicOptions = NWProtocolQUIC.Options(alpn: ["universalcam/1"])
        let params = NWParameters(quic: quicOptions)
        return params
    }

    /// Builds the binary frame header + payload.
    private func makeFramePacket(payload: Data, ptsUs: Int64, flags: UInt8) -> Data {
        var packet = Data()
        var length = UInt32(payload.count).littleEndian
        var pts    = ptsUs.littleEndian
        packet.append(contentsOf: withUnsafeBytes(of: &length) { Array($0) })
        packet.append(contentsOf: withUnsafeBytes(of: &pts)    { Array($0) })
        packet.append(flags)
        packet.append(payload)
        return packet
    }

    // MARK: - Reconnect

    private func scheduleReconnect(host: String, port: UInt16) {
        let delay = min(pow(2.0, Double(retryCount)), 30.0)
        retryCount += 1
        queue.asyncAfter(deadline: .now() + delay) { [weak self] in
            self?.connect(to: host, port: port)
        }
    }
}
