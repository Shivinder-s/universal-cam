import Foundation
import Combine
import AVFoundation

/// Central state machine. Owns QuicTransport and BonjourDiscovery.
/// Wires CameraSession output → VideoEncoder → QuicTransport.
final class ConnectionManager: ObservableObject {

    // MARK: - State

    enum State: Equatable {
        case idle
        case discovering
        case connecting(host: String)
        case connected
        case streaming
        case error(String)
    }

    @Published var state:       State  = .idle
    @Published var peerHost:    String = ""
    @Published var latencyMs:   Int    = 0
    @Published var discoveredPeers: [String] = []

    // MARK: - Private

    private let transport  = QuicTransport()
    private let discovery  = BonjourDiscovery()
    private let encoder    = VideoEncoder()
    private let audioCapture = AudioCapture()
    private var pingTimer:  Timer?
    private var pingSentAt: Date?
    private var cancellables = Set<AnyCancellable>()

    // MARK: - Lifecycle

    init() {
        bindDiscovery()
        bindTransport()
    }

    // MARK: - Public API

    func startDiscovery() {
        state = .discovering
        discovery.start()
    }

    func connect(to host: String, port: UInt16 = 7779) {
        state = .connecting(host: host)
        peerHost = host
        transport.connect(to: host, port: port)
    }

    func disconnect() {
        stopStreaming()
        transport.disconnect()
        state = .idle
    }

    func startStreaming() {
        guard state == .connected else { return }
        let msg = ControlMessage.startStream
        transport.sendControl(msg)
        state = .streaming
        startPingTimer()
    }

    func stopStreaming() {
        guard state == .streaming else { return }
        let msg = ControlMessage.stopStream
        transport.sendControl(msg)
        state = .connected
        pingTimer?.invalidate()
    }

    /// Called by CameraSession.onVideoSampleBuffer
    func handleVideoSample(_ sampleBuffer: CMSampleBuffer) {
        guard state == .streaming else { return }
        encoder.encode(sampleBuffer)
    }

    // MARK: - Private Wiring

    private func bindDiscovery() {
        discovery.$discovered
            .receive(on: DispatchQueue.main)
            .assign(to: &$discoveredPeers)
    }

    private func bindTransport() {
        encoder.onEncodedFrame = { [weak self] frame in
            self?.transport.sendVideoFrame(frame)
        }
        audioCapture.onEncodedAudio = { [weak self] audioFrame in
            self?.transport.sendAudioFrame(audioFrame)
        }
    }

    // MARK: - Ping / Latency

    private func startPingTimer() {
        pingTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            guard let self else { return }
            self.pingSentAt = Date()
            let ts = Int64(Date().timeIntervalSince1970 * 1000)
            self.transport.sendControl(.ping(ts: ts))
        }
    }

    func handlePong(ts: Int64) {
        guard let sent = pingSentAt else { return }
        let rtt = Int(Date().timeIntervalSince(sent) * 1000)
        DispatchQueue.main.async { self.latencyMs = rtt / 2 }
    }

    // MARK: - Incoming Control Messages

    func handleIncomingControl(_ message: ControlMessage) {
        switch message {
        case .welcome:
            let hello = ControlMessage.hello(
                deviceName: UIDevice.current.name,
                capabilities: ["h264", "stereo_audio"]
            )
            transport.sendControl(hello)
            DispatchQueue.main.async { self.state = .connected }
        case .configureAck:
            break
        case .pong(let ts):
            handlePong(ts: ts)
        default:
            break
        }
    }
}

// MARK: - Control Message Types

enum ControlMessage: Codable {
    case hello(deviceName: String, capabilities: [String])
    case welcome
    case configure(resolution: String, fps: Int, codec: String, bitrate: Int)
    case configureAck(resolution: String, fps: Int)
    case startStream
    case stopStream
    case ping(ts: Int64)
    case pong(ts: Int64)

    private enum CodingKeys: String, CodingKey { case type, deviceName, capabilities,
        resolution, fps, codec, bitrate, ts }

    var type: String {
        switch self {
        case .hello:        return "hello"
        case .welcome:      return "welcome"
        case .configure:    return "configure"
        case .configureAck: return "configure_ack"
        case .startStream:  return "start_stream"
        case .stopStream:   return "stop_stream"
        case .ping:         return "ping"
        case .pong:         return "pong"
        }
    }
}
