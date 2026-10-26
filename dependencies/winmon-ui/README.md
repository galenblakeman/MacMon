# WinMon UI source provenance

MacMon 0.9.0 adapts the current WinMon gauge geometry and resource-readout layout
to Avalonia. The comparison used the sibling WinMon checkout at commit
`dcac7448a8389185be38a9f40b98433596cf4190`; these source files were clean at inspection:

| WinMon source | SHA-256 |
| --- | --- |
| `src/WinMon/Controls/MetricGauge.cs` | `5094a2f68602a79ba67d181108f0b17dc8be222f1305e7db9f07959b044cee6d` |
| `src/WinMon/Controls/MetricReadout.cs` | `15ae60602fa844ea030cbeeae47320c7e251b02ba3bb766dbdf9c3790fc643fb` |

The Avalonia adaptations are [ResourceGaugeMarks](../../src/MacMon/ResourceGaugeMarks.cs)
and [ResourceGauge](../../src/MacMon/ResourceGauge.cs). The drawing uses Avalonia
primitives; it does not reference WPF or add any native metric collection.
The original [MIT license](LICENSE) is retained here and packaged as
`Contents/Resources/WinMon.LICENSE`.

Window chrome, chart controls and table layouts were also compared with the same
WinMon checkout. MacMon retains native Mac window controls, verified process
lifetimes, read-only launchd scope, and explicit unavailable readings for
unsupported platform metrics.

Settings themes and palettes additionally adapt `Preferences/ThemeFinishes.cs`
and `Preferences/ChartPalettes.cs` from the same clean reference commit. Their
catalog values are in `WinMonAppearanceCatalog.cs`; macOS uses solid Avalonia
surfaces and retains contrast adjustment. The existing MIT license covers these
adaptations as well.
