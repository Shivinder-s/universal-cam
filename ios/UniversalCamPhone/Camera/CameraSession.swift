import AVFoundation
import Combine

/// Describes a camera available on this device.
struct CameraInfo: Codable, Identifiable, Equatable {
    let id: String            // AVCaptureDevice.uniqueID
    let name: String          // Localised device name
    let position: String      // "front", "back", or "unspecified"
    let type: String          // e.g. "wide_angle"
}

/// Wraps AVCaptureSession and exposes camera state as @Published properties.
/// Owned by the app-level @StateObject; injected into views via @EnvironmentObject.
final class CameraSession: NSObject, ObservableObject {

    // MARK: - Public State

    @Published var isRunning: Bool = false
    @Published var currentCameraID: String = ""
    @Published var availableCameras: [CameraInfo] = []

    /// The underlying capture session — used by CameraPreviewView to attach the preview layer.
    let captureSession = AVCaptureSession()

    // MARK: - Private

    private let sessionQueue = DispatchQueue(label: "com.universalcam.camera.session")
    private var videoOutput  = AVCaptureVideoDataOutput()

    /// Called with each encoded video sample. Set by ConnectionManager.
    var onVideoSampleBuffer: ((CMSampleBuffer) -> Void)?

    // MARK: - Camera Discovery

    /// Discovers all available video cameras on this device.
    func discoverCameras() {
        let deviceTypes: [AVCaptureDevice.DeviceType] = [
            .builtInWideAngleCamera,
            .builtInUltraWideCamera,
            .builtInTelephotoCamera,
            .builtInDualCamera,
            .builtInDualWideCamera,
            .builtInTripleCamera,
            .builtInTrueDepthCamera
        ]

        let discovery = AVCaptureDevice.DiscoverySession(
            deviceTypes: deviceTypes,
            mediaType: .video,
            position: .unspecified
        )

        let cameras = discovery.devices.map { device in
            CameraInfo(
                id: device.uniqueID,
                name: device.localizedName,
                position: positionString(device.position),
                type: deviceTypeString(device.deviceType)
            )
        }

        DispatchQueue.main.async {
            self.availableCameras = cameras
            print("[CameraSession] Discovered \(cameras.count) cameras:")
            for cam in cameras {
                print("  - \(cam.name) (\(cam.type), \(cam.position)) id=\(cam.id)")
            }
        }
    }

    // MARK: - Lifecycle

    func start() {
        sessionQueue.async { [weak self] in
            guard let self else { return }
            self.discoverCameras()
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

    /// Switch to a specific camera by its unique ID.
    /// Calls the completion handler on the main queue with the CameraInfo on success, or nil on failure.
    func switchToCamera(withID uniqueID: String, completion: ((CameraInfo?) -> Void)? = nil) {
        sessionQueue.async { [weak self] in
            guard let self else {
                DispatchQueue.main.async { completion?(nil) }
                return
            }
            guard let device = AVCaptureDevice(uniqueID: uniqueID) else {
                print("[CameraSession] No device found with ID: \(uniqueID)")
                DispatchQueue.main.async { completion?(nil) }
                return
            }
            self.reconfigureInput(with: device)
            let info = CameraInfo(
                id: device.uniqueID,
                name: device.localizedName,
                position: self.positionString(device.position),
                type: self.deviceTypeString(device.deviceType)
            )
            DispatchQueue.main.async {
                self.currentCameraID = device.uniqueID
                completion?(info)
            }
        }
    }

    /// Legacy toggle between front and back (for backward compatibility).
    func switchCamera(completion: ((CameraInfo?) -> Void)? = nil) {
        sessionQueue.async { [weak self] in
            guard let self else {
                DispatchQueue.main.async { completion?(nil) }
                return
            }
            let currentDevice = self.currentVideoDevice()
            let currentPos = currentDevice?.position ?? .front
            let newPosition: AVCaptureDevice.Position = currentPos == .front ? .back : .front

            guard let device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: newPosition) else {
                DispatchQueue.main.async { completion?(nil) }
                return
            }
            self.reconfigureInput(with: device)
            let info = CameraInfo(
                id: device.uniqueID,
                name: device.localizedName,
                position: self.positionString(device.position),
                type: self.deviceTypeString(device.deviceType)
            )
            DispatchQueue.main.async {
                self.currentCameraID = device.uniqueID
                completion?(info)
            }
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

        // Start with the default front wide-angle camera
        if let device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .front) {
            addVideoInput(device: device)
            DispatchQueue.main.async { self.currentCameraID = device.uniqueID }
        } else if let device = AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back) {
            addVideoInput(device: device)
            DispatchQueue.main.async { self.currentCameraID = device.uniqueID }
        }

        addVideoOutput()

        captureSession.commitConfiguration()
    }

    private func addVideoInput(device: AVCaptureDevice) {
        guard let input = try? AVCaptureDeviceInput(device: device),
              captureSession.canAddInput(input) else { return }
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

        // Lock to landscape-right (sensor native) — rotation handled on the Windows side.
        // videoRotationAngle 0° = sensor native = landscape-right on iOS cameras.
        // The deprecated videoOrientation .landscapeRight is the equivalent for < iOS 17.
        if let connection = videoOutput.connection(with: .video) {
            if #available(iOS 17.0, *) {
                connection.videoRotationAngle = 0
            } else {
                connection.videoOrientation = .landscapeRight
            }
        }
    }

    private func reconfigureInput(with device: AVCaptureDevice) {
        captureSession.beginConfiguration()
        // Remove existing video inputs only
        captureSession.inputs
            .compactMap { $0 as? AVCaptureDeviceInput }
            .filter { $0.device.hasMediaType(.video) }
            .forEach { captureSession.removeInput($0) }
        addVideoInput(device: device)
        captureSession.commitConfiguration()
    }

    private func currentVideoDevice() -> AVCaptureDevice? {
        return captureSession.inputs
            .compactMap { $0 as? AVCaptureDeviceInput }
            .first { $0.device.hasMediaType(.video) }?
            .device
    }

    // MARK: - Helpers

    private func positionString(_ position: AVCaptureDevice.Position) -> String {
        switch position {
        case .front: return "front"
        case .back:  return "back"
        default:     return "unspecified"
        }
    }

    private func deviceTypeString(_ deviceType: AVCaptureDevice.DeviceType) -> String {
        if deviceType == .builtInWideAngleCamera     { return "wide_angle" }
        if deviceType == .builtInUltraWideCamera     { return "ultra_wide" }
        if deviceType == .builtInTelephotoCamera      { return "telephoto" }
        if deviceType == .builtInDualCamera           { return "dual" }
        if deviceType == .builtInDualWideCamera       { return "dual_wide" }
        if deviceType == .builtInTripleCamera         { return "triple" }
        if deviceType == .builtInTrueDepthCamera      { return "true_depth" }
        return "unknown"
    }
}

// MARK: - AVCaptureVideoDataOutputSampleBufferDelegate

extension CameraSession: AVCaptureVideoDataOutputSampleBufferDelegate {
    func captureOutput(
        _ output: AVCaptureOutput,
        didOutput sampleBuffer: CMSampleBuffer,
        from connection: AVCaptureConnection
    ) {
        onVideoSampleBuffer?(sampleBuffer)
    }

    func captureOutput(
        _ output: AVCaptureOutput,
        didDrop sampleBuffer: CMSampleBuffer,
        from connection: AVCaptureConnection
    ) {
        // Dropped frame — metrics can be added later
    }
}
