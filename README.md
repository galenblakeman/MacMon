# MacMon

macOS resource monitor built with C# and Avalonia, using WinMon's portable MonCore library.

Choose **Compact view** or press **⌘⇧M** for a short live resource strip. **Full view** or **Escape** restores the dashboard and its selections. Compact mode and width restore on clean restart; full-window dimensions are kept separately. Settings and Always on top work in both views.

Resource cards use MicroGauge for CPU, memory, network and storage, with Bowl, Needle, Bar or None in Settings → Appearance. Contrasting markers show live two-minute peaks; activity scales are labeled in bytes/second. Each gauge’s **Details** button opens a live inspector with measurement definitions, scope and supported readings. Details work in full and compact views, stay live during chart Capture, and close with Escape. [Dependency details](dependencies/microgauge/README.md).

MacMon provides live system CPU usage, configurable resource history charts, estimated physical-memory usage, live memory pressure and available space on the data volume. The process list shows CPU averaged over five seconds, resident memory and thread counts. Switch between individual processes and executable groups, pin rows, filter to pins, and follow 30-second trends for the selected CPU/memory/thread metric. Grouped memory shows a resident sum that can count shared pages more than once. Search by name or PID, sort by CPU/memory/threads/disk I/O/name/PID, and select a row to inspect its start time and executable path. Individual selection and pins follow a process lifetime, so a reused PID cannot inherit them. Group pins follow the exact executable path; pins and view preferences are saved on clean exit. Individual pins restore only within the same boot; group pins can follow later instances.

Network throughput follows the chosen interface (Automatic prefers an active Ethernet/Wi-Fi interface). Storage read/write rates aggregate all readable block-storage drivers, separately from the data volume’s free space. The history chart offers Overlay, Small Multiples, Heatmap and Focus modes, with metric visibility controls. CPU/memory use 0–100%; network and storage each use an independently labeled bytes-per-second scale. Layout, visible series and focus restore on restart. Process Disk I/O measures disk reads/writes only.

Hover the chart to preview readings without changing mode. Click the chart or choose Capture to inspect retained process observations while recording and top readouts stay live. Focus the chart and use Left/Right to inspect samples, Home/End for visible boundaries, and Escape to return to Live. Choose 2, 10, 30 or 60 minutes to adjust the chart and retention limit; memory bounds can shorten retained history. Export resources or retained process samples as local CSV for the selected chart range. Historical samples can omit idle processes; pressure/compressed details stay live. Enable **Keep history across restarts** under **Settings → Monitoring** to save bounded history on clean exit and reopen it in Capture mode; Usage totals begin fresh. Saved reports remain available across restarts.

Process and Usage column headers sort in either direction. Choose All rows or Top 5/10/20/50/100; matching pinned process rows remain visible in addition to the unpinned limit. **App bundles only** filters executable paths inside `.app/Contents/MacOS`, including helpers and background apps; it does not mean apps with visible windows. Bundle icons come from macOS, with a generic fallback for inaccessible or missing bundles. The **Actions** checkbox hides the process action bar. Filters, limits and sort directions restore on clean exit; exports retain all applicable source data.

The Usage tab totals observed CPU time and disk I/O by executable for this session, including exited apps. Search, sort, reset or export those totals independently of the chart range. Missing intervals are excluded.

Automatic captures can watch system CPU, estimated memory usage, network throughput or any process’s CPU. Configure thresholds, duration and cooldown with **Automatic captures…**. A fired rule marks the peak on the chart and saves a local text report under `~/Library/Application Support/MacMon/Captures`; only the newest 200 MacMon-owned reports are kept. Choose a capture to inspect its retained peak or open its report after history expires. **Copy capture** copies the current resource summary and process rows with their averaging notes.

The **Jobs** tab monitors loaded launchd jobs in MacMon's current bootstrap context. Search label/PID/observed process, filter running or not running, sort, refresh, and inspect/copy read-only details. It refreshes every five seconds while visible and preserves selection. Scope and observation time are explicit; failures retain stale rows with a warning. “Not running” does not mean disabled, and last exit status does not describe current health. Process details require a fresh matching lifetime. Other domains, disabled-job configuration and child-worker attribution are outside this inventory; there are no job-control actions. Jobs remain live when the resource chart is in Capture mode. The selected tab and Jobs sort/filter restore on clean restart.

Open **Settings…** or press **⌘,** for General, Appearance, Monitoring and Automatic captures. One Save applies all sections immediately and writes preferences; Cancel discards edits. Settings reopens to the last section used during the app session. The **Automatic captures…** shortcut opens that section directly.

Choose **System**, **Light** or **Dark** appearance and an independent **Classic**, **Ocean**, **Colorblind** or **High contrast** chart palette. A local sample preview shows the pending choices without changing the running app. Resource text, charts, sparklines, cards and dialogs follow the selected appearance; System follows macOS appearance changes. Existing settings retain Dark/Classic defaults. Monitoring holds restart history, history range and network selection. Always on top is also available in the main window.

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

The bundle and versioned ZIP/checksum are ad-hoc signed for development. Close the running packaged app before rebuilding its bundle. Intel packaging accepts `osx-x64`; execution still requires validation on Intel hardware.

For distribution, `bash tools/package-release.sh RUNTIME 'Developer ID Application: …' KEYCHAIN_PROFILE` verifies a clean committed checkout, signs with hardened runtime, submits for notarization, staples and assesses the app, then creates a ZIP/checksum and release manifest. This command uploads the app to Apple. A Developer ID Application certificate/private key and a configured `notarytool` keychain profile are required. Actual Developer ID notarization remains unverified on this machine; see the development notes.

Select a process for Properties, Copy details, Reveal in Finder and a confirmed termination request. Termination uses a captured process generation, protects system/other-user/MacMon targets, and excludes processes started after confirmation opens. Historical views disable termination. Always on top, ranking/grouping, pins, history range, network selection, capture rules, pane ratio and window size restore on restart. Preferences live at `~/Library/Application Support/MacMon/settings.json`; invalid files are preserved and defaults are used.

GPU remains research work; unavailable metrics are not synthesized. See [development notes](docs/DEVELOPMENT.md) for contracts, verification and remaining work.
