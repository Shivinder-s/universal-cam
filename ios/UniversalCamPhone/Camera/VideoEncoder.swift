import VideoToolbox
import CoreMedia

/// Encodes raw YUV CMSampleBuffers to H.264 Annex B NAL units using VideoToolbox hardware encoder.
final class VideoEncoder {

    // MARK: - Types

    struct EncodedFrame {
        let data:      Data      // H.264 Annex B NAL unit(s)
        let ptsUs:     Int64     // Presentation timestamp in microseconds
        let isKeyframe: Bool
    }

    // MARK: - Public

    /// Called on the encoder's private queue for each encoded frame.
    var onEncodedFrame: ((EncodedFrame) -> Void)?

    // MARK: - Private

    private var session:      VTCompressionSession?
    private let encoderQueue  = DispatchQueue(label: "com.universalcam.videoencoder")
    private var frameCount    = 0

    // MARK: - Lifecycle

    func prepare(width: Int32, height: Int32, fps: Int32 = 30, bitrate: Int = 8_000_000) {
        encoderQueue.async { [weak self] in
            self?.createSession(width: width, height: height, fps: fps, bitrate: bitrate)
        }
    }

    func encode(_ sampleBuffer: CMSampleBuffer) {
        guard let session, let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        let dur = CMSampleBufferGetDuration(sampleBuffer)

        var flags: VTEncodeInfoFlags = []
        // Force keyframe every 2 seconds
        let forceKeyframe = frameCount % 60 == 0
        let frameProperties: CFDictionary? = forceKeyframe
            ? [kVTEncodeFrameOptionKey_ForceKeyFrame: true] as CFDictionary
            : nil

        VTCompressionSessionEncodeFrame(session, imageBuffer: pixelBuffer, presentationTimeStamp: pts,
                                        duration: dur, frameProperties: frameProperties,
                                        infoFlagsOut: &flags, outputHandler: nil)
        frameCount += 1
    }

    func flush() {
        guard let session else { return }
        VTCompressionSessionCompleteFrames(session, untilPresentationTimeStamp: .invalid)
    }

    func invalidate() {
        guard let session else { return }
        VTCompressionSessionInvalidate(session)
        self.session = nil
    }

    // MARK: - Private

    private func createSession(width: Int32, height: Int32, fps: Int32, bitrate: Int) {
        var s: VTCompressionSession?
        let status = VTCompressionSessionCreate(
            allocator: nil,
            width: width,
            height: height,
            codecType: kCMVideoCodecType_H264,
            encoderSpecification: nil,
            imageBufferAttributes: nil,
            compressedDataAllocator: nil,
            outputCallback: outputCallback,
            refcon: Unmanaged.passUnretained(self).toOpaque(),
            compressionSessionOut: &s
        )
        guard status == noErr, let s else {
            print("[VideoEncoder] Failed to create VTCompressionSession: \(status)")
            return
        }
        VTSessionSetProperty(s, key: kVTCompressionPropertyKey_RealTime,          value: kCFBooleanTrue)
        VTSessionSetProperty(s, key: kVTCompressionPropertyKey_AllowFrameReordering, value: kCFBooleanFalse)
        VTSessionSetProperty(s, key: kVTCompressionPropertyKey_ProfileLevel,      value: kVTProfileLevel_H264_Baseline_AutoLevel)
        VTSessionSetProperty(s, key: kVTCompressionPropertyKey_AverageBitRate,    value: bitrate as CFNumber)
        VTSessionSetProperty(s, key: kVTCompressionPropertyKey_ExpectedFrameRate, value: fps as CFNumber)
        VTSessionSetProperty(s, key: kVTCompressionPropertyKey_H264EntropyMode,   value: kVTH264EntropyMode_CABAC)
        VTCompressionSessionPrepareToEncodeFrames(s)
        self.session = s
    }
}

// MARK: - VTCompressionOutputCallback (C function)

private func outputCallback(
    outputCallbackRefCon: UnsafeMutableRawPointer?,
    sourceFrameRefCon: UnsafeMutableRawPointer?,
    status: OSStatus,
    infoFlags: VTEncodeInfoFlags,
    sampleBuffer: CMSampleBuffer?
) {
    guard
        status == noErr,
        let sampleBuffer,
        CMSampleBufferDataIsReady(sampleBuffer),
        let refCon = outputCallbackRefCon
    else { return }

    let encoder = Unmanaged<VideoEncoder>.fromOpaque(refCon).takeUnretainedValue()
    let isKeyframe = !CFDictionaryContainsKey(
        CMSampleBufferGetAttachments(sampleBuffer, attachmentMode: .shouldPropagate),
        kCMSampleAttachmentKey_NotSync
    )

    guard let dataBuffer = CMSampleBufferGetDataBuffer(sampleBuffer) else { return }
    var totalLength = 0
    var dataPointer: UnsafeMutablePointer<Int8>?
    CMBlockBufferGetDataPointer(dataBuffer, atOffset: 0, lengthAtOffsetOut: nil,
                                totalLengthOut: &totalLength, dataPointerOut: &dataPointer)
    guard let ptr = dataPointer else { return }

    // Convert AVCC (length-prefixed) to Annex B (start-code prefixed)
    var annexB = Data()
    var offset = 0
    while offset < totalLength {
        // Read 4-byte AVCC length prefix (big-endian)
        var nalLength: UInt32 = 0
        memcpy(&nalLength, ptr + offset, 4)
        nalLength = CFSwapInt32BigToHost(nalLength)
        offset += 4
        // Write Annex B start code
        annexB.append(contentsOf: [0x00, 0x00, 0x00, 0x01])
        annexB.append(Data(bytes: ptr + offset, count: Int(nalLength)))
        offset += Int(nalLength)
    }

    let ptsUs = Int64(CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sampleBuffer)) * 1_000_000)
    let frame = VideoEncoder.EncodedFrame(data: annexB, ptsUs: ptsUs, isKeyframe: isKeyframe)
    encoder.onEncodedFrame?(frame)
}
