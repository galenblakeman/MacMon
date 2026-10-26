# MonCore development package

MacMon consumes `MonCore` **3.4.1-macos.1**, built from WinMon commit `395a1a29ec83917d60beb380465c7e16500fcd6f` plus `portable-process-identity.patch`.

The shared changes also live in the sibling WinMon checkout on branch `codex/portable-process-identity`. The patch is a reproducible handoff until those changes are reviewed and published upstream. It contains shared-library changes and portable tests. MacMon compiles against the resulting NuGet package only.

Run `bash tools/bootstrap-moncore.sh` from the MacMon root. It creates a temporary build checkout under ignored artifacts, applies the patch to the pinned base, runs all portable tests and packs the prerelease. The source checkout's current branch and uncommitted files are left intact. Compiler paths and unsigned NuGet ZIP metadata are normalized to make package hashes stable across temporary build directories. Python 3 is required for normalization. Rebuilding twice with SDK 10.0.401 produced identical bytes; a different SDK/compiler requires revalidating the package and locks.

When the upstream package is available, replace the patch bootstrap with the published version, update both package references and regenerate the dependency locks. Do not reuse this prerelease version for subsequent shared-library changes.
