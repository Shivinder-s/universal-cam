import Network
import Foundation

/// TCP-based transport for USB connections.
///
/// When an iPhone is connected to a PC via USB cable, the PC-side app uses
/// a USB multiplexing tool (e.g. usbmuxd / libimobiledevice) to create a
/// TCP tunnel from a local PC port to a port on the iPhone.  From our side
/// we simply listen on a TCP port and accept the incoming connection.
///
/// Uses the same wire framing as QuicTransport:
///   [1 byte:  stream_type]
///   [4 bytes: payload_length LE]
///   [8 bytes: pts_us LE]
///   [1 byte:  flags]
///   [N bytes: payload]
final class USBTransport {

    // MARK: - Constants

    /// TCP port the phone listens on for USB-tunnelled connections from the PC.
    static let port: UInt16 = 7780

    /// Stream type identifiers (mirrors QuicTransport)
    private enum StreamType: UInt8 {
        case control = 0x00
        case video   = 0x01
        case audio   = 0x02
    }

    // MARK: - Callbacks

    var onControlMessage: ((Data) -> Void)?
    var onStateChange:    ((USBConnectionState) -> Void)?

    enum USBConnectionState: Equatable {
        case listening
        case connected
        case disconnected
        case failed(String)
    }

    // MARK: - Private

    private var listener: NWListener?
    private var connection: NWConnection?
    private let queue = DispatchQueue(label: "com.universalcam.usb", qos: .userInteractive)
    private var isListening = false

    // MARK: - Listener

    /// Start listening for incoming TCP connections on the USB port.
    func startListening() {
        guard !isListening else { return }

        let tcpOptions = NWProtocolTCP.Options()
        tcpOptions.noDelay = true  // Low latency for real-time streaming

        let params = NWParameters(tls: nil, tcp: tcpOptions)

        do {
            let listener = try NWListener(using: params, on: NWEndpoint.Port(rawValue: Self.port)!)
            self.listener = listener

            listener.stateUpdateHandler = { [weak self] state in
                switch state {
                case .ready:
                    self?.isListening = true
                    self?.onStateChange?(.listening)
                    print("[USBTransport] Listening on TCP port \(Self.port)")
                case .failed(let error):
                    self?.isListening = false
                    self?.onStateChange?(.failed(error.localizedDescription))
                    print("[USBTransport] Listener failed: \(error)")
                case .cancelled:
                    self?.isListening = false
                    print("[USBTransport] Listener cancelled")
                default:
                    break
                }
            }

            listener.newConnectionHandler = { [weak self] newConnection in
                self?.handleNewConnection(newConnection)
            }

            listener.start(queue: queue)
        } catch {
            print("[USBTransport] Failed to create listener: \(error)")
            onStateChange?(.failed(error.localizedDescription))
        }
    }

    /// Stop listening and disconnect any active connection.
    func stopListening() {
        disconnect()
        listener?.cancel()
        listener = nil
        isListening = false
    }

    /// Disconnect the current USB connection (but keep listening).
    func disconnect() {
        connection?.cancel()
        connection = nil
        onStateChange?(.disconnected)
    }

    // MARK: - Connection Handling

    private func handleNewConnection(_ newConnection: NWConnection) {
        // If we already have a connection, reject the new one
        // (only one PC should be connected via USB at a time)
        if connection != nil {
            print("[USBTransport] Rejecting additional connection — already connected")
            newConnection.cancel()
            return
        }

        connection = newConnection

        newConnection.stateUpdateHandler = { [weak self] state in
            switch state {
            case .ready:
                self?.onStateChange?(.connected)
                print("[USBTransport] PC connected via USB")
            case .failed(let error):
                print("[USBTransport] Connection failed: \(error)")
                self?.connection = nil
                self?.onStateChange?(.disconnected)
            case .cancelled:
                self?.connection = nil
                self?.onStateChange?(.disconnected)
                print("[USBTransport] PC disconnected (USB)")
            default:
                break
            }
        }

        newConnection.start(queue: queue)
        receiveLoop(newConnection)
    }

    // MARK: - Send

    func sendControl(_ message: ControlMessage) {
        guard let data = try? JSONEncoder().encode(message),
              let conn = connection
        else { return }
        var payload = Data([StreamType.control.rawValue])
        payload.append(data)
        payload.append(0x0A)
        conn.send(content: payload, completion: .idempotent)
    }

    func sendVideoFrame(_ frame: VideoEncoder.EncodedFrame) {
        guard let conn = connection else { return }
        let flags: UInt8 = frame.isKeyframe ? 0x01 : 0x00
        let packet = makeFramePacket(streamType: .video, payload: frame.data, ptsUs: frame.ptsUs, flags: flags)
        conn.send(content: packet, completion: .idempotent)
    }

    func sendAudioFrame(_ frame: AudioCapture.AudioFrame) {
        guard let conn = connection else { return }
        let flags = UInt8(frame.channels)
        let packet = makeFramePacket(streamType: .audio, payload: frame.data, ptsUs: frame.ptsUs, flags: flags)
        conn.send(content: packet, completion: .idempotent)
    }

    /// Whether there is an active USB connection.
    var isConnected: Bool {
        connection != nil
    }

    // MARK: - Receive Loop

    private func receiveLoop(_ conn: NWConnection) {
        conn.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, isComplete, error in
            if let data, !data.isEmpty {
                self?.demultiplex(data)
            }
            if !isComplete && error == nil {
                self?.receiveLoop(conn)
            }
        }
    }

    private func demultiplex(_ data: Data) {
        guard let firstByte = data.first else { return }

        if firstByte == StreamType.control.rawValue {
            let controlData = data.dropFirst()
            if !controlData.isEmpty {
                onControlMessage?(Data(controlData))
            }
        } else {
            // From the PC we only expect control messages
            onControlMessage?(data)
        }
    }

    // MARK: - Helpers

    private func makeFramePacket(streamType: StreamType, payload: Data, ptsUs: Int64, flags: UInt8) -> Data {
        var packet = Data()
        packet.append(streamType.rawValue)
        var length = UInt32(payload.count).littleEndian
        var pts    = ptsUs.littleEndian
        packet.append(contentsOf: withUnsafeBytes(of: &length) { Array($0) })
        packet.append(contentsOf: withUnsafeBytes(of: &pts)    { Array($0) })
        packet.append(flags)
        packet.append(payload)
        return packet
    }
}
