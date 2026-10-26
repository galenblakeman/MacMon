# MacMon

macOS resource monitor built with C# and Avalonia, using WinMon's portable MonCore library.

MacMon provides live system CPU usage, a two-minute CPU/memory chart, estimated physical-memory usage, live memory pressure and available space on the data volume. The process list shows CPU averaged over five seconds, resident memory and thread counts. Search by name or PID, sort by CPU/memory/threads, and select a row to inspect its start time and executable path. Selection follows a process lifetime, so a reused PID cannot inherit it.

Sampling, ranking, selection and bounded history run through MonCore. Baselines, failed readings and wake gaps appear as unavailable CPU readings instead of zero. macOS may deny access to some processes; the list reports unavailable readings and retries without requesting elevated privileges.

Requires macOS, Python 3 for local-package normalization, and the .NET 10 SDK (validated with 10.0.401).

```sh
bash tools/bootstrap-moncore.sh
bash tools/verify.sh --smoke
dotnet run --project src/MacMon -c Release --no-build
```

The bootstrap script uses a sibling `../WinMon` checkout by default, or accepts its path as the first argument. It builds **MonCore 3.4.1-macos.1** from a pinned upstream commit plus the [portable identity handoff patch](dependencies/moncore/README.md), runs the portable tests and writes the package to the ignored local feed. An existing checkout's branch and working files remain intact. MacMon references that exact package; shared source stays in WinMon. No external NuGet publication is needed.

To create a self-contained local application:

```sh
bash tools/package-app.sh
open artifacts/osx-arm64/MacMon.app
```

The bundle is ad-hoc signed for development. Developer ID signing, notarization and distribution are future work. Intel packaging accepts `osx-x64`; it has not been validated on Intel hardware.

Network rates, GPU, launchd, process actions and saved history are not implemented yet. See [development notes](docs/DEVELOPMENT.md) for contracts, verification and remaining work.
