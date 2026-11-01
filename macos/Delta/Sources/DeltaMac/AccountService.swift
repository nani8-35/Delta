import Foundation
import Security

enum AccountError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case let .message(value) = self { return value }; return nil }
}

/// Installation identity lives in Keychain. Passwords are never persisted.
enum InstallationIdentity {
    static func load() throws -> String {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "com.acranix.delta.mac", kSecAttrAccount as String: "installation"]
        var read = query; read[kSecReturnData as String] = true
        var item: CFTypeRef?
        let status = SecItemCopyMatching(read as CFDictionary, &item)
        if status == errSecSuccess, let data = item as? Data, let value = String(data: data, encoding: .utf8) { return value }
        guard status == errSecItemNotFound else { throw AccountError.message("Keychain access failed (\(status)).") }
        var bytes = [UInt8](repeating: 0, count: 32)
        guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess else {
            throw AccountError.message("Could not create the installation identity.")
        }
        let value = bytes.map { String(format: "%02x", $0) }.joined()
        var add = query; add[kSecValueData as String] = Data(value.utf8)
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        let write = SecItemAdd(add as CFDictionary, nil)
        if write == errSecDuplicateItem { return try load() }
        guard write == errSecSuccess else { throw AccountError.message("Could not save installation identity (\(write)).") }
        return value
    }
}

actor AccountService {
    struct User: Decodable, Sendable { let username: String; let active: Bool; let disabled: Bool? }
    private struct Reply: Decodable { let token: String?; let user: User?; let error: String? }
    private var token: String?
    private let session: URLSession
    init() {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 20; config.httpCookieStorage = nil
        session = URLSession(configuration: config)
    }
    private func request(_ path: String, body: [String: String]? = nil) async throws -> Reply {
        var request = URLRequest(url: URL(string: "https://delta.acranix.com/api/" + path)!)
        request.httpMethod = body == nil ? "GET" : "POST"
        if let body { request.httpBody = try JSONSerialization.data(withJSONObject: body); request.setValue("application/json", forHTTPHeaderField: "Content-Type") }
        if let token { request.setValue("Bearer " + token, forHTTPHeaderField: "Authorization") }
        let (data, response) = try await session.data(for: request)
        guard data.count <= 65536 else { throw AccountError.message("Account service response was too large.") }
        guard let response = response as? HTTPURLResponse,
              let reply = try? JSONDecoder().decode(Reply.self, from: data) else {
            throw AccountError.message("The account service returned an unexpected response.")
        }
        guard (200..<300).contains(response.statusCode) else {
            throw AccountError.message(String((reply.error ?? "Account request failed.").prefix(500)))
        }
        return reply
    }
    func login(username: String, password: String, device: String) async throws -> User {
        token = nil
        let reply = try await request("login", body: ["username": username, "password": password,
            "deviceSecret": device, "client": "desktop", "version": "2.1.11"])
        guard let candidate = reply.token, (20...1024).contains(candidate.count),
              candidate.range(of: "^[A-Za-z0-9_-]+$", options: .regularExpression) != nil else {
            throw AccountError.message("Account service returned an invalid session.")
        }
        token = candidate
        do { return try await validate() } catch { token = nil; throw error }
    }
    func validate() async throws -> User {
        guard token != nil else { throw AccountError.message("Sign in to use Delta.") }
        let reply = try await request("session")
        guard let user = reply.user, user.active, user.disabled != true else {
            throw AccountError.message("Account access is expired or paused.")
        }
        return user
    }
    func logout() async {
        _ = try? await request("logout", body: [:]); token = nil
    }
}
