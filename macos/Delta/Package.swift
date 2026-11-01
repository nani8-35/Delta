// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "DeltaMac",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "DeltaMac", targets: ["DeltaMac"])],
    targets: [
        .target(name: "DeltaCore"),
        .executableTarget(name: "DeltaMac", dependencies: ["DeltaCore"], resources: [.process("Resources")]),
        .testTarget(name: "DeltaCoreTests", dependencies: ["DeltaCore"])
    ]
)
