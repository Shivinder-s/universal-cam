import AVFoundation
import Combine

/// Wraps AVCaptureSession and exposes camera state as @Published properties.
/// Owned by the app-level @StateObject; injected into views via @EnvironmentObject.
final class CameraSession: NSObject, ObservableObject {

    // MARK: - Public State

    @Published var isRunning: Bool = false
    @Published var currentPosition: AVCaptureDevice.Position = .front

    /// The underlying capture session — used by CameraPreviewView to attach the preview layer.
    let captureSession = AVCaptureSession()

    // MARK: - Private

    private let sessionQueue = DispatchQueue(label: "com.universalcam.camera.session")
    private var videoOutput  = AVCaptureVideoDataOutput()
    private var audioOutput  = AVCaptureAudioDataOutput()

    /// Called with each encoded video sample. Set by ConnectionManager.
    var onVideoSampleBuffer: ((CMSampleBuffer) -> Void)?

    // MARK: - Lifecycle

    func start() {
        sessionQueue.async { [weak self] in
            guard let self else { return }
            self.configureCaptureSession()
            self.captureSession.startRunning()
            DispatchQueue.main.async { self.isRunning = true }
        }
    }

    func stop() {
        sessionQueue.async { [weak self] in
            guard let self else { return }
            self.captureSession.stopRunning()
            DispatchQueue.main.async { self.isRunning = false }
        }
    }

    func switchCamera() {
        sessionQueue.async { [weak self] in
            guard let self else { return }
            let newPosition: AVCaptureDevice.Position = self.currentPosition == .front ? .back : .front
            self.reconfigureInput(for: newPosition)
            DispatchQueue.main.async { self.currentPosition = newPosition }
        }
    }

    func setResolution(_ preset: AVCaptureSession.Preset) {
        sessionQueue.async { [weak self] in
            guard let self else { return }
            self.captureSession.beginConfiguration()
            if self.captureSession.canSetSessionPreset(preset) {
                self.captureSession.sessionPreset = preset
            }
            self.captureSession.commitConfiguration()
        }
    }

    // MARK: - Private Configuration

    private func configureCaptureSession() {
        captureSession.beginConfiguration()
        captureSession.sessionPreset = .hd1920x1080

        addVideoInput(for: currentPosition)
        addAudioInput()
        addVideoOutput()

        captureSession.commitConfiguration()
    }

    private func addVideoInput(for position: AVCaptureDevice.Position) {
        guard
            let device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: position),
            let input  = try? AVCaptureDeviceInput(device: device),
            captureSession.canAddInput(input)
        else { return }
        captureSession.addInput(input)
    }

    private func addAudioInput() {
        guard
            let device = AVCaptureDevice.default(for: .audio),
            let input  = try? AVCaptureDeviceInput(device: device),
            captureSession.canAddInput(input)
        else { return }
        captureSession.addInput(input)
    }

    private func addVideoOutput() {
        videoOutput.videoSettings = [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange
        ]
        videoOutput.setSampleBufferDelegate(self, queue: sessionQueue)
        videoOutput.alwaysDiscardsLateVideoFrames = true
        guard captureSession.canAddOutput(videoOutput) else { return }
        captureSession.addOutput(videoOutput)

        // Lock orientation — rotation is handled on the Windows side
        videoOutput.connection(with: .video)?.videoRotationAngle = 0
    }

    private func reconfigureInput(for position: AVCaptureDevice.Position) {
        captureSession.beginConfiguration()
        captureSession.inputs
            .compactMap { $0 as? AVCaptureDeviceInput }
            .filter { $0.device.hasMediaType(.video) }
            .forEach { captureSession.removeInput($0) }
        addVideoInput(for: position)
        captureSession.commitConfiguration()
    }
}

// MARK: - AVCaptureVideoDataOutputSampleBufferDelegate

extension CameraSession: AVCaptureVideoDataOutputSampleBufferDelegate {
    func captureOutput(
        _ output: AVCaptureOutput,
        didOutput sampleBuffer: CMSampleBuffer,
        from connection: AVCaptureConnection
    ) {
        // Forward raw YUV frames to VideoEncoder via callback
        onVideoSampleBuffer?(sampleBuffer)
    }

    func captureOutput(
        _ output: AVCaptureOutput,
        didDrop sampleBuffer: CMSampleBuffer,
        from connection: AVCaptureConnection
    ) {
        // TODO: surface dropped-frame metrics in Phase 2
    }
}
