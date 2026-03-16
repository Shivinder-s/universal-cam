import AVFoundation
import Accelerate

/// Captures microphone audio via AVAudioEngine and encodes to AAC-LC frames.
final class AudioCapture {

    // MARK: - Types

    struct AudioFrame {
        let data:     Data    // Raw AAC-LC frame
        let ptsUs:    Int64   // Presentation timestamp in microseconds
        let channels: Int     // 1 = mono, 2 = stereo
    }

    // MARK: - Public

    var onEncodedAudio: ((AudioFrame) -> Void)?

    // MARK: - Private

    private let engine        = AVAudioEngine()
    private var converter:    AVAudioConverter?
    private let outputFormat  = AVAudioFormat(commonFormat: .pcmFormatFloat32,
                                              sampleRate: 44100, channels: 2, interleaved: false)!

    // MARK: - Lifecycle

    func start() {
        let input = engine.inputNode
        let inputFormat = input.outputFormat(forBus: 0)

        // Install tap on input node — 1024 samples at native input format
        input.installTap(onBus: 0, bufferSize: 1024, format: inputFormat) { [weak self] buffer, time in
            self?.process(buffer: buffer, time: time)
        }

        do {
            try engine.start()
        } catch {
            print("[AudioCapture] Engine start failed: \(error)")
        }
    }

    func stop() {
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
    }

    // MARK: - Processing

    private func process(buffer: AVAudioPCMBuffer, time: AVAudioTime) {
        // TODO: Encode PCM → AAC-LC using AVAudioConverter (Phase 2)
        // For Phase 1 skeleton: pass raw PCM as placeholder until AAC encoding is wired up
        guard let channelData = buffer.floatChannelData else { return }
        let frameLength = Int(buffer.frameLength)
        let channelCount = Int(buffer.format.channelCount)
        var rawData = Data(count: frameLength * channelCount * MemoryLayout<Float>.size)
        rawData.withUnsafeMutableBytes { ptr in
            for ch in 0..<channelCount {
                let src = channelData[ch]
                let dst = ptr.baseAddress!.advanced(by: ch * frameLength * MemoryLayout<Float>.size)
                memcpy(dst, src, frameLength * MemoryLayout<Float>.size)
            }
        }
        let ptsUs = time.hostTime > 0
            ? Int64(Double(time.hostTime) / Double(NSEC_PER_MSEC) * 1000)
            : Int64(Date().timeIntervalSince1970 * 1_000_000)
        let frame = AudioFrame(data: rawData, ptsUs: ptsUs, channels: channelCount)
        onEncodedAudio?(frame)
    }
}
