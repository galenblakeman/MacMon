# Historical MicroGauge Avalonia integration

MacMon 0.5.1–0.8.0 used the official MIT-licensed NuGet packages
[MicroGauge.Avalonia 1.1.0.2](https://www.nuget.org/packages/MicroGauge.Avalonia/1.1.0.2)
and [MicroGauge 1.1.0.2](https://www.nuget.org/packages/MicroGauge/1.1.0.2).
Both packages identify upstream source commit
[70bb712cbdcd05bfdffeb3f9f48c785b30100a66](https://github.com/galenblakeman/MicroGauge/commit/70bb712cbdcd05bfdffeb3f9f48c785b30100a66).
Those versions pinned the adapter exactly; lock files fix the complete dependency graph,
including core 1.1.0.2. MicroGauge packages restore from NuGet.org. Only MonCore uses
the local feed; no MicroGauge checkout or bootstrap is required.

The adapter targets .NET 10 and Avalonia 12.1.4 and requires SkiaSharp 4.150.1.
MacMon explicitly pins that Skia version, including Linux native assets for headless
rendering, rather than accepting Avalonia's older 3.x transitive version. Run
`bash tools/verify.sh --smoke` and native package checks after dependency updates.
Those application bundles included the upstream [MIT license](LICENSE) at
`Contents/Resources/MicroGauge.LICENSE`.

The initial resource-card slice used temporary local packages from commit 7100922
before the Avalonia package was published. Version 0.5.1/build 6 replaces them with
the official packages and removes the MicroGauge bootstrap and local source mapping.
The published source includes an inner radial-range interpolation correction; the
Avalonia adapter's source is unchanged from the initial integration.

MacMon 0.9.0 ports the current WinMon `MetricGauge` geometry and 98-pixel
readout layouts directly to Avalonia, including Segmented Arc, Left Bar, Bottom
Bar and Segmented Bar. It no longer references or bundles MicroGauge. This file
and the license remain as provenance for earlier releases. The explicit
SkiaSharp pins remain validated by headless rendering and native packaging.
