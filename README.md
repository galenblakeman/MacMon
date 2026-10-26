# MacMon

macOS resource monitor built with C# and Avalonia, using WinMon's portable MonCore library.

MacMon provides live system CPU usage, a two-minute CPU/memory chart, estimated physical-memory usage, live memory pressure and available space on the data volume. The process list shows CPU averaged over five seconds, resident memory and thread counts. Switch between individual processes and executable groups, pin rows, filter to pins, and follow 30-second trends for the selected CPU/memory/thread metric. Grouped memory shows a resident sum that can count shared pages more than once. Search by name or PID, sort by CPU/memory/threads/disk I/O, and select a row to inspect its start time and executable path. Individual selection and pins follow a process lifetime, so a reused PID cannot inherit them. Group pins follow the exact executable path; pins currently last for this session.

Network throughput follows the chosen interface (Automatic prefers an active Ethernet/Wi-Fi interface). Storage read/write rates aggregate all readable block-storage drivers, separately from the data volume’s free space. A second history plot shows network and storage total throughput on an automatic bytes-per-second scale. Process Disk I/O measures disk reads/writes only.

Click the chart or choose Capture to inspect retained process observations while recording and top readouts stay live. Choose 2, 10, 30 or 60 minutes to adjust the chart and retention limit; memory bounds can shorten retained history. Export resources or retained process samples as local CSV for the selected chart range. Historical samples can omit idle processes; pressure/compressed details stay live.

The Usage tab totals observed CPU time and disk I/O by executable for this session, including exited apps. Search, sort, reset or export those totals independently of the chart range. Missing intervals are excluded.

Automatic captures can watch system CPU, estimated memory usage, network throughput or any process’s CPU. Configure thresholds, duration and cooldown with **Automatic captures…**. A fired rule marks the peak on the chart and saves a local text report under `~/Library/Application Support/MacMon/Captures`; only the newest 200 MacMon-owned reports are kept. Choose a capture to inspect its retained peak or open its report after history expires. **Copy capture** copies the current resource summary and process rows with their averaging notes.

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

GPU, launchd, process actions and saved history are not implemented yet. See [development notes](docs/DEVELOPMENT.md) for contracts, verification and remaining work.
