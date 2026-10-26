# MicroGauge Avalonia dependency

MacMon uses the unmodified MIT-licensed MicroGauge core and Avalonia adapter from
[upstream commit 710092272d5be4e27536b296ec7d52dae569d54c](https://github.com/galenblakeman/MicroGauge/commit/710092272d5be4e27536b296ec7d52dae569d54c).
The Avalonia adapter is not yet published on NuGet. `bash tools/bootstrap-microgauge.sh`
checks out that exact commit in a temporary directory, runs its eight Avalonia tests,
and packs both projects into the ignored `artifacts/local-feed` directory. No sibling
checkout or upstream source is modified.

Both local packages use version **1.1.0.1-macmon.1**, distinguished from published core
1.1.0.1 to avoid cache collisions. Source mapping restricts these package IDs to the
local feed. The adapter targets .NET 10 and Avalonia 12.1.4 and requires SkiaSharp
4.150.1. MacMon explicitly pins that Skia version, including Linux native assets for
headless rendering, rather than accepting Avalonia's older 3.x transitive version.
Run `tools/verify.sh --smoke` and package native smoke checks after changes to this set.

The upstream [MIT license](LICENSE) is included in each application bundle at
`Contents/Resources/MicroGauge.LICENSE`. When an official package is published, replace
the bootstrap and local references only after validating the new package's rendering,
headless tests and native bundle on supported architectures.

Two clean builds with SDK 10.0.401 produced identical normalized package bytes.
Release DLLs omit debug metadata to avoid temporary-checkout-dependent PDB hashes.
SHA-256:

- MicroGauge: `d58235adc1d8c1456e2ec6f1dacbbe5c8b5b077199d3e56734cf7350e7a60523`
- MicroGauge.Avalonia: `41b3b704502ae6edd17915522b0db461984017cd303d2a48f2c39194f9ce8fd3`

A different compiler or upstream commit requires revalidating the package bytes and
regenerating both lock files. Packages are development artifacts, not published builds.
