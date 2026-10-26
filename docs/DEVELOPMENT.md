# MacMon development

## Dependencies and verification

MacMon (Avalonia) references MonCore.MacOS and the pinned MonCore NuGet package. MonCore.MacOS references only MonCore. Shared code and portable tests belong to WinMon; MonCore retains the `WinMon.Monitoring` namespace. No Windows adapter or copied shared implementation is compiled into MacMon.

MacMon consumes **MonCore 3.4.1-macos.1**, built from WinMon commit `395a1a29ec83917d60beb380465c7e16500fcd6f` plus the [handoff patch](../dependencies/moncore/README.md). The shared changes also live in the sibling WinMon checkout on branch `codex/portable-process-identity`. The patch keeps this checkout reproducible until the shared changes are published upstream.

```sh
bash tools/bootstrap-moncore.sh
bash tools/verify.sh --smoke
bash tools/package-app.sh
open artifacts/osx-arm64/MacMon.app
```

Bootstrap builds and tests a temporary checkout, preserving the source checkout's branch and work in progress. Python 3 normalizes unsigned NuGet ZIP metadata; compiler path mapping removes temporary-directory names. Two clean bootstraps with SDK 10.0.401 produced identical package bytes. A different compiler requires revalidating package hashes and locks. `NuGet.Config` maps MonCore exclusively to the local feed, and the application dependency graphs restore in locked mode.

Commands run from MacMon's root, selecting SDK 10.0.401 instead of WinMon's Windows SDK pin of 10.0.402. Packaging publishes into a fresh staging bundle and runs its own native smoke on a matching host architecture before replacing the generated app, preventing stale DLLs from earlier local-package builds. The app is ad-hoc signed for local development. Intel packaging accepts `osx-x64`, but execution is validated on Apple Silicon only.

## Portable process contracts

`ProcessLifetimeIdentity` combines PID, an opaque start token and an explicit `ProcessLifetimeDomain`. Tokens are comparable only within their domain and originating machine/boot. Existing Windows constructors retain FILETIME identity. Mac observations use Unix microseconds from BSD start seconds/microseconds; `StartedAtUtc` is separately supplied for presentation. The legacy `CreationTicks` property carries that opaque token and must not be interpreted as FILETIME by portable consumers.

`ProcessIdentityPolicy` supplies executable keys and their comparer. Ranking, pins, selection and usage receive the same policy. Windows defaults preserve case-insensitive path/name grouping. MacMon selects `ExactPaths`: ordinal, case-sensitive path comparison, with a per-lifetime fallback when the path is unreadable. This deliberately avoids merging distinct executables on case-sensitive volumes. It does not promise to merge aliases or differently spelled paths on case-insensitive volumes. Adapters can supply canonical file identity policies later.

`CpuAvailable` distinguishes unavailable interval CPU from measured zero without changing the existing Windows constructor. Portable consumers use nullable `ObservedCpuPercent`. Ranking excludes explicitly missing intervals from CPU averages; compaction preserves unavailable rows. Samples opt into `IndependentProcessReadings` so failed system CPU does not invalidate process CPU, memory or thread observations. Windows samples retain the original defaults. Usage totals remain observed totals over frames with system CPU, keeping their denominator consistent; they skip missing process CPU contributions. Portable usage is reassigned after an executable changes through exec.

The old Windows restart format is unchanged. Saving portable domains, timestamps, availability states or independent process frames through that serializer returns false before writing. MacMon does not restore Windows history; its separate Mac history format is described below. Grouped pins and usage snapshots record their identity policy and reject mismatched policy restoration. Portable CSV appends lifetime-domain/start-time columns and leaves unavailable CPU empty; Windows CSV columns remain unchanged.

## Native collection and app behavior

The system reader uses `host_statistics(HOST_CPU_LOAD_INFO)` for cumulative CPU states, `sysctlbyname(hw.memsize)` for installed RAM and `hw.logicalcpu` for machine capacity. The host port is released on disposal. `DriveInfo` reads the configured volume's capacity, defaulting to `/System/Volumes/Data`. Memory uses the stable rev1 `host_statistics64` layout and `host_page_size`. Estimated used physical bytes are `(internal - purgeable + wired + compressor) * pageSize`; cached bytes are `(external + purgeable) * pageSize`. Available bytes are the complement of this estimate, not a guarantee of immediately allocatable RAM. Invalid counters remain unavailable. The memory trace is recorded even on CPU-baseline frames. Compressed bytes describe physical compressor storage, not uncompressed logical pages. Live pressure reads `kern.memorystatus_vm_pressure_level`, whose [XNU sysctl handler](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/kern/kern_memorystatus_notify.c) returns dispatch flags 1/2/4 for Normal/Warning/Critical; denied or unknown values stay unavailable. Pressure and compressed details are live adapter metadata, not persisted shared metrics. Data-volume capacity remains separate from aggregate storage activity, described below.

The process reader uses `proc_listpids`, `proc_pidinfo(PROC_PIDTASKALLINFO/PROC_PIDTBSDINFO)` and `proc_pidpath`. Native layouts match the SDK's `sys/proc_info.h`; tests assert the layout sizes and exercise real reads. Native CPU counters use Mach absolute ticks, as in [Apple's implementation](https://github.com/apple-oss-distributions/xnu/blob/main/osfmk/kern/bsd_kern.c). The adapter converts them through `mach_timebase_info` to nanoseconds, including Apple Silicon's non-unit timebase; an actual-workload test compares the result with .NET process CPU accounting. Enumerations retry instead of truncating. Each path lookup is followed by a start-identity recheck. Exited, denied and raced observations are counted separately from successful rows. Readable BSD identity can still produce a row with unavailable CPU/memory when task details are denied. No permission escalation is requested.

Process CPU divides lifetime CPU deltas by the actual per-process monotonic interval and machine logical-core count. First readings, PID reuse, counter rollback, missing previous reads and wake gaps require fresh baselines. Rates use monotonic time. Wall time is used for labels and discontinuity detection because some Mac monotonic clocks stop during sleep. Resident bytes are kept distinct from private committed memory and macOS physical footprint.

`MonitorSession` captures once per second and posts ordered drains to Avalonia's dispatcher. The process list substitutes the full current frame into the short ranking window, keeping every observed idle process visible while retained history remains compacted within MonCore's sample-count/process-entry budgets. CPU averages cover observed intervals in the last five seconds. Memory and threads use the newest observation; exited lifetimes are filtered from the live list. Search and sorting preserve lifetime selection. The sampling thread receives an immutable retention snapshot. Unknown readings use a dash, and chart gaps break connecting lines.

## Evidence and remaining checks

On October 25, 2026, Apple Silicon, macOS 26.6, .NET SDK 10.0.401:

- 152 shared portable tests passed, including legacy Windows history fixtures, case-sensitive policy behavior, PID/domain reuse, pins, selection, exec usage, partial CPU and portable CSV.
- 21 Mac collector tests passed, covering layouts, native reads, protected/exited/raced processes, CPU normalization, resets and partial failures. Native smoke reported hundreds of process rows and readable CPU/memory/thread values for its own lifetime.
- The full WinMon solution and Windows adapter test assembly cross-built with `EnableWindowsTargeting=true`, zero warnings. This does not execute the Windows tests or replace `Verify.ps1`.
- Local package reproduction, locked restore, Release build and native smoke passed. The packaged desktop app was visually checked for name/PID search, memory sorting, start/path details, all observed idle rows and selection surviving live refresh. Repeat these checks after UI changes.

The repeatable core microbenchmark is `tools/MonCore.Performance`. For an existing local baseline package, pass `-p:MonCoreBenchmarkVersion=3.4.1`; the default is the current prerelease. It feeds 850 processes across six observations, warms the ranking path 1,000 times and measures seven batches of 500 operations. On this Mac, medians changed from 165.4 to 165.8 microseconds for grouped ranking and 13.7 to 12.1 microseconds for usage. Ranking allocations grew from 32,416 to 35,652 bytes per refresh for portable metadata; usage remained 32 bytes. These measurements do not replace WinMon's Windows UI/pixel/performance suite.

Windows runtime, smoke, performance and Store checks were reported completed and passing by the maintainer on October 25, 2026.

Outstanding platform checks: Intel execution; actual sleep/wake and reboot behavior. Simulated clock/wake gaps are covered. GPU remains research work. Read-only current-context launchd monitoring is implemented below. Mac persistence and distribution tooling are implemented below; actual Developer ID notarization remains an external gate.

## Parity implementation progress

Work proceeds in separately verified commits on `codex/winmon-parity-slices`.

1. Memory usage/pressure: implemented. Release build has zero warnings; 30 collector tests and native smoke pass. Packaged Apple Silicon app visually checked for memory card wrapping, live pressure and separate CPU/memory traces.
2. Grouped processes, pins and sparklines: implemented. 35 collector/presentation tests and native smoke pass; packaged app visually checked for grouping, search, pinned-only filtering, selection and trails. Pin/view persistence is implemented in slice 6.
3. History inspection and CSV export: implemented. 39 collector/presentation tests and native smoke pass. Packaged app checked for Capture staying fixed while top readings advance, chart marker, 10-minute range selection and a native-dialog resource export verified on disk.
4. Network and disk activity: implemented. 48 tests and native smoke pass; packaged UI checked for network selection, rate readouts, separate activity chart and process disk-I/O columns.
5. Usage totals and automatic captures: implemented. 53 tests and native smoke pass. Packaged Usage tab visually checked. Capture dialog and a live CPU trigger were subsequently checked after unlock: a report was saved and its marker/list selected the retained peak. Split-pane keyboard resizing and dashboard scrolling were verified after fixing overlapping minimum heights.
6. Process tools and preferences: implemented. 62 tests and native smoke pass. Packaged properties, copy, captured-target termination of a disposable sleep process, disabled self-group action, saved grouped pin/filter/always-on-top restoration, keyboard pane resizing and dashboard scrolling verified.
7. Portable persistence and distribution readiness: implemented. 72 Mac tests, three release-workflow tests and native smoke pass. Packaged restart/history/report restoration verified. Developer ID notarization, Intel execution and physical sleep/wake/reboot remain external validation gates; details follow.

GPU remains separate capability research; the launchd investigation below now has a read-only implementation. Windows runtime, smoke, performance and Store verification are complete per maintainer report; they do not need to be repeated for Mac-only UI/collector changes.

### Process presentation and performance

`ProcessPresentation` composes MonCore ranking, search and pins. Grouped memory is explicitly a resident sum, sorted by that sum in the Mac UI; no private-byte measurement is invented. Idle current lifetimes remain visible. Individual pins and selection retain lifetime identity; grouped pins use the same exact-path policy as ranking. Their combined immutable retention predicate reaches the sampler. Missing pins display a state and unavailable metrics. Switching grouping clears selection but preserves the separate pin lists.

Thirty-second sparklines use the selected metric on an independent local scale. Missing retained observations and collection gaps break trails; absent compacted rows are not silently shown as measured zero. Group trails stay unavailable if an observed member's metric is missing. The trail builder uses one row index and fixed arrays per refresh, caching policy keys while preserving exec path changes.

Run `dotnet run --project src/MacMon -c Release -- --presentation-benchmark` for 850 processes, 120 samples, 20 warmups and seven batches of 20 refreshes. On this Apple Silicon Mac, the final run measured medians of 0.545 ms / 316,856 bytes for ranking/filtering without trails, 1.045 ms / 1,014,256 bytes for individual rows with trails and 3.259 ms / 501,656 bytes for grouped rows with trails. The extra work buys 30-second per-row histories at one refresh per second; this is synthetic presentation overhead, not total application CPU or memory. An initial implementation allocated 4.3 MB per individual refresh; fixed arrays and cached keys reduced it before commit.

### Capture and export

`HistoryInspection` selects by monotonic timestamp plus wall timestamp. Capture uses compacted history ending at that exact sample; live ranking substitutes the full current frame. Expired selection stays expired until the user chooses another moment or Live. The chart uses monotonic coordinates for selection and marks the selected moment. Top resource cards always remain live. A 2/10/30/60-minute choice changes chart range and the history capacity; the initial default retains up to ten minutes while showing two. Entry budgets can shorten available history.

Resource/process CSV exports snapshot retained samples in the current chart range before opening Avalonia's native save picker. They export that dataset regardless of current table filters, grouping, or Capture selection. MonCore writes invariant numbers and UTC timestamps through a sibling temporary file and atomic replacement; portable process fields preserve opaque identities and missing CPU. CSV contains retained observations, including gap rows, not a full process census at every second. Export runs off the UI thread and reports errors without discarding recording. A UI-driven resource export produced 20 readable rows with missing baseline CPU and measured memory bytes.

### Network and storage activity

Network counters come from .NET `NetworkInterface.GetIPStatistics` for one non-loopback adapter. Automatic chooses an active Ethernet/Wi-Fi interface and keeps it until it disconnects; it does not promise default-route detection. The user can explicitly select other interfaces, including virtual adapters. Offline/missing interfaces have unavailable counters. Interface list changes preserve the selected ID. No adapters are summed, preventing physical/VPN double counting.

Storage counters sum the [IOBlockStorageDriver Statistics](https://developer.apple.com/documentation/iokit/kioblockstoragedriverstatisticskey) table's cumulative read/write bytes. Every IOKit object and owned CoreFoundation value is released. Missing drivers, invalid values and overflow produce unavailable totals. Registry entry IDs identify the exact driver set so hot-plugging requires a new baseline. These rates describe all enumerated block-storage drivers, not one APFS volume and not active-time percentage. They occupy the secondary-disk activity fields with an explicit name; primary disk fields continue to describe `/System/Volumes/Data` capacity.

`proc_pid_rusage(RUSAGE_INFO_V2)` supplies each readable process's disk read/write counters. The 160-byte SDK layout is asserted in tests. The final BSD lifetime recheck covers this lookup as well as executable-path lookup. Each direction is differenced independently, so a rollback in one cannot be masked by growth in the other. These rates exclude network I/O and must be labeled Disk I/O in Mac UI/report presentation; the shared CSV `io_bytes_per_second` field carries this Mac measurement. Group totals contain observed contributions and may be incomplete when member readings are denied.

All rates use actual monotonic intervals and require successful adjacent observations, an unchanged source identity, and nondecreasing counters. Wake gaps reset baselines. System activity is independent of CPU collection availability. The activity plot shows receive+send in blue and storage read+write in gold, with a labeled shared automatic rate scale; CPU/memory remain on a separate percentage plot. Both plots select the same capture moment.

`--collector-benchmark` warms ten captures and measures fifty native captures without sleeps or UI drawing. With roughly 450 readable processes on this Mac, commit `ef4ef78` measured median 1.96 ms/capture, 2.02 CPU ms and 459,294 allocated bytes; this slice measured 6.63 ms, 6.92 CPU ms and 1,109,113 bytes. The additional roughly five CPU milliseconds per one-second capture pays for interface enumeration, IOKit storage and per-process rusage. These burst measurements include JIT/cache effects and do not replace a sustained UI run or establish application-wide CPU usage.

### Usage and automatic captures

The Usage tab reads `MonitorSession.Usage`, which receives uncompacted live frames exactly once. CPU time and disk-I/O bytes group by the Mac identity policy and include exited applications. Search/sorting are presentation only. Reset begins a new session-accounting period; CSV snapshots its totals and denominator before the native save picker opens. Chart range and historical selection do not alter totals. Missing readings contribute no invented activity; these are observed totals, not lifetime accounting from process creation. GPU totals are not displayed.

`MacCaptureOptions` restricts the shared capture engine to CPU, memory estimate, network (the busier direction in decimal Mbps) and process CPU (one-core percent). Duration, threshold and cooldown normalize to the shared supported ranges. Disk active-time and GPU rules are unavailable. `LiveSampleRecorded` feeds ordered full observations to the engine; it does not replay restored or selected historical samples. Shared gap, availability, hysteresis and cooldown behavior remains intact. Capture settings persist with the preferences on normal exit.

Each firing retains a bounded marker/metadata entry and enqueues a full-peak report to a single bounded disk writer. Full peak process rows are interval readings, not five-second averages, and say so. Network reports explicitly lack process network attribution; Mac process I/O is disk only. Reports include UTC and local/time-zone labels, raw units, unknown readings and the triggering process when applicable. Filenames contain UTC time and a UUID. Writes flush a temporary file before rename. Pruning requires both a MacMon capture filename and the first-line ownership marker; it does not remove WinMon/user files. Accepted reports drain before the window closes; write failures appear in app status. Automatic capture markers and their list open retained peaks or saved reports once history has expired. Manual Copy capture uses current filters, grouping and five-second averages with explicit notes.

The dashboard is in a scrollable upper pane separated from the bounded data pane by a draggable divider. This keeps a usable minimum data area while allowing the resource controls to scroll on short windows. Hidden process/Usage tabs skip their row rebuilds while sampling and accounting continue.

### Process tools and preferences

Properties and Copy details resolve the lifetime/executable selection independently of row slots and filters, including retained history. Details state their averaging window and distinguish resident sums and disk-only I/O. Reveal in Finder passes the captured path as a separate argument to `/usr/bin/open -R`; it does not execute that file. Historical and unobserved rows cannot request termination. Self/PID 0/PID 1 targets and groups containing MacMon are protected.

`MacProcessActions` prepares one immutable target per current observed lifetime before confirmation. A group is rejected if any member cannot be prepared safely. The confirmation lists exactly those targets; no later enumeration adds new members. Each native preparation gets a task-name port, reads `TASK_AUDIT_TOKEN`, then checks BSD start microseconds, current effective UID and executable path. Ports are released promptly. Only same-user, non-root targets are allowed. Execution revalidates identity/path/ownership and calls [`proc_signal_with_audittoken`](https://github.com/apple-oss-distributions/xnu/blob/main/libsyscall/wrappers/libproc/libproc.c) with SIGTERM and the original token. The kernel validates PID version at delivery, covering exits/reuse/exec after validation. There is no PID-only fallback or elevation. Success means a termination request was accepted; processes may ignore SIGTERM. The app waits for an already-confirmed delivery task before exiting. Native tests and a UI confirmation test terminated only disposable sleep children; stale, other-user, root, self and replacement-executable cases are covered through the action seam.

Preferences use versioned JSON in `~/Library/Application Support/MacMon/settings.json`, flushed to a temporary file before replacement. Dimension/index/rule/pin normalization occurs before control assignment. Invalid files are preserved with unique `.invalid-*` names; an unknown newer format disables saves and preserves the original. Normal exit saves view, sort, tab, window dimensions, splitter ratio, pins, network selection, Always on top and capture settings. The sampling loop receives immutable retention snapshots as before. The boot-session UUID scopes restored individual pins; on a changed or unavailable boot UUID only executable-group pins restore. Usage totals still begin fresh. Window dimensions are clamped to the current screen's work area where possible.


### Restart history and distribution readiness (slice 7)

Opt-in **Keep history across restarts** writes `~/Library/Application Support/MacMon/history.mmh.gz` on clean exit. A Mac-specific versioned gzip format preserves compacted observations, missing values, Unix-microsecond identities, original time-zone offsets and per-sample boot provenance. The Windows restart format is untouched. Limits are 3,600 samples, 500,000 process entries, 64 MiB compressed, 192 MiB expanded and bounded string tables. Atomic flushed replacement preserves the previous complete file on a failed save. Corrupt files are preserved under `.invalid-*`; unknown versions disable replacement. Turning the option off removes the application's saved history on clean exit. This is local restart storage, not an interchange format or crash-recovery journal.

Restore filters by age using the newest saved wall time plus saved monotonic intervals, then rebases into a positive virtual monotonic clock shared with future frames. This handles short uptime after reboot without interpreting old monotonic ticks as current uptime. History ahead of the current wall clock by more than five seconds is ignored. Actual elapsed time across an offline wall-clock correction cannot be reconstructed reliably. Restored samples never feed Usage or automatic rules. The app initially selects the newest restored moment; resource cards wait for fresh live readings. Foreign/unknown-boot historical individual pins are disabled, executable-group pins remain available, and selection clears across boot origins. Report metadata reloads independently; exact keys or unique original wall timestamps reconnect retained peaks after repeated rebasing. Ambiguous/expired peaks open their reports instead. Minimized/hidden windows skip presentation rebuilds while collection and accounting continue.

`--history-benchmark` saved/restored 588 samples with 499,800 synthetic process entries: 666.6 ms save, 541.6 ms load, 11,283,554 compressed bytes on this Apple Silicon Mac. The repeated synthetic values compress well; this is persistence throughput evidence, not a maximum real-world file-size prediction.

A 60-interval `--measure-process PID 60` run against the packaged single-file GUI with roughly 440 readable processes, individual CPU ranking and two-minute history measured mean 0.389% of total machine CPU, peak 1.480%, and peak resident 226.9 MiB. The machine has ten logical cores; Activity Monitor's one-core CPU convention would show approximately ten times those percentages. This short run included other desktop activity and is not a long-duration memory/leak or sleep/wake test.

Packaging now publishes a self-contained .NET single-file host plus the three Avalonia/Skia/HarfBuzz native libraries. Version 0.2.0/build 2 comes from MSBuild properties. Managed DLLs and runtime JSON are embedded, avoiding invalid code placement under `Contents/MacOS`. `sign-app.py` signs nested Mach-O code inside out and verifies with strict deep verification; it never uses `--deep` for signing. Development uses ad-hoc signing. Developer ID uses secure timestamps, hardened runtime and only the JIT entitlement required for [non-AOT .NET on macOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos). No debugger or library-validation exception is added. Close a running packaged app before replacing its bundle: older multi-file builds can fail late assembly loads after their files have been replaced.

`tools/package-app.sh [osx-arm64|osx-x64] [--identity CERTIFICATE]` creates the app, ZIP and SHA-256 file after native smoke on matching hardware. `tools/package-release.sh RUNTIME CERTIFICATE KEYCHAIN_PROFILE` requires a clean committed tree, matching native hardware and a valid Developer ID Application identity. It runs verification, signs, submits with `notarytool --wait`, requires Accepted, staples and validates, checks strict signatures and Gatekeeper, then rebuilds the final ZIP from the stapled bundle. Output under `artifacts/releases/VERSION/RUNTIME` includes the checksum and source commit manifest. Failed submissions retain staging logs and do not produce a final release directory. This follows [Apple's notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow); invoking it uploads the app to Apple.

Release workflow tests use mocked Apple signing/notary commands and a disposable fixture, checking sequencing, rejection behavior, hardened signing arguments and that the final ZIP includes the stapled ticket. These tests do not prove Apple acceptance. The normal user's keychain has Apple Development/Distribution identities but no Developer ID Application certificate/private key. No real notarization submission, stapling or Developer ID Gatekeeper assessment was performed; a notarytool profile is also not yet verified. Intel execution, actual sleep/wake/reboot, and older macOS versions remain hardware/manual gates. Tests simulate reboot and clock discontinuities. The bundle declares macOS 13 minimum; runtime validation here is macOS 26.6.2 only.

Packaged UI verification confirmed a live low-threshold CPU trigger, saved report, clean exit, restoration of 24 history samples in Capture mode, restored report selection linked to its peak, fresh live process readings and independent fresh Usage accounting. The test rule was returned to disabled with its normal 90%/10-second values. Full verification passes 72 Mac collector/presentation/persistence tests and three release-workflow tests with zero build warnings, plus native smoke. Earlier portable/Windows evidence above remains applicable; Windows runtime, smoke, performance and Store checks were reported passing by the maintainer.

### Separate GPU investigation

[Metal counter sampling](https://developer.apple.com/documentation/metal/gpu-counters-and-counter-sample-buffers) instruments an application's command work. It does not establish an all-process system utilization contract, and supported counters vary with the [Metal feature sets](https://developer.apple.com/metal/feature-sets/). A normal-user `IOAccelerator` registry probe on this Mac exposed one entry with `Device Utilization %`, `Renderer Utilization %` and `Tiler Utilization %` keys. This is experimental evidence for this machine, not a documented portable interface. Normal-user `powermetrics --samplers gpu_power` refused to run without superuser privileges; no elevation was attempted.

A next GPU slice should first prototype capability discovery behind an unsupported fallback, validate idle/loaded readings on multiple Apple Silicon generations and Intel/integrated/discrete configurations, and establish units/reset/source identity before exposing a chart. Per-process attribution needs a separate supported source. Until those checks pass, GPU stays unavailable and no GPU rule or estimated process attribution is offered.

### Initial launchd investigation

The installed SDK's `launchctl(1)` manual explicitly warns that newer output has no fixed format and is intended for humans rather than API parsing. A read-only normal-user `launchctl list` succeeded with 512 job rows in this login session; only the count was retained. No job was modified. Apple's [launchd job documentation](https://developer.apple.com/library/archive/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html) provides the job/KeepAlive model. System, user and GUI domains have distinct identity and permission boundaries.

A next launchd slice should provide a read-only current-user inventory with domain+label identity, distinguish loaded/running/inactive/disabled states, and mark partial or unsupported discovery explicitly. Any CLI parser needs captured-version fixtures and failure detection rather than an assumed stable format. Reported PIDs must be matched to verified process lifetimes; worker attribution should remain conservative. Model this as Mac jobs rather than Windows Services compatibility records. Only after inventory is reliable should explicit start/stop actions account for KeepAlive relaunch and domain permissions. This investigation informed the read-only implementation below. Service-management actions remain outside its scope.

## Read-only launchd implementation

Work follows three slices: bounded discovery/lifetime verification; job presentation and manual refresh; automatic refresh, saved view settings and packaged validation.

### Slice 1: discovery

The adapter invokes only the absolute path `/bin/launchctl` with one allowlisted argument: `manageruid`, `managername`, or `list`. It uses no shell, labels as command arguments, job mutation, privilege escalation, plist/argument/environment inspection, or newer `print` parser. Each child has a three-second timeout, bounded stdout/stderr and cancellation cleanup. The legacy tabular header and every row must validate; malformed, duplicate or oversized inventories fail as a whole. Bounds are 2 Mi characters, 20,000 jobs and 4,096 characters per label. Negative last-exit values are signal numbers, not current job health.

Scope is the caller's current bootstrap context, described by manager UID/name and an opaque reader-local context identity. These fields do not prove a canonical `gui/UID` or `user/UID` domain, so the app does not fabricate one. Job identity combines context and exact ordinal label. The inventory contains loaded jobs visible in that context only; absence is not proof of disabled/unloaded state across other domains. A reported PID means running at observation time; no PID means not running, with no inference about on-demand eligibility or disabled state.

Before listing, the adapter captures readable same-context-user process lifetimes, then rechecks each reported PID after listing. Only unchanged Unix-microsecond identities are attached. Raced, exited, denied, newly started and other-user processes remain unlinked. This does not attribute child workers or cumulative resource usage to jobs. The optional `--launchd-smoke` prints counts, scope and elapsed time without dumping labels or arguments.

### Slice 2: job presentation

The Jobs tab searches label/PID/observed process, filters all loaded/running/not running, and sorts by exact label, running first or PID. Rows identify states as observations, retain signed last-termination semantics and never infer disabled state or job health. Details/copy show full labels, context, observation time and only a fresh exact-lifetime process match; no launchd control actions are present. Discovery runs off the dispatcher and concurrent manual requests coalesce. Failed discovery keeps the last successful inventory explicitly stale, with process association suppressed. Selection follows context+label across refreshes rather than row position or PID. Job snapshots are independent of chart Capture, CSV history and session Usage.

Verification: 87 Mac tests, three release workflow tests, zero warnings and native smoke passed. The Apple Silicon development package was checked for label search, Finder job selection, read-only details with verified start identity, manual refresh and layout.

### Slice 3: refresh lifecycle and saved views

A monotonic five-second cadence polls only while Jobs is selected and the window is visible and not minimized. In-flight requests coalesce; failure retries use the same cadence. The one-second UI heartbeat launches no discovery work for hidden tabs. Returning to an overdue view requests a refresh. Exit cancels and joins an in-flight discovery before disposing the monitor; late completions cannot publish rows. The last successful inventory becomes stale after 15 seconds or immediately after a failed attempt. Stale warnings appear beside the scope and in status/details, and process linkage is suppressed. Polling does not depend on chart Live/Capture or restored history.

Context+label selection and list offset survive refreshes. Jobs sort/filter and the selected tab save through existing version-1 normalized preferences; old files default to All loaded/Label. Search text, job inventories and process associations are not persisted. App version is now 0.3.0/build 3. Both verification and native-architecture packaging run the read-only launchd smoke alongside the existing resource smoke.

Tests cover strict captured-format shape, empty inventory versus unsupported output, bounds, duplicate labels, signed exit status, process reuse/exit/other-user/mismatched PID, filtering, fresh details, stale preservation and recovery, monotonic cadence, overlapping requests, cancellation, ignored late completion, and preference normalization/round-trip. Native discovery and UI observations do not start, stop, load, unload, enable or disable any jobs.

Final evidence on October 26, 2026, Apple Silicon/macOS 26.6.2: 91 Mac tests and three release workflow tests passed with zero build warnings, plus resource and launchd native smoke. Discovery measured 82.2–97.4 ms in individual CLI/package observations with roughly 511–525 loaded jobs. Packaged UI checks covered automatic refresh, selection and scrolled-position retention, label search, running/not-running filtering, exact-lifetime details, chart Capture independence, and restart restoration of the Jobs tab with Running/PID choices. Archive SHA-256, extracted strict signatures, version/build and extracted launchd smoke passed. No launchd state was modified.

A 30-interval process measurement with Jobs polling and Running/PID view showed mean 0.595% of total ten-core machine CPU, peak 1.646%, and peak resident 240.0 MiB for MacMon itself. It excludes the short-lived launchctl children, includes other desktop activity and is not a long-duration performance/leak guarantee. Intel, older macOS and real sleep/wake validation remain outstanding; Developer ID signing/notarization gates are unchanged.

## UI parity slice 1: settings and appearance foundation

Version 0.4.0/build 4 adds one Settings window with General, Appearance, Monitoring and Automatic captures sections. The header entry and Command-comma open the last section used in this session; the capture shortcut opens its section directly. Capture controls are extracted into a reusable editor, with one Save for every section. Restart-history preference moves from the header to Monitoring; history range/network choices remain available in both places. General exposes Always on top without inventing unsupported startup/tray options.

Settings fields form an isolated draft. Theme/palette preview is scoped to its sample controls; Cancel/window close changes neither the app nor saved preferences. Save merges only edited fields with the latest view/window/pin snapshot, normalizes them and atomically persists before applying. Errors retain the draft for retry. A pending save disables duplicate submission/Cancel and blocks dialog close; main shutdown joins it before saving its final snapshot. Unknown preference versions remain protected from replacement. Theme-only changes do not reset capture-rule duration/cooldown state.

Version-1 preferences gain normalized string choices Theme and Palette. Missing/unknown choices use Dark/Classic, preserving the previous appearance for old files. System maps to Avalonia's default theme variant; Light/Dark force their variant. Shared theme dictionaries provide window/card/text/muted/grid/marker brushes. Application actual-theme changes recalculate palette brushes without changing the chosen palette. The local System preview reads the platform preference rather than inheriting a currently forced app theme.

Classic, Ocean, Colorblind and High contrast each define four resource hues. Chart strokes meet 3:1 contrast and resource text 4.5:1 against its card surface in both light and dark variants. The Colorblind palette uses a conventional blue/orange/purple/green grouping; these contrast checks are not a color-vision certification. High contrast is a chart palette, not an override of all operating-system accessibility settings. Custom charts and process sparklines expose render-affecting styled brush properties, so existing samples repaint after appearance changes. Selection/capture markers use a contrasting theme brush. Legends no longer hardcode color names.

Headless Avalonia 12.1.4/Skia tests cover draft/Cancel isolation, every-section Save, failed-save retry, duplicate/pending save behavior, protected settings, attached chart/sparkline resource updates, legacy/unknown preferences, persistence, merge preservation and palette contrast. They render all four sections at 800×560 and every palette in Light/Dark, verifying footer accessibility. To generate the ignored previews without a desktop window:

```sh
MACMON_RENDER_OUTPUT="$PWD/artifacts/appearance-preview" bash tools/verify.sh --smoke
```

On October 26, 2026, the suite passed 100 Mac tests and three release-workflow tests, zero warnings, with resource and launchd native smoke. Generated light/dark appearance and capture-section layouts were visually inspected. Native automatic macOS theme switching and display-scaling checks remain separate from headless rendering.

Native packaged checks also confirmed Command-comma entry, local Light/Ocean preview, Save applying the theme/palette to dashboard and charts, appearance restoration after a clean restart, and the automatic-capture shortcut entering the unified section. Dark/Classic was restored after testing. Selected process metric text uses theme foreground while selected, with a headless regression covering both themes, to avoid palette colors losing contrast on selection backgrounds.

## UI parity slice 2: resource cards and MicroGauge

Version 0.5.0/build 5 uses the actual MicroGauge Avalonia controls for CPU, memory,
network and storage cards. Appearance offers Bowl, Needle, Bar and None with a local
Settings preview. Gauge style saves with the other appearance settings; older
preferences default to Bowl. Cards reserve the same height for unavailable readings
and None, using four columns at dashboard widths of 840 DIPs and above and two below.
Capacity remains a separate available/total Data volume reading.

CPU is machine capacity and memory is physical usage, both scaled 0–100%. Network is
receive + send for the selected interface; storage is aggregate read + write across
block-storage drivers. Both activity cards explicitly label bytes/second and adapt
their scale to binary powers covering the current two-minute peak. A contrasting mark
shows the live peak. Numeric zero remains distinct from unavailable. Tooltips and
accessible names include exact peak values, units, scope and details.

`ResourceGaugeHistory` receives only `LiveSampleRecorded` events, independently of
chart inspection, retention, captures or restored history. Peaks expire by monotonic
age after 120 seconds, with a 256-entry safety bound. Clock resets and gaps over 2.5
seconds reset the window; an interface switch or unavailable activity baseline clears
that activity's previous peaks. No additional polling loop or per-process attribution
is introduced. Rendering uses fixed 0–100 native gauge coordinates and maps measured
values into them. Native ranges are replaced rather than mutating render-thread data.

The [MicroGauge dependency](../dependencies/microgauge/README.md) introduces
SkiaSharp 4.150.1; real Skia pixel tests verify fills/needles and peak marks, including
unavailable/None states. Packaged smoke checks exercise the native host and collectors.
Compact view follows in UI parity slice 3 below.

Validation for this slice: 106 MacMon tests, eight upstream Avalonia tests, and three
Python release-workflow tests pass; the release build has no warnings. Real Skia
rendering covers every style across both themes and all palettes, measured zero,
unavailable values, peak expiry, source changes, draft-theme isolation and reattachment.
Native arm64 packaging passes collector/launchd smoke and strict signature checks.
The packaged UI was inspected at 1000 and 800 pixels; Bar persisted across a clean
restart, then Bowl/Dark/Classic and the original width were restored. A 30-second
visible Jobs/dashboard measurement averaged 0.523% of total machine CPU (1.635% peak),
with 240.8 MiB peak resident memory; this is one host observation, not a comparative
performance guarantee.

### Published MicroGauge packages

Version 0.5.1/build 6 replaces temporary MicroGauge builds with official
MicroGauge.Avalonia and MicroGauge 1.1.0.2 from NuGet.org. The adapter is exactly
pinned and the dependency graph remains locked. The bootstrap command and local
MicroGauge source mappings are removed; MonCore's local workflow is unchanged.
Published package metadata identifies source commit 70bb712cbdcd05bfdffeb3f9f48c785b30100a66,
which includes a radial inner-range interpolation fix. The Avalonia adapter has no
source changes relative to the initial integration.

Validation after migration: locked restore confirms both MicroGauge packages came
from NuGet.org; the release build has no warnings, all 106 MacMon tests and three
Python release-workflow tests pass, and native collector/launchd smoke checks pass.
The real Skia tests exercise published assemblies across every style, theme and
palette, including zero/unavailable values, peak marks and control reattachment.

## UI parity slice 3: compact live view

Version 0.6.0/build 7 adds a four-metric compact strip to the existing window.
Compact view and Command-Shift-M enter it; Full view, Escape or Command-Shift-M
return to the dashboard. Settings/Command-comma and Always on top remain available.
Compact gauges use the same readings, two-minute peak history, scope, units, gauge
style and palette as full view. Tooltips and accessible names retain the expanded
measurement details. All four styles reserve the same height, including None and
unavailable readings. The strip is 224 DIPs high with a 720-DIP minimum width.

There is still one MonitorSession and one native collector. Sampling, usage accounting,
automatic capture rules, reports and bounded history continue in compact mode.
Chart/table presentation and launchd inventory refreshes pause while hidden. Returning
to full view refreshes the visible data and resumes Jobs when that tab is selected.
Process filters, pins, selected tab, chart inspection and dashboard splitter ratio are
preserved; compact readings stay live even while the full chart inspects a capture.

WindowViewState keeps normal full-window dimensions separately from compact width.
Both sizes and compact mode persist on clean shutdown, and Settings merges preserve
them. Maximized/minimized bounds do not replace normal dimensions; entering compact
returns to a normal window. Transitions fit the window inside the current display's
working area. Settings opens centered on screen from the short strip so its footer
remains reachable. Old preferences default to full view.

Tests cover independent size restoration, settings merges, preference round trips,
invalid dimensions, control actions and unavailable readouts. Real Skia rendering
covers all styles, themes and palettes at widths 720 and 1000, with explicit numeric
readout-fit assertions and accessible Full view controls. Native checks cover button
and keyboard round trips, retained capture inspection, shared Always on top, Settings
accessibility, compact restart at width 720, and recovery of the saved full dimensions.

A 30-second visible compact run on the development Mac averaged 0.340% of total
machine CPU (0.519% peak), with 204.8 MiB peak resident memory. This is a single-host
observation, not a comparative performance guarantee. Native QA restored the prior
full mode, dimensions, Ocean/Bar appearance and Always on top preference afterward.

Release validation passes all 110 MacMon tests and three Python release-workflow
tests with zero build warnings, plus native collector/launchd smoke checks and
strict development-bundle signature verification.


## UI parity slice 4: live resource inspectors

Version 0.7.0/build 8 adds a keyboard-accessible Details button to each resource card
in full and compact views. An owned, modeless inspector shows its resource's current
readings, definition, live update timestamp, gauge scale and two-minute peak. The
owner remains interactive, and opening a different resource replaces the inspector.
Close or Escape dismisses it and returns focus to the source button. Windows center
on screen, have bounded size and scroll long content with a reachable Close button.

CPU details include user/system shares of machine capacity, logical cores, enumerated
process count (including unreadable processes) and the measured interval. Memory
shows used/installed physical memory, estimated available bytes, cached bytes,
compressor storage and pressure. Its definition distinguishes estimates from guaranteed
allocatable memory and identifies pressure/compressed values as live adapter metadata.
Network separates receive and send for the selected interface and explains that the
adaptive throughput scale is not link speed or utilization. Storage separates read/write
activity across readable block-storage drivers from the Data volume's filesystem path
and available/total capacity. Unsupported Windows-only fields are omitted.

ResourceDetails formats immutable snapshots from the existing live sample and metadata.
No additional collector, timer or process ranking is introduced. Providers run on open
and while their inspector is visible; chart Capture and restored history cannot become
their live source. Missing live samples suppress old values, peaks and adapter metadata.
Zero remains a measured value. Mode changes, Settings, minimize and shutdown close
inspectors; a visual reparent does not prematurely dismiss them. Topmost follows the
owner, and deferred focus restoration avoids interrupting another inspector or dialog.

The initial native popup remained open but was not exposed by the Mac accessibility
and screenshot surface. The final implementation uses explicit owned windows, whose
content, controls and update timestamps are visible to native accessibility inspection.
Tests use actual pointer/keyboard input and cover lazy refresh, Escape, focus restoration,
detach/owner-close cleanup and one-inspector behavior. Formatting tests cover partial
readings, invalid CPU values, restored-data suppression and storage/capacity scope.
Rendering checks cover both themes, long values, reachable Close controls, and compact
layouts with the Details controls enabled across all styles/palettes at 720/1000 pixels.

Native arm64 checks cover all four inspectors, live CPU updates during a fixed chart
Capture, compact Network details, the 720-pixel layout, Escape/Close and restored
full/live state. The user's current Ocean/Bar appearance, window dimensions and current
tab selection are preserved. Temporary lifecycle diagnostics were removed.

Release verification passes all 118 MacMon tests and three Python release-workflow
tests with zero warnings, plus native collector and launchd smoke checks. Real-input
inspector tests verify keyboard focus restoration after close and Escape. Distribution
checks validate the development ZIP checksum and extracted native signature/runtime.

Native resize persistence uses measured client bounds (including at mode switches
and save), rather than potentially stale requested Width/Height values. This keeps
compact-width cleanup and full-window restoration consistent after native resizing.

### UI parity slice 5 — application shell

The full view now uses a compact versioned toolbar, four resource cards at the
800px minimum width, consistent 28px buttons and smaller tab headings. Gauge
descriptions remain accessible through tooltips and the live Details windows;
secondary automatic-capture tools expand on demand. Dashboard/table resizing and
separate full/compact sizes remain intact. New installations default to 1000×900;
existing saved dimensions are preserved. The dashboard remains scrollable at
small heights and when secondary tools are expanded.

Verification: 119 Mac tests, three release-workflow tests, zero build warnings,
and native resource/launchd smoke passed. Headless shell renders cover minimum
and default sizes, both themes, card placement, chart sizing and data-pane space.

### UI parity slice 6 — chart presentation

One resource-history surface now supports Overlay, Small Multiples, Heatmap and
Focus. Four color-matched checkboxes control visible series; Focus selects a
visible metric and hiding it selects the next available metric. All-hidden is an
explicit empty state. Layout, metric mask and focus persist in version-1 settings
and survive Settings edits and compact-mode transitions. Old settings default
to Overlay/all metrics.

CPU/memory retain 0–100% scales. Network receive+send and aggregate storage
read+write have independent binary throughput scales, labeled in bytes/s; overlay
height is relative to each labeled scale. Null, non-finite and interval-gap values
are missing, rather than zero. Lines break across collector gaps and network
source changes. Heatmap cells cover at most one observed second. Capture markers
follow the visible metric. Settings appearance previews use the same renderer.

Verification: 123 Mac tests, three release-workflow tests and native resource/
launchd smoke passed with zero warnings. Rendered all four modes in both themes
with all/single/no metrics, missing observations and selected timestamps; inspected
Small Multiples and Heatmap output. Scale/persistence tests include expired peaks,
unknown settings, hidden focus and preservation through Settings edits.
