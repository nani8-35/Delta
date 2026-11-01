import Foundation

public enum NavigationInput {
    public static func url(for input: String) -> URL? {
        let value = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return nil }
        if let url = URL(string: value), let scheme = url.scheme?.lowercased() {
            guard ["https", "http"].contains(scheme), url.host != nil,
                  url.user == nil, url.password == nil else { return nil }
            return url
        }
        if !value.contains(where: { $0.isWhitespace }), value.contains("."),
           let url = URL(string: "https://" + value), url.host != nil,
           url.user == nil, url.password == nil { return url }
        var components = URLComponents(string: "https://www.google.com/search")!
        components.queryItems = [URLQueryItem(name: "q", value: value)]
        return components.url
    }
}
