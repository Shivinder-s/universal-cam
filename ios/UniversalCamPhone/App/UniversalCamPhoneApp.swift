import SwiftUI

@main
struct UniversalCamPhoneApp: App {

    @UIApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    @StateObject private var connectionManager = ConnectionManager()
    @StateObject private var cameraSession     = CameraSession()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(connectionManager)
                .environmentObject(cameraSession)
        }
    }
}
