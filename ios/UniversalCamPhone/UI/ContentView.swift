import SwiftUI

struct ContentView: View {

    @EnvironmentObject var connection: ConnectionManager
    @EnvironmentObject var camera:     CameraSession

    @State private var showSettings = false

    var body: some View {
        ZStack {
            // Full-screen camera preview
            CameraPreviewView(session: camera.captureSession)
                .ignoresSafeArea()
                .onAppear  { camera.start() }
                .onDisappear { camera.stop() }

            // Status overlay
            VStack {
                HStack {
                    Spacer()
                    StatusPill(state: connection.state, latency: connection.latencyMs)
                        .padding(.top, 56)
                        .padding(.trailing, 16)
                }
                Spacer()

                // Bottom controls
                HStack(spacing: 24) {
                    // Flip camera
                    Button {
                        camera.switchCamera()
                    } label: {
                        Image(systemName: "arrow.triangle.2.circlepath.camera")
                            .font(.system(size: 28))
                            .foregroundStyle(.white)
                    }

                    // Stream toggle
                    StreamToggleButton(state: connection.state) {
                        switch connection.state {
                        case .idle, .error:
                            connection.startDiscovery()
                        case .connected:
                            connection.startStreaming()
                        case .streaming:
                            connection.stopStreaming()
                        default:
                            break
                        }
                    }

                    // Settings
                    Button {
                        showSettings = true
                    } label: {
                        Image(systemName: "gearshape")
                            .font(.system(size: 28))
                            .foregroundStyle(.white)
                    }
                }
                .padding(.bottom, 48)
            }
        }
        .sheet(isPresented: $showSettings) {
            SettingsView()
                .environmentObject(connection)
                .environmentObject(camera)
        }
    }
}

// MARK: - Sub-views

private struct StatusPill: View {
    let state:   ConnectionManager.State
    let latency: Int

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(stateColor)
                .frame(width: 8, height: 8)
            Text(stateLabel)
                .font(.caption.bold())
                .foregroundStyle(.white)
            if state == .streaming, latency > 0 {
                Text("\(latency)ms")
                    .font(.caption2)
                    .foregroundStyle(.white.opacity(0.8))
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background(.ultraThinMaterial, in: Capsule())
    }

    private var stateColor: Color {
        switch state {
        case .streaming:         return .green
        case .connected:         return .yellow
        case .connecting:        return .orange
        case .discovering:       return .blue
        case .error:             return .red
        case .idle:              return .gray
        }
    }

    private var stateLabel: String {
        switch state {
        case .idle:              return "Not connected"
        case .discovering:       return "Searching..."
        case .connecting(let h): return "Connecting to \(h)"
        case .connected:         return "Connected"
        case .streaming:         return "Streaming"
        case .error(let msg):    return "Error: \(msg)"
        }
    }
}

private struct StreamToggleButton: View {
    let state:   ConnectionManager.State
    let action:  () -> Void

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .fill(state == .streaming ? Color.red : Color.white)
                    .frame(width: 72, height: 72)
                if state == .streaming {
                    RoundedRectangle(cornerRadius: 4)
                        .fill(.white)
                        .frame(width: 24, height: 24)
                } else {
                    Circle()
                        .fill(Color.red)
                        .frame(width: 56, height: 56)
                }
            }
        }
    }
}

private struct SettingsView: View {
    @EnvironmentObject var connection: ConnectionManager
    @EnvironmentObject var camera:     CameraSession
    @Environment(\.dismiss) var dismiss

    var body: some View {
        NavigationStack {
            Form {
                Section("Discovered Devices") {
                    if connection.discoveredPeers.isEmpty {
                        Text("Searching for Windows PC...")
                            .foregroundStyle(.secondary)
                    } else {
                        ForEach(connection.discoveredPeers, id: \.self) { peer in
                            Button(peer) {
                                connection.connect(to: peer)
                                dismiss()
                            }
                        }
                    }
                }
                Section("Resolution") {
                    // TODO: Wire to CameraSession.setResolution in Phase 2
                    Text("1080p (default)")
                        .foregroundStyle(.secondary)
                }
            }
            .navigationTitle("Settings")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button("Done") { dismiss() }
                }
            }
        }
    }
}
