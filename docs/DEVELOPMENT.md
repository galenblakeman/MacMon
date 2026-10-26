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

The old Windows restart format is unchanged. Saving portable domains, timestamps, availability states or independent process frames through that serializer returns false before writing. MacMon does not restore Windows history or persist Mac history yet. Grouped pins and usage snapshots record their identity policy and reject mismatched policy restoration. Portable CSV appends lifetime-domain/start-time columns and leaves unavailable CPU empty; Windows CSV columns remain unchanged.

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

Outstanding platform checks: Intel execution; actual sleep/wake and reboot behavior. Simulated clock/wake gaps are covered. Future features include GPU, launchd, process actions, Mac-specific persistence and signed/notarized distribution.

## Parity implementation progress

Work proceeds in separately verified commits on `codex/winmon-parity-slices`.

1. Memory usage/pressure: implemented. Release build has zero warnings; 30 collector tests and native smoke pass. Packaged Apple Silicon app visually checked for memory card wrapping, live pressure and separate CPU/memory traces.
2. Grouped processes, pins and sparklines: implemented. 35 collector/presentation tests and native smoke pass; packaged app visually checked for grouping, search, pinned-only filtering, selection and trails. Pins are session-only until preferences land.
3. History inspection and CSV export: implemented. 39 collector/presentation tests and native smoke pass. Packaged app checked for Capture staying fixed while top readings advance, chart marker, 10-minute range selection and a native-dialog resource export verified on disk.
4. Network and disk activity: implemented. 48 tests and native smoke pass; packaged UI checked for network selection, rate readouts, separate activity chart and process disk-I/O columns.
5. Usage totals and automatic captures: pending.
6. Process tools and preferences: pending.
7. Portable persistence and distribution readiness: pending. Developer ID signing/notarization requires the maintainer's signing identity; Intel execution requires Intel hardware. Real sleep/wake and reboot checks remain outstanding.

GPU and launchd require separate capability research. Windows runtime, smoke, performance and Store verification are complete per maintainer report; they do not need to be repeated for Mac-only UI/collector changes.

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
