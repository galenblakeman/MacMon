// Verify Sparkle's archive signature against the public key embedded in MacMon, before publishing.
import Foundation
import CryptoKit

do {
    guard CommandLine.arguments.count == 4,
          let key = Data(base64Encoded: CommandLine.arguments[1]),
          let signature = Data(base64Encoded: CommandLine.arguments[2]) else {
        throw NSError(domain: "MacMon", code: 1)
    }
    let publicKey = try Curve25519.Signing.PublicKey(rawRepresentation: key)
    let data = try Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[3]), options: .mappedIfSafe)
    guard publicKey.isValidSignature(signature, for: data) else { throw NSError(domain: "MacMon", code: 2) }
} catch {
    fputs("Update signature does not match SPARKLE_PUBLIC_ED_KEY or archive bytes.\n", stderr)
    exit(1)
}
