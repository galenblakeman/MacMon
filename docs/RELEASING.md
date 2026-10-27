# Publishing and automatic updates

MacMon uses GitHub Actions to build a stable version tag on native Apple Silicon
and Intel runners, sign and notarize both apps, sign the final ZIPs with Sparkle,
and publish a complete GitHub Release. Sparkle 2.10.1 is pinned by archive SHA-256
in `tools/bootstrap-sparkle.sh`.

Release apps check for updates automatically. **Settings → General → Updates**
offers a manual check and an automatic-check preference; Save applies the preference
and Cancel leaves it alone. Sparkle stores that preference in macOS defaults.
Installation requires user confirmation; Sparkle also offers users its own option
to install future updates automatically. Downloaded archives must pass Ed25519
signature verification before extraction. Update-triggered relaunch waits for
MacMon's normal settings/history save and capture-report drain.

Development builds omit Sparkle and update networking. Existing installations that
predate this integration need one manual installation of an update-enabled release.
Install the app in `/Applications` and launch it there before testing updates.

## One-time GitHub configuration

The repository must be public: Sparkle fetches releases without a GitHub token.
Never embed a GitHub token or signing private key in MacMon.

In GitHub Settings → Environments, create an environment named **release**. Restrict
deployments to the release tags you intend to allow. Required reviewers are optional;
enabling them adds an approval step to otherwise automatic publishing.

Add these environment variables:

| Variable | Value |
| --- | --- |
| `DEVELOPER_ID_APPLICATION` | Exact `Developer ID Application: … (TEAMID)` signing identity |
| `SPARKLE_PUBLIC_ED_KEY` | Base64 public key printed by Sparkle's `generate_keys` |

Add these environment secrets:

| Secret | Value |
| --- | --- |
| `APPLE_CERTIFICATE_P12_BASE64` | Base64 of a password-protected exported Developer ID Application certificate **with its private key** |
| `APPLE_CERTIFICATE_PASSWORD` | Password of that P12 file |
| `APPLE_NOTARY_KEY_ID` | App Store Connect API key ID authorized for notarization |
| `APPLE_NOTARY_ISSUER_ID` | API issuer ID |
| `APPLE_NOTARY_PRIVATE_KEY` | Full contents of the matching `.p8` API private key |
| `SPARKLE_PRIVATE_ED_KEY` | Full contents of the private key file exported by Sparkle |

The workflow uses a temporary unlocked keychain on disposable macOS runners, deletes
signing files and the keychain in an `always()` cleanup step, and grants release
write permission only to the publishing job. No personal GitHub token is required.

To create a Sparkle key, run on your development Mac:

```sh
bash tools/bootstrap-sparkle.sh
artifacts/sparkle/2.10.1/bin/generate_keys --account MacMon
artifacts/sparkle/2.10.1/bin/generate_keys --account MacMon -x /private/tmp/macmon-sparkle-private.key
```

Store the exported file securely, copy its contents into `SPARKLE_PRIVATE_ED_KEY`,
then delete the temporary file. Keep a secure backup of the key. Keep the public
key stable between releases; changing it requires Sparkle's key-rotation process.
Do not commit private keys, P12 files or `.p8` files. The public key is embedded in
the signed app and is safe to publish.

See [Sparkle setup](https://sparkle-project.org/documentation/),
[publishing updates](https://sparkle-project.org/documentation/publishing/), and
[Apple notarization credentials](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).

## Publish a version

1. Increase **both** `Version` and `MacMonBundleBuild` in
   `src/MacMon/MacMon.csproj`. Use a stable `MAJOR.MINOR.PATCH` version and a
   monotonically increasing positive integer build number.
2. Commit and push the reviewed changes, including the workflow and release tools.
3. Create and push the matching version tag, for example:

   ```sh
   git tag v0.14.0
   git push origin v0.14.0
   ```

The example tag assumes the project's Version was changed to `0.14.0` first.
The **Publish MacMon** workflow also supports manual dispatch when selecting a
version tag; dispatching a branch does not publish anything.

Both native jobs rebuild the pinned MonCore package, run the verification suite,
build a self-contained app, sign nested code and the bundle with hardened runtime,
notarize with Apple, staple and assess the app, and create a ZIP with checksum.
The final stapled ZIP is signed with the Sparkle private key; a separate CryptoKit
verification checks that signature against the configured public key.

The publishing job requires matching versions, build numbers, source commits,
bundled public keys and architecture-specific feeds. It rejects an equal or older
version/build than the previous update-enabled release. It uploads both ZIPs,
checksums, manifests and feeds into a **draft** release, downloads and compares the
uploaded bytes, then publishes the release and marks it latest. A failed upload
leaves a draft; users continue to see the previous stable release. Release runs
are serialized so concurrent tags cannot race to become latest.

Public feeds:

- [Apple Silicon](https://github.com/galenblakeman/MacMon/releases/latest/download/appcast-osx-arm64.xml)
- [Intel](https://github.com/galenblakeman/MacMon/releases/latest/download/appcast-osx-x64.xml)

Each feed points to an immutable version-tagged ZIP for its own architecture.
Do not manually mark releases without both appcasts as latest; that would break
the feed URLs. Prerelease tags/channels are not supported by this workflow.

## Recover from a failure

Fix a transient signing, notarization or upload problem and rerun the failed jobs
on the same tag. An existing draft can be completed. Published releases cannot be
overwritten by the tooling: ship a new version/build instead. Do not move an
existing published tag or lower the build number. If a release needs to be rolled
back, ship the previous code under a new, higher version/build so Sparkle can offer it.

Notarization failures leave diagnostic JSON in the build runner's local staging
directory. Successful release artifacts are retained in Actions for seven days
and attached permanently to the GitHub Release. Only the public ZIP, checksum,
appcast and versioned release manifest are uploaded; signing material is excluded.

## Local verification

```sh
bash tools/bootstrap-sparkle.sh
bash tools/verify.sh --smoke
```

The Python suite tests release orchestration and update metadata/publication
failures. When Sparkle is bootstrapped on macOS it additionally signs a test ZIP
with a public RFC 8032 test key, verifies it with CryptoKit, and rejects a wrong
public key. It never generates or accesses production signing keys.

For an isolated local package with Sparkle linked, set `MACMON_ENABLE_UPDATES=1`,
`SPARKLE_PUBLIC_ED_KEY` to a test public key, and
`MACMON_PACKAGE_ROOT=artifacts/update-test` when running `tools/package-app.sh`.
This leaves the normal development bundle intact. An ad-hoc signed test package
does not qualify as a distributable notarized release.

Before broad distribution, validate an actual two-version update on Apple Silicon
and Intel: manual and scheduled checks, disabling checks and relaunching, download
and install, clean history/settings restoration, and refusal of a modified ZIP.
Live GitHub publishing, Developer ID notarization, and a complete installed-app
update require the configured credentials and cannot be established by mocked tests.

Update restarts save a separate, bounded `UpdateRestart` handoff containing history
and the Usage ledger, regardless of the ordinary restart-history preference. The
next launch claims it once, validates the same boot and a ten-minute age limit,
rebases retained sample keys, and restores counted lifetimes before live sampling.
Normal quits still start a fresh Usage session. Failed or stale handoffs fall back
to ordinary startup; the signed two-version Sparkle installation remains a native
release validation requirement.
