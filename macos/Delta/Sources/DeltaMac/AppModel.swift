import AppKit
import Combine
import DeltaCore

@MainActor
final class AppModel: ObservableObject {
    @Published var username: String?
    @Published var busy = false
    @Published var status = "Sign in to use Delta."
    @Published var tabs: [BrowserTab] = []
    @Published var selection: UUID?
    @Published var opacity = 0.5 { didSet { window?.alphaValue = opacity } }
    @Published var floating = false { didSet { window?.level = floating ? .floating : .normal } }
    @Published var clickThrough = false { didSet { window?.ignoresMouseEvents = clickThrough } }
    @Published var speed = 6.0
    @Published var typing = false
    weak var window: NSWindow?
    private let account = AccountService()
    private var validation: Task<Void, Never>?
    private var typingTask: Task<Void, Never>?
    private var typingSession = TypingSession()
    private var typingGeneration = UUID()
    var current: BrowserTab? { tabs.first(where: { $0.id == selection }) }

    func login(user: String, password: String) async {
        guard !busy else { return }; busy = true; defer { busy = false }
        do {
            let device = try InstallationIdentity.load()
            let reply = try await account.login(username: user.trimmingCharacters(in: .whitespacesAndNewlines), password: password, device: device)
            username = reply.username
            for (title, address) in [("ChatGPT", "https://chatgpt.com"), ("Gemini", "https://gemini.google.com"),
                ("Claude", "https://claude.ai"), ("Google Meet", "https://meet.google.com"), ("Grok", "https://grok.com")] {
                addTab(title: title, url: URL(string: address)!, select: tabs.isEmpty)
            }
            status = "Ready. macOS preview: screenshot export, OCR, and in-page typing."
            validation?.cancel()
            validation = Task { [weak self] in
                while !Task.isCancelled {
                    do { try await Task.sleep(nanoseconds: 60_000_000_000) } catch { return }
                    guard let self, !Task.isCancelled else { return }
                    do { _ = try await self.account.validate() }
                    catch { if !Task.isCancelled { self.signOut(); self.status = "Session validation failed. Please sign in again." }; return }
                }
            }
        } catch { status = error.localizedDescription }
    }
    func signOut() {
        validation?.cancel(); validation = nil; pauseTyping()
        tabs.forEach { $0.stop() }; tabs.removeAll(); selection = nil; username = nil
        resetTyping(); clickThrough = false; floating = false; opacity = 1
        busy = true; status = "Signing out…"
        Task { await account.logout(); busy = false; status = "Sign in to use Delta." }
    }
    func addTab(title: String = "New tab", url: URL = URL(string: "https://chatgpt.com")!, select: Bool = true) {
        guard username != nil else { return }
        guard tabs.count < 30 else { status = "Close a tab before adding another."; return }
        let tab = BrowserTab(title: title, url: url)
        tab.onStatus = { [weak self] message in self?.status = message }
        tab.onNewTab = { [weak self] url in self?.addTab(url: url) }
        tabs.append(tab); if select { selection = tab.id }
    }
    func closeTab(_ tab: BrowserTab) {
        pauseTyping(); tab.stop(); let index = tabs.firstIndex(where: { $0.id == tab.id }) ?? 0
        tabs.removeAll { $0.id == tab.id }
        if tabs.isEmpty { addTab() }
        else if selection == tab.id { selection = tabs[min(index, tabs.count - 1)].id }
    }
    func navigate(_ input: String) {
        guard let url = NavigationInput.url(for: input) else { status = "Enter an HTTP(S) address or search query."; return }
        pauseTyping(); current?.webView.load(URLRequest(url: url))
    }
    func showWindow() {
        window?.ignoresMouseEvents = false; clickThrough = false
        window?.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
    }
    func toggleWindow() { if window?.isVisible == true { window?.orderOut(nil) } else { showWindow() } }

    func editorAction(_ action: String) async {
        guard let tab = current else { return }
        do {
            if action == "copy" {
                let text = try await tab.editor("copy") as? String ?? ""
                NSPasteboard.general.clearContents(); NSPasteboard.general.setString(text, forType: .string)
                status = "Selected webpage text copied."
            } else {
                let text = NSPasteboard.general.string(forType: .string) ?? ""
                let result = try await tab.editor(action, text: text)
                guard result as? Bool == true else { throw AccountError.message("No supported editor, or submission was not confirmed.") }
                status = action == "submit" ? "Submission requested." : "Clipboard text inserted."
            }
        } catch { status = error.localizedDescription }
    }
    func pauseTyping() { typingTask?.cancel(); if typingTask == nil { typing = false } }
    func resetTyping() {
        pauseTyping(); typingGeneration = UUID(); typingTask = nil; typing = false; typingSession.reset()
        status = "Typing buffer cleared; existing editor text was not deleted."
    }
    func startTyping(manual: Bool = false) {
        guard !typing, let tab = current else { return }
        if typingSession.count == 0 {
            guard let text = NSPasteboard.general.string(forType: .string), !text.isEmpty else { status = "Copy code first."; return }
            typingSession.load(text)
        }
        guard !typingSession.isComplete else { status = "Completed. Reset to load another copy."; return }
        let navigation = tab.navigation; let generation = typingGeneration; typing = true
        typingTask = Task { [weak self, weak tab] in
            guard let self, let tab else { return }
            defer { if self.typingGeneration == generation { self.typing = false; self.typingTask = nil } }
            do {
                repeat {
                    try Task.checkCancellation()
                    guard self.selection == tab.id, tab.navigation == navigation else { throw AccountError.message("Typing paused: the destination changed.") }
                    guard let text = self.typingSession.next else { break }
                    guard try await tab.editor("insert", text: text) as? Bool == true else { throw AccountError.message("Click a supported webpage editor before typing.") }
                    guard self.typingGeneration == generation else { return }
                    self.typingSession.acknowledge()
                    if manual || self.typingSession.isComplete { break }
                    try await Task.sleep(nanoseconds: UInt64(TypingSession.delay(speed: self.speed) * 1_000_000_000))
                } while true
                self.status = self.typingSession.isComplete ? "Typing completed." : "One character inserted."
            } catch is CancellationError { if self.typingGeneration == generation { self.status = "Typing paused. Start resumes." } }
            catch { if self.typingGeneration == generation { self.status = error.localizedDescription } }
        }
    }
    func capture(ocr: Bool) async {
        guard !busy, let tab = current else { return }; busy = true; defer { busy = false }
        let navigation = tab.navigation
        do {
            let image = try await CaptureService.capture()
            if !ocr { try await CaptureService.save(image); status = "Screenshot export finished."; return }
            let text = try await CaptureService.recognize(image)
            guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw AccountError.message("No readable text found.") }
            guard selection == tab.id, navigation == tab.navigation else { throw AccountError.message("OCR destination changed. Nothing was inserted.") }
            let alert = NSAlert(); alert.messageText = "Insert recognized text into the current webpage?"
            alert.informativeText = String(text.prefix(1500)); alert.addButton(withTitle: "Insert text"); alert.addButton(withTitle: "Cancel")
            guard alert.runModal() == .alertFirstButtonReturn else { status = "OCR canceled."; return }
            guard selection == tab.id, navigation == tab.navigation else { throw AccountError.message("OCR destination changed.") }
            guard try await tab.editor("insert", text: text) as? Bool == true else { throw AccountError.message("No supported editor found.") }
            status = "OCR text inserted. Review it before sending."
        } catch { status = error.localizedDescription }
    }
}
