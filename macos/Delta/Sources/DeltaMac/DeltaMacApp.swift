import SwiftUI
import AppKit
import WebKit
import Darwin

@main
@MainActor
struct DeltaMacApp: App {
    @StateObject private var model = AppModel()
    init() {
        if CommandLine.arguments.contains("--verify-resources") {
            guard let url = Bundle.main.url(forResource: "EditorBridge", withExtension: "js"),
                  let script = try? String(contentsOf: url, encoding: .utf8), script.contains("function(action, text)") else {
                fputs("Packaged editor bridge is missing.\n", stderr); exit(1)
            }
            print("Packaged editor bridge verified."); exit(0)
        }
    }
    var body: some Scene {
        WindowGroup("Delta Browser", id: "main") {
            Group {
                if model.username == nil { LoginView(model: model) }
                else { WorkspaceView(model: model) }
            }
            .frame(minWidth: 760, minHeight: 520)
            .background(WindowBinding(model: model))
            .preferredColorScheme(.dark)
        }
        .defaultSize(width: 1000, height: 680)
        .commands {
            CommandGroup(after: .newItem) {
                Button("New Delta tab") { model.addTab() }.keyboardShortcut("t", modifiers: [.command])
                Button("Close Delta tab") { if let tab = model.current { model.closeTab(tab) } }.keyboardShortcut("w", modifiers: [.command])
                Button("Show / hide Delta") { model.toggleWindow() }.keyboardShortcut("m", modifiers: [.command, .shift])
                Button("One typing step (current webpage)") { model.startTyping(manual: true) }.keyboardShortcut(",", modifiers: [.option])
            }
        }
        MenuBarExtra("Delta", systemImage: "triangle") {
            Button("Show Delta") { model.showWindow() }
            Button("Show / hide Delta") { model.toggleWindow() }
            Button("Turn click-through off") { model.clickThrough = false; model.showWindow() }
            Button("Pause typing") { model.pauseTyping() }
            Divider()
            Button("Quit Delta") { NSApp.terminate(nil) }
        }
    }
}

private struct WindowBinding: NSViewRepresentable {
    @ObservedObject var model: AppModel
    func makeNSView(context: Context) -> NSView { let view = NSView(); bind(view); return view }
    func updateNSView(_ nsView: NSView, context: Context) { bind(nsView) }
    private func bind(_ view: NSView) {
        DispatchQueue.main.async {
            if let window = view.window {
                model.window = window
                window.alphaValue = model.username == nil ? 1 : model.opacity
                window.level = model.floating ? .floating : .normal
                window.ignoresMouseEvents = model.clickThrough
                window.isRestorable = false
            }
        }
    }
}

private struct LoginView: View {
    @ObservedObject var model: AppModel
    @State private var username = ""
    @State private var password = ""
    @State private var reveal = false
    var body: some View {
        VStack(spacing: 18) {
            Image(systemName: "triangle").font(.system(size: 44)).foregroundStyle(.blue)
            Text("Delta Browser").font(.largeTitle.bold())
            Text("macOS development preview").foregroundStyle(.secondary)
            TextField("Username", text: $username).textFieldStyle(.roundedBorder)
            Group { if reveal { TextField("Password", text: $password) } else { SecureField("Password", text: $password) } }.textFieldStyle(.roundedBorder)
            Toggle("Show password", isOn: $reveal)
            Button(model.busy ? "Signing in…" : "Sign in") {
                let value = password; password = ""
                Task { await model.login(user: username, password: value) }
            }.buttonStyle(.borderedProminent).disabled(model.busy || username.isEmpty || password.isEmpty)
            Text(model.status).font(.callout).textSelection(.enabled)
            Link("Contact support", destination: URL(string: "mailto:delta@acranix.com")!)
        }.padding(40).frame(maxWidth: 440)
    }
}

private struct WorkspaceView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text("△ DELTA").bold()
                Button("Copy") { Task { await model.editorAction("copy") } }
                Button("Paste") { Task { await model.editorAction("insert") } }
                Button("Enter") { Task { await model.editorAction("submit") } }
                Spacer()
                Toggle("Float", isOn: $model.floating)
                Button("Sign out") { model.signOut() }
            }.padding(10).background(Color(red: 0.10, green: 0.15, blue: 0.22))
            HStack(spacing: 0) {
                VStack(alignment: .leading, spacing: 12) {
                    Text("Typing Master").foregroundStyle(.blue).font(.headline)
                    Text("In-page typing · \(Int((model.speed / 60 * 100).rounded()))%")
                    Slider(value: $model.speed, in: 1...60)
                    Button("Insert") { Task { await model.editorAction("insert") } }
                    Button("Start") { model.startTyping() }
                    Button("One character") { model.startTyping(manual: true) }
                    Button("Pause") { model.pauseTyping() }
                    Button("Reset") { model.resetTyping() }
                    Divider()
                    Text("Opacity · \(Int(model.opacity * 100))%")
                    Slider(value: $model.opacity, in: 0.1...1)
                    HStack { ForEach([1.0, 0.5, 0.1], id: \.self) { value in Button("\(Int(value * 100))%") { model.opacity = value } } }
                    Toggle("Click-through", isOn: $model.clickThrough)
                    Text("Use the menu-bar triangle to restore interaction.").font(.caption).foregroundStyle(.secondary)
                    Divider()
                    Button("Save screenshot") { Task { await model.capture(ocr: false) } }.disabled(model.busy)
                    Button("OCR screen…") { Task { await model.capture(ocr: true) } }.disabled(model.busy)
                    Spacer()
                    Text("External typing, cropped uploads, and voice are not available in this preview.").font(.caption).foregroundStyle(.secondary)
                }.buttonStyle(.bordered).padding(12).frame(width: 205).background(Color(red: 0.07, green: 0.12, blue: 0.18))
                VStack(spacing: 0) {
                    ScrollView(.horizontal) {
                        HStack {
                            ForEach(model.tabs) { tab in
                                HStack(spacing: 5) {
                                    Button { model.pauseTyping(); model.selection = tab.id } label: { Text(tab.title).lineLimit(1).frame(maxWidth: 150) }
                                    Button { model.closeTab(tab) } label: { Image(systemName: "xmark") }.help("Close tab")
                                }.padding(5).background(model.selection == tab.id ? Color.blue.opacity(0.35) : Color.clear).cornerRadius(5)
                            }
                            Button { model.addTab() } label: { Image(systemName: "plus") }
                        }.padding(6)
                    }
                    if let tab = model.current { BrowserPane(model: model, tab: tab).id(tab.id) }
                    else { Spacer(); Text("Open a tab to begin."); Spacer() }
                }
            }
            Text(model.status).font(.caption).frame(maxWidth: .infinity, alignment: .leading).padding(8).background(Color.blue.opacity(0.15))
        }
    }
}

private struct BrowserPane: View {
    @ObservedObject var model: AppModel
    @ObservedObject var tab: BrowserTab
    @State private var address = ""
    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Button { tab.webView.goBack() } label: { Image(systemName: "chevron.left") }.disabled(!tab.canGoBack)
                Button { tab.webView.goForward() } label: { Image(systemName: "chevron.right") }.disabled(!tab.canGoForward)
                Button { tab.webView.reload() } label: { Image(systemName: "arrow.clockwise") }
                TextField("Address or search", text: $address).textFieldStyle(.roundedBorder).onSubmit { model.navigate(address) }
            }.padding(8)
            WebContent(tab: tab)
        }.onAppear { address = tab.address }.onChange(of: tab.address) { address = tab.address }
    }
}

private struct WebContent: NSViewRepresentable {
    let tab: BrowserTab
    func makeNSView(context: Context) -> WKWebView { tab.webView }
    func updateNSView(_ nsView: WKWebView, context: Context) {}
}
