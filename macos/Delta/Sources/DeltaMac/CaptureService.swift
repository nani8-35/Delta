import AppKit
import ScreenCaptureKit
import Vision
import UniformTypeIdentifiers

enum CaptureService {
    /// Uses normal macOS Screen Recording consent; no protected-content workarounds.
    static func capture() async throws -> CGImage {
        guard CGPreflightScreenCaptureAccess() || CGRequestScreenCaptureAccess() else {
            throw AccountError.message("Allow Screen Recording for Delta in System Settings, then reopen the app.")
        }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        guard let display = content.displays.first(where: { $0.displayID == CGMainDisplayID() }) ?? content.displays.first else {
            throw AccountError.message("No display is available.")
        }
        let filter = SCContentFilter(display: display, excludingWindows: [])
        let config = SCStreamConfiguration(); config.width = display.width; config.height = display.height
        config.showsCursor = false
        return try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
    }
    static func recognize(_ image: CGImage) async throws -> String {
        try await Task.detached(priority: .userInitiated) {
            let request = VNRecognizeTextRequest()
            request.recognitionLevel = .accurate; request.usesLanguageCorrection = false
            try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])
            return (request.results ?? []).compactMap { $0.topCandidates(1).first?.string }.joined(separator: "\n")
        }.value
    }
    @MainActor static func save(_ image: CGImage) async throws {
        let panel = NSSavePanel(); panel.allowedContentTypes = [.png]; panel.nameFieldStringValue = "Delta-screenshot.png"
        let response: NSApplication.ModalResponse = await withCheckedContinuation { continuation in
            panel.begin { continuation.resume(returning: $0) }
        }
        guard response == .OK, let url = panel.url else { return }
        guard let data = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else {
            throw AccountError.message("PNG encoding failed.")
        }
        try data.write(to: url, options: .atomic)
    }
}
