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

The system reader uses `host_statistics(HOST_CPU_LOAD_INFO)` for cumulative CPU states, `sysctlbyname(hw.memsize)` for installed RAM and `hw.logicalcpu` for machine capacity. The host port is released on disposal. `DriveInfo` reads the configured volume's capacity, defaulting to `/System/Volumes/Data`. Memory and disk capabilities currently provide capacity only, without pressure, used-memory estimates or disk activity.

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

Outstanding platform checks: Windows `Verify.ps1` including relevant smoke/performance and Store gates; Intel execution; actual sleep/wake and reboot behavior. Simulated clock/wake gaps are covered. Future features include memory pressure, network/I/O, GPU, launchd, process actions, Mac-specific persistence and signed/notarized distribution.
