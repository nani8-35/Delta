import Foundation

/// Pure session state, shared by manual stepping and automatic typing.
public struct TypingSession {
    private var characters: [Character] = []
    public private(set) var position = 0
    public var isComplete: Bool { position >= characters.count }
    public var count: Int { characters.count }
    public var next: String? { isComplete ? nil : String(characters[position]) }
    public init() {}
    public mutating func load(_ text: String) { characters = Array(text); position = 0 }
    public mutating func reset() { load("") }
    /// Call only after the destination confirms that insertion was accepted.
    public mutating func acknowledge() { if !isComplete { position += 1 } }
    public static func delay(speed: Double) -> TimeInterval { 1 / min(60, max(1, speed)) }
}
