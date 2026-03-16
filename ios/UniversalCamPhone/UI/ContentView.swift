import SwiftUI

struct ContentView: View {

    @EnvironmentObject var connection: ConnectionManager
    @EnvironmentObject var camera:     CameraSession

    @State private var showSettings = false

    var body: some View {
        ZStack {
            // Full-screen camera preview (blurred)
            CameraPreviewView(session: camera.captureSession)
                .ignoresSafeArea()
                .onAppear {
                    camera.start()
                    connection.startDiscovery()
                }
                .onDisappear { camera.stop() }

            // Status overlay
            VStack {
                HStack {
                    StatusPill(
                        state: connection.state,
                        latency: connection.latencyMs,
                        transport: connection.activeTransport
                    )
                    .padding(.top, 56)
                    .padding(.leading, 16)
                    Spacer()

                    // Settings
                    Button {
                        showSettings = true
                    } label: {
                        Image(systemName: "gearshape.fill")
                            .font(.system(size: 22))
                            .foregroundStyle(.white)
                            .padding(10)
                            .background(.ultraThinMaterial, in: Circle())
                    }
                    .padding(.top, 56)
                    .padding(.trailing, 16)
                }

                Spacer()

                // Connection hint when idle
                if case .idle = connection.state {
                    Text("Waiting for PC connection...")
                        .font(.callout)
                        .foregroundStyle(.white.opacity(0.7))
                        .padding(.bottom, 48)
                } else if case .discovering = connection.state {
                    VStack(spacing: 4) {
                        Text("Searching for PC on local network...")
                            .font(.callout)
                            .foregroundStyle(.white.opacity(0.7))
                        if connection.usbListening {
                            Text("Also listening for USB connection on port \(USBTransport.port)")
                                .font(.caption)
                                .foregroundStyle(.white.opacity(0.5))
                        }
                    }
                    .padding(.bottom, 48)
                }
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
    let state:     ConnectionManager.State
    let latency:   Int
    let transport: ConnectionManager.TransportType?

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(stateColor)
                .frame(width: 8, height: 8)
            Text(stateLabel)
                .font(.caption.bold())
                .foregroundStyle(.white)
            if let transport, state == .connected || state == .streaming {
                Text(transport.rawValue)
                    .font(.caption2.bold())
                    .foregroundStyle(.white.opacity(0.8))
                    .padding(.horizontal, 4)
                    .padding(.vertical, 1)
                    .background(
                        transport == .usb ? Color.blue.opacity(0.6) : Color.purple.opacity(0.6),
                        in: Capsule()
                    )
            }
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

private struct SettingsView: View {
    @EnvironmentObject var connection: ConnectionManager
    @EnvironmentObject var camera:     CameraSession
    @Environment(\.dismiss) var dismiss

    var body: some View {
        NavigationStack {
            Form {
                Section("Connection") {
                    if let transport = connection.activeTransport {
                        HStack {
                            Image(systemName: transport == .usb ? "cable.connector" : "wifi")
                            Text("Connected via \(transport.rawValue)")
                            Spacer()
                            Image(systemName: "checkmark.circle.fill")
                                .foregroundStyle(.green)
                        }
                    }

                    if connection.discoveredPeers.isEmpty && connection.activeTransport != .wifi {
                        HStack {
                            ProgressView()
                                .padding(.trailing, 8)
                            Text("Searching for Windows PC...")
                                .foregroundStyle(.secondary)
                        }
                    } else {
                        ForEach(connection.discoveredPeers, id: \.self) { peer in
                            Button {
                                connection.connect(to: peer)
                                dismiss()
                            } label: {
                                HStack {
                                    Image(systemName: "desktopcomputer")
                                    Text(peer)
                                    Spacer()
                                    if connection.peerHost == peer {
                                        Image(systemName: "checkmark.circle.fill")
                                            .foregroundStyle(.green)
                                    }
                                }
                            }
                        }
                    }
                }

                Section("USB") {
                    HStack {
                        Image(systemName: "cable.connector")
                        Text("USB Listener")
                        Spacer()
                        Text(connection.usbListening ? "Port \(USBTransport.port)" : "Inactive")
                            .foregroundStyle(.secondary)
                    }
                }

                Section("Status") {
                    HStack {
                        Text("State")
                        Spacer()
                        Text(stateDescription)
                            .foregroundStyle(.secondary)
                    }
                    if connection.latencyMs > 0 {
                        HStack {
                            Text("Latency")
                            Spacer()
                            Text("\(connection.latencyMs) ms")
                                .foregroundStyle(.secondary)
                        }
                    }
                }

                if connection.state != .idle {
                    Section {
                        Button("Disconnect", role: .destructive) {
                            connection.disconnect()
                            dismiss()
                        }
                    }
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

    private var stateDescription: String {
        switch connection.state {
        case .idle:              return "Not connected"
        case .discovering:       return "Searching..."
        case .connecting(let h): return "Connecting to \(h)"
        case .connected:         return "Ready"
        case .streaming:         return "Streaming"
        case .error(let msg):    return msg
        }
    }
}
