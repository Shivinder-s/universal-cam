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
///
/// All mutable state is accessed exclusively on `queue` for thread safety.
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

    /// Header size for binary media frames: stream_type(1) + length(4) + pts(8) + flags(1)
    private static let frameHeaderSize = 14

    // MARK: - Callbacks

    var onControlMessage: ((Data) -> Void)?
    var onStateChange:    ((USBConnectionState) -> Void)?

    enum USBConnectionState: Equatable {
        case listening
        case connected
        case disconnected
        case failed(String)
    }

    // MARK: - Private (accessed on `queue`)

    private var listener: NWListener?
    private var connection: NWConnection?
    private let queue = DispatchQueue(label: "com.universalcam.usb", qos: .userInteractive)
    private var isListening = false

    /// Accumulation buffer for reassembling TCP segments into complete messages.
    private var receiveBuffer = Data()

    /// Flow control: number of sends in flight (not yet completed).
    private var sendsInFlight = 0
    private static let maxSendsInFlight = 30

    // MARK: - Listener

    /// Start listening for incoming TCP connections on the USB port.
    func startListening() {
        queue.async { [self] in
            guard !isListening else { return }

            let tcpOptions = NWProtocolTCP.Options()
            tcpOptions.noDelay = true

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
    }

    /// Stop listening and disconnect any active connection.
    func stopListening() {
        queue.async { [self] in
            disconnectInternal()
            listener?.cancel()
            listener = nil
            isListening = false
        }
    }

    /// Disconnect the current USB connection (but keep listening).
    func disconnect() {
        queue.async { [self] in
            disconnectInternal()
        }
    }

    /// Must be called on `queue`.
    private func disconnectInternal() {
        connection?.cancel()
        connection = nil
        receiveBuffer = Data()
        sendsInFlight = 0
        onStateChange?(.disconnected)
    }

    // MARK: - Connection Handling

    /// Must be called on `queue`.
    private func handleNewConnection(_ newConnection: NWConnection) {
        if connection != nil {
            print("[USBTransport] Rejecting additional connection — already connected")
            newConnection.cancel()
            return
        }

        connection = newConnection
        receiveBuffer = Data()
        sendsInFlight = 0

        newConnection.stateUpdateHandler = { [weak self] state in
            switch state {
            case .ready:
                self?.onStateChange?(.connected)
                print("[USBTransport] PC connected via USB")
            case .failed(let error):
                print("[USBTransport] Connection failed: \(error)")
                self?.connection = nil
                self?.receiveBuffer = Data()
                self?.sendsInFlight = 0
                self?.onStateChange?(.disconnected)
            case .cancelled:
                self?.connection = nil
                self?.receiveBuffer = Data()
                self?.sendsInFlight = 0
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
        guard let data = try? JSONEncoder().encode(message) else { return }
        var payload = Data([StreamType.control.rawValue])
        payload.append(data)
        payload.append(0x0A)
        queue.async { [self] in
            guard let conn = connection else { return }
            sendWithFlowControl(conn, data: payload)
        }
    }

    func sendVideoFrame(_ frame: VideoEncoder.EncodedFrame) {
        let flags: UInt8 = frame.isKeyframe ? 0x01 : 0x00
        let packet = makeFramePacket(streamType: .video, payload: frame.data, ptsUs: frame.ptsUs, flags: flags)
        queue.async { [self] in
            guard let conn = connection else { return }
            guard sendsInFlight < Self.maxSendsInFlight else { return }
            sendWithFlowControl(conn, data: packet)
        }
    }

    func sendAudioFrame(_ frame: AudioCapture.AudioFrame) {
        let flags = UInt8(frame.channels)
        let packet = makeFramePacket(streamType: .audio, payload: frame.data, ptsUs: frame.ptsUs, flags: flags)
        queue.async { [self] in
            guard let conn = connection else { return }
            guard sendsInFlight < Self.maxSendsInFlight else { return }
            sendWithFlowControl(conn, data: packet)
        }
    }

    /// Send data with backpressure tracking. Must be called on `queue`.
    private func sendWithFlowControl(_ conn: NWConnection, data: Data) {
        sendsInFlight += 1
        conn.send(content: data, completion: .contentProcessed { [weak self] error in
            guard let self else { return }
            self.sendsInFlight = max(0, self.sendsInFlight - 1)
            if let error {
                print("[USBTransport] Send error: \(error)")
            }
        })
    }

    /// Whether there is an active USB connection.
    var isConnected: Bool {
        connection != nil
    }

    // MARK: - Receive Loop with Reassembly

    private func receiveLoop(_ conn: NWConnection) {
        conn.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, isComplete, error in
            if let data, !data.isEmpty {
                self?.receiveBuffer.append(data)
                self?.processReceiveBuffer()
            }
            if isComplete || error != nil {
                return
            }
            self?.receiveLoop(conn)
        }
    }

    /// Consume complete messages from the receive buffer.
    private func processReceiveBuffer() {
        while !receiveBuffer.isEmpty {
            guard let firstByte = receiveBuffer.first else { break }

            if firstByte == StreamType.control.rawValue {
                guard let newlineIndex = receiveBuffer.dropFirst(1).firstIndex(of: 0x0A) else {
                    break
                }
                let jsonData = Data(receiveBuffer[receiveBuffer.startIndex + 1 ..< newlineIndex])
                receiveBuffer = Data(receiveBuffer[(newlineIndex + 1)...])
                if !jsonData.isEmpty {
                    onControlMessage?(jsonData)
                }

            } else if firstByte == StreamType.video.rawValue || firstByte == StreamType.audio.rawValue {
                guard receiveBuffer.count >= Self.frameHeaderSize else { break }
                let payloadLength = receiveBuffer.withUnsafeBytes { ptr -> Int in
                    let base = ptr.baseAddress!.advanced(by: 1)
                    return Int(base.loadUnaligned(as: UInt32.self).littleEndian)
                }
                let totalFrameSize = Self.frameHeaderSize + payloadLength
                guard receiveBuffer.count >= totalFrameSize else { break }
                receiveBuffer = Data(receiveBuffer.dropFirst(totalFrameSize))

            } else {
                if let newlineIndex = receiveBuffer.firstIndex(of: 0x0A) {
                    let lineData = Data(receiveBuffer[receiveBuffer.startIndex ..< newlineIndex])
                    receiveBuffer = Data(receiveBuffer[(newlineIndex + 1)...])
                    if !lineData.isEmpty {
                        onControlMessage?(lineData)
                    }
                } else {
                    break
                }
            }
        }
    }

    // MARK: - Helpers

    private func makeFramePacket(streamType: StreamType, payload: Data, ptsUs: Int64, flags: UInt8) -> Data {
        var packet = Data()
        packet.reserveCapacity(Self.frameHeaderSize + payload.count)
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
