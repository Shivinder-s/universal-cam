import Foundation
import Network
import Combine
import UIKit

/// Discovers Windows-side universal-cam instances on the LAN via Bonjour/mDNS.
/// Also advertises the phone so the Windows app can find it (bidirectional discovery).
///
/// Service type: _universalcam._tcp (must match Info.plist NSBonjourServices entry)
final class BonjourDiscovery: NSObject, ObservableObject {

    @Published var discovered: [String] = []   // Resolved host strings "192.168.x.x"

    private let browser  = NetServiceBrowser()
    private var services: [NetService] = []
    private var advertiser: NetService?

    // MARK: - Public

    func start() {
        browser.delegate = self
        browser.searchForServices(ofType: "_universalcam._tcp.", inDomain: "local.")
        advertisePhone()
    }

    func stop() {
        browser.stop()
        advertiser?.stop()
    }

    // MARK: - Private

    private func advertisePhone() {
        let service = NetService(
            domain: "local.",
            type: "_universalcam._tcp.",
            name: UIDevice.current.name,
            port: Int32(QuicTransport.port)
        )
        service.publish()
        self.advertiser = service
    }
}

// MARK: - NetServiceBrowserDelegate

extension BonjourDiscovery: NetServiceBrowserDelegate {
    func netServiceBrowser(_ browser: NetServiceBrowser, didFind service: NetService, moreComing: Bool) {
        service.delegate = self
        service.resolve(withTimeout: 5.0)
        services.append(service)
    }

    func netServiceBrowser(_ browser: NetServiceBrowser, didRemove service: NetService, moreComing: Bool) {
        services.removeAll { $0 == service }
    }
}

// MARK: - NetServiceDelegate

extension BonjourDiscovery: NetServiceDelegate {
    func netServiceDidResolveAddress(_ sender: NetService) {
        guard let addresses = sender.addresses else { return }
        for addressData in addresses {
            if let host = resolveIPv4(from: addressData) {
                DispatchQueue.main.async {
                    if !self.discovered.contains(host) {
                        self.discovered.append(host)
                    }
                }
                return
            }
        }
    }

    private func resolveIPv4(from data: Data) -> String? {
        data.withUnsafeBytes { ptr -> String? in
            let addr = ptr.baseAddress!.assumingMemoryBound(to: sockaddr.self)
            guard addr.pointee.sa_family == AF_INET else { return nil }
            var buf = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
            let sin = addr.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee }
            var addr4 = sin.sin_addr
            inet_ntop(AF_INET, &addr4, &buf, socklen_t(INET_ADDRSTRLEN))
            return String(cString: buf)
        }
    }
}
