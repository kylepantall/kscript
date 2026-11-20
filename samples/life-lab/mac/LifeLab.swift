// Life Lab for macOS: a small native shell around the KScript version.
//
// It starts `kscript serve` on the site bundled in the app (Contents/Resources/site) on a free local port and
// shows it in a native window (WKWebView). All of the program's logic is KScript: lib/life.ks.xml, the same
// script the terminal and browser versions use. Quitting the app stops the server.
//
// Built by make-app.sh; there is no Xcode project to open.

import Cocoa
import WebKit

final class AppDelegate: NSObject, NSApplicationDelegate, WKNavigationDelegate {
    private var window: NSWindow!
    private var webView: WKWebView!
    private var server: Process?
    private var serverLog = ""

    func applicationDidFinishLaunching(_ notification: Notification) {
        buildMenu()

        let config = WKWebViewConfiguration()
        webView = WKWebView(frame: .zero, configuration: config)
        webView.navigationDelegate = self
        webView.setValue(false, forKey: "drawsBackground") // no white flash before the dark page loads

        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1060, height: 960),
                          styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                          backing: .buffered, defer: false)
        window.title = "Life Lab"
        window.titlebarAppearsTransparent = true
        window.backgroundColor = NSColor(red: 0x0f / 255.0, green: 0x14 / 255.0, blue: 0x19 / 255.0, alpha: 1)
        window.contentView = webView
        window.setFrameAutosaveName("LifeLabWindow")
        window.center()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        do {
            let port = try freePort()
            try startServer(port: port)
            waitForServer(port: port, attempts: 100)
        } catch {
            fail("Life Lab couldn't start KScript.", detail: error.localizedDescription)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        server?.terminate()
        server?.waitUntilExit()
    }

    // MARK: kscript serve

    /// The kscript binary: the one bundled in the app, then $KSCRIPT, ~/.kscript/bin and the usual install folders.
    private func findKScript() -> String? {
        var candidates: [String] = []
        if let bundled = Bundle.main.url(forAuxiliaryExecutable: "kscript")?.path { candidates.append(bundled) }
        if let env = ProcessInfo.processInfo.environment["KSCRIPT"] { candidates.append(env) }
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        candidates += [home + "/.kscript/bin/kscript", "/opt/homebrew/bin/kscript", "/usr/local/bin/kscript"]
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    private func startServer(port: UInt16) throws {
        guard let kscript = findKScript() else {
            throw NSError(domain: "LifeLab", code: 1, userInfo: [NSLocalizedDescriptionKey:
                "No kscript found. Rebuild the app with make-app.sh (it bundles kscript), or install kscript to ~/.kscript/bin."])
        }
        guard let site = Bundle.main.resourceURL?.appendingPathComponent("site").path else {
            throw NSError(domain: "LifeLab", code: 2, userInfo: [NSLocalizedDescriptionKey: "The app has no site folder in its Resources."])
        }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: kscript)
        process.arguments = ["serve", site, "--port", String(port), "--host", "127.0.0.1"]
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        pipe.fileHandleForReading.readabilityHandler = { [weak self] handle in
            let text = String(decoding: handle.availableData, as: UTF8.self)
            DispatchQueue.main.async { self?.serverLog += text }
        }
        process.terminationHandler = { [weak self] p in
            DispatchQueue.main.async {
                guard let self = self else { return }
                if self.server === p && !self.quitting {
                    self.fail("KScript stopped (exit code \(p.terminationStatus)).", detail: self.serverLog)
                }
            }
        }
        try process.run()
        server = process
    }

    private var quitting = false

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        quitting = true
        return .terminateNow
    }

    /// Polls until kscript answers, then loads the page.
    private func waitForServer(port: UInt16, attempts: Int) {
        let url = URL(string: "http://127.0.0.1:\(port)/")!
        var request = URLRequest(url: url)
        request.timeoutInterval = 1
        URLSession.shared.dataTask(with: request) { [weak self] _, response, _ in
            DispatchQueue.main.async {
                guard let self = self else { return }
                if response != nil {
                    self.webView.load(URLRequest(url: url))
                } else if attempts > 0 && self.server?.isRunning == true {
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { self.waitForServer(port: port, attempts: attempts - 1) }
                } else if self.server?.isRunning == true {
                    self.fail("KScript didn't answer on port \(port).", detail: self.serverLog)
                }
            }
        }.resume()
    }

    /// Asks the system for a free TCP port on 127.0.0.1.
    private func freePort() throws -> UInt16 {
        let fd = socket(AF_INET, SOCK_STREAM, 0)
        guard fd >= 0 else { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
        defer { close(fd) }
        var addr = sockaddr_in()
        addr.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_addr.s_addr = inet_addr("127.0.0.1")
        addr.sin_port = 0
        var len = socklen_t(MemoryLayout<sockaddr_in>.size)
        let bound = withUnsafeMutablePointer(to: &addr) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(fd, $0, len) == 0 && getsockname(fd, $0, &len) == 0 }
        }
        guard bound else { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
        return UInt16(bigEndian: addr.sin_port)
    }

    // MARK: Window and menus

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        // Links to other sites open in the default browser.
        if let url = action.request.url, url.host != "127.0.0.1", url.scheme?.hasPrefix("http") == true {
            NSWorkspace.shared.open(url)
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }

    private func fail(_ message: String, detail: String) {
        let alert = NSAlert()
        alert.messageText = message
        alert.informativeText = String(detail.suffix(1500))
        alert.alertStyle = .critical
        alert.addButton(withTitle: "Quit")
        alert.runModal()
        NSApp.terminate(nil)
    }

    private func buildMenu() {
        let main = NSMenu()
        let appItem = NSMenuItem()
        main.addItem(appItem)
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "About Life Lab", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Hide Life Lab", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Quit Life Lab", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appItem.submenu = appMenu

        let viewItem = NSMenuItem()
        main.addItem(viewItem)
        let viewMenu = NSMenu(title: "View")
        viewMenu.addItem(withTitle: "Reload", action: #selector(reload), keyEquivalent: "r")
        viewMenu.addItem(withTitle: "Enter Full Screen", action: #selector(NSWindow.toggleFullScreen(_:)), keyEquivalent: "f")
        viewItem.submenu = viewMenu

        let windowItem = NSMenuItem()
        main.addItem(windowItem)
        let windowMenu = NSMenu(title: "Window")
        windowMenu.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowItem.submenu = windowMenu
        NSApp.windowsMenu = windowMenu

        NSApp.mainMenu = main
    }

    @objc private func reload() { webView.reload() }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
