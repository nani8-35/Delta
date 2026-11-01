import XCTest
@testable import DeltaCore

final class DeltaCoreTests: XCTestCase {
    func testTypingWhitespaceAndUnicode() {
        let code = "def palindrome(s):\r\n\treturn s == s[::-1] # 😀e\u{301}"
        var session = TypingSession(); session.load(code)
        var output = ""
        while let next = session.next { output += next; session.acknowledge() }
        XCTAssertEqual(output, code)
        XCTAssertNil(session.next)
    }
    func testCursorDoesNotAdvanceUntilAcknowledged() {
        var session = TypingSession(); session.load("abc")
        XCTAssertEqual(session.next, "a"); XCTAssertEqual(session.next, "a")
        session.acknowledge(); XCTAssertEqual(session.next, "b")
        session.reset(); XCTAssertTrue(session.isComplete); XCTAssertEqual(session.position, 0)
    }
    func testCRLFIsOneCharacter() {
        var session = TypingSession(); session.load("\r\n😀")
        XCTAssertEqual(session.count, 2); XCTAssertEqual(session.next, "\r\n")
        session.acknowledge(); XCTAssertEqual(session.next, "😀")
    }
    func testSpeedBounds() {
        XCTAssertEqual(TypingSession.delay(speed: 6), 1.0 / 6)
        XCTAssertEqual(TypingSession.delay(speed: 0), 1)
        XCTAssertEqual(TypingSession.delay(speed: 1000), 1.0 / 60)
    }
    func testAddressAndSearch() {
        XCTAssertEqual(NavigationInput.url(for: "chatgpt.com")?.host, "chatgpt.com")
        XCTAssertEqual(NavigationInput.url(for: "https://example.com/path")?.path, "/path")
        let result = NavigationInput.url(for: "palindrome Python & Unicode Ω")!
        XCTAssertEqual(URLComponents(url: result, resolvingAgainstBaseURL: false)?.queryItems?.first?.value,
                       "palindrome Python & Unicode Ω")
    }
    func testRejectUnsafeSchemesAndCredentials() {
        for input in ["javascript:alert(1)", "file:///etc/passwd", "data:text/html,test", "https://user:secret@example.com", ""] {
            XCTAssertNil(NavigationInput.url(for: input), input)
        }
    }
}
