# WPF source patches

Managed-project patches against the pinned `dotnet/wpf` **submodule** at
`third_party/dotnet-wpf`. That is the only in-tree path for project
references and for applying `patches/`. Do **not** add the submodule to
`NovaPresentationFramework.slnx` (it would pull WpfGfx). Reference
individual `PresentationCore` / `PresentationFramework` / `WindowsBase`
csprojs from the submodule when the host slice lands.

## Fresh worktree setup — ONE command (REQUIRED reading)

A clean worktree has a long, easy-to-get-wrong bootstrap: submodule init,
the full 15-directory sparse cone, **9 case-alias symlinks** (Linux is
case-sensitive and the csprojs reference differently-cased paths), the full
17-patch series, and a strict build order where the theme assemblies
(`PresentationFramework.Classic`, `PresentationUI`) must be built **before**
any consumer (harness / tests / samples) — otherwise the consumer build
**silently drops** those references (MSB3245 is only a warning) and the
whole UI renders nothing at runtime.

**Do not hand-roll this.** Run the bootstrap script from the repo root:

```sh
scripts/bootstrap-worktree.sh            # submodule + cone + aliases + patches + build
scripts/bootstrap-worktree.sh --tests    # ... + all 19 test suites (sequential)
scripts/bootstrap-worktree.sh --harness  # ... + `harness all` + `feat all`
```

The script is idempotent (safe to re-run), fail-loud, and finishes by
running `patches/verify-series.sh` and printing the MATCH count. It never
initializes/updates an already-initialized submodule, never forces patches
onto a dirty tree, and builds in the correct dependency order
(SdlSource → PresentationFramework → Classic → PresentationUI → samples).
If it errors, the message names the fix; do not improvise past it.

Manual equivalent (only when the script is unavailable — every step is
required, in this order):

```sh
# 1. Submodule at the pin (below). Fresh worktree only: never run
#    `git submodule update`/`checkout`/`reset`/`clean`/`stash` against an
#    already-initialized submodule — its dirty working tree IS the applied
#    patch series.
git submodule update --init third_party/dotnet-wpf
cd third_party/dotnet-wpf
git sparse-checkout init --cone
git sparse-checkout set \
  eng/WpfArcadeSdk \
  src/Microsoft.DotNet.Wpf/cycle-breakers \
  src/Microsoft.DotNet.Wpf/src/Common \
  src/Microsoft.DotNet.Wpf/src/PresentationCore \
  src/Microsoft.DotNet.Wpf/src/PresentationFramework \
  src/Microsoft.DotNet.Wpf/src/PresentationUI \
  src/Microsoft.DotNet.Wpf/src/ReachFramework \
  src/Microsoft.DotNet.Wpf/src/Shared \
  src/Microsoft.DotNet.Wpf/src/System.Printing \
  src/Microsoft.DotNet.Wpf/src/System.Windows.Input.Manipulations \
  src/Microsoft.DotNet.Wpf/src/System.Windows.Primitives \
  src/Microsoft.DotNet.Wpf/src/System.Xaml \
  src/Microsoft.DotNet.Wpf/src/Themes \
  src/Microsoft.DotNet.Wpf/src/UIAutomation \
  src/Microsoft.DotNet.Wpf/src/WindowsBase
git checkout 1346571efc19a83a90edf3abe9059d18f8412cdb
cd ../..
# The cone must EXCLUDE PresentationBuildTasks: its presence (a full
# checkout, or a partial cone) breaks the impl build with NETSDK1004 on
# PresentationBuildTasks.csproj (net6.0). Verify the exclusion, not just
# the 15 inclusions — a fresh `submodule update` checks out everything.

# 2. The 9 case-alias symlinks (ALL of them; a partial set fails with
#    CS2001 "source file could not be found"). `target` is the on-disk
#    dir, `link` is the differently-cased alias the csprojs reference.
ln -sfn internal   third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/Internal
ln -sfn MS         third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/Shared/ms
ln -sfn Internal   third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/Shared/MS/internal
ln -sfn Generated  third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/Shared/MS/Internal/generated
ln -sfn InterOp    third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Interop
ln -sfn documents  third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/PresentationFramework/MS/Internal/Documents
ln -sfn Windows    third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/windows
ln -sfn manager    third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/ReachFramework/Serialization/Manager
ln -sfn Packaging  third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/ReachFramework/packaging

# 3. Apply the FULL series, in order, straight (no --3way, no fuzz).
P=patches
git -C third_party/dotnet-wpf apply $P/0001-duce-nests-forwarder-ivt.patch
git -C third_party/dotnet-wpf apply $P/0002-window-sdlsource.patch
git -C third_party/dotnet-wpf apply $P/0003-linux-managed-restore.patch
git -C third_party/dotnet-wpf apply $P/0004-forwarder-stubs.patch
git -C third_party/dotnet-wpf apply $P/0005-linux-host-traps.patch
git -C third_party/dotnet-wpf apply $P/0006-linux-classic-theme.patch
git -C third_party/dotnet-wpf apply $P/0007-linux-line-services.patch
git -C third_party/dotnet-wpf apply $P/0008-window-hwnd-off-show.patch
git -C third_party/dotnet-wpf apply $P/0009-popup-sdlsource.patch
git -C third_party/dotnet-wpf apply $P/0010-extended-assembly-info-incremental.patch
git -C third_party/dotnet-wpf apply $P/0011-xaml-access-level-linux-guard.patch
git -C third_party/dotnet-wpf apply $P/0012-linux-dispatcher-run.patch
git -C third_party/dotnet-wpf apply $P/0013-textedit-dragselect-guard.patch
git -C third_party/dotnet-wpf apply $P/0014-linux-pts.patch
git -C third_party/dotnet-wpf apply $P/0015-linux-imaging.patch
git -C third_party/dotnet-wpf apply $P/0016-desktop-theme-ivt.patch
git -C third_party/dotnet-wpf apply $P/0017-linux-multitarget-net10.patch

# 4. Verify the series composes (must print MATCH=61 DIFFER=0, exit 0).
./patches/verify-series.sh

# 5. Build in dependency order. SdlSource pulls every Nova.* + Core +
#    WindowsBase; PresentationFramework pulls the rest of the impl set via
#    cycle-breakers; Classic and PresentationUI are NOT transitive
#    dependencies — build them explicitly BEFORE any consumer.
dotnet build src/Nova.SdlSource/Nova.SdlSource.csproj -c Debug
dotnet build third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj -c Debug
dotnet build third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Classic/PresentationFramework.Classic.csproj -c Debug
dotnet build third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/PresentationUI/PresentationUI.csproj -c Debug
dotnet build samples/ControlProbeHarness/ControlProbeHarness.csproj -c Debug
```

**Do not skip Classic/UI.** If `PresentationFramework.Classic.dll` or
`PresentationUI.dll` is missing when a consumer builds, the build SUCCEEDS
with only an MSB3245 warning and silently drops the theme references — the
UI stack then renders nothing at runtime. Since this change, the samples
and `tests/Nova.Framework.Tests` fail the build loudly instead
(`samples/Directory.Build.targets` / the `ValidateHintPathReferences`
target), but the ordering requirement remains: build the theme projects
before consumers.

### "My rebuild did nothing" — stale incremental state (artifacts/obj)

Symptom: you change a patched WPF source (or a patch), rebuild, and the
build "succeeds" but the output/behavior does not change — an old compiled
dll is reused with no error.

Cause: `CoreCompile`'s up-to-date check is **mtime-based**: a source file
whose mtime is OLDER than the previously-built intermediate dll
(`third_party/dotnet-wpf/artifacts/obj/<Project>/Debug/net11.0/<Project>.dll`)
is silently skipped even when its CONTENT changed. The
`CoreCompileInputs.cache` hashes only the file **paths**, not contents, so
content edits with old mtimes never invalidate it. Triggers:
`git archive | tar -x` or `cp -a` of a patched tree (mtime-preserving
copies), switching branches/commits inside a worktree, or re-applying
patches over an already-built tree.

Fix: remove the intermediate state and rebuild (only gitignored build
output, never tracked files):

```sh
rm -rf third_party/dotnet-wpf/artifacts/obj third_party/dotnet-wpf/artifacts/bin
scripts/bootstrap-worktree.sh      # or with --clean, which does the rm first
```

`scripts/bootstrap-worktree.sh --clean` performs that wipe before building.
The script also warns at build time when an impl project's newest source is
older than its intermediate dll, which is the precondition for this trap.
(Proven: appended a line to a PresentationCore source, backdated its mtime
one hour; the build reported success and the dll was byte-identical to
before.)

## Pin

Pin: `1346571efc19a83a90edf3abe9059d18f8412cdb` (dotnet/wpf main at survey;
"Source code updates from dotnet/dotnet (#11838)"). Also in
`patches/UPSTREAM.txt` and as the gitlink SHA.

The full 15-directory cone is required: `Themes/` holds
PresentationFramework.Classic (0006) and the theme BAML, `cycle-breakers/` +
`System.Printing` + `ReachFramework` + `System.Windows.Primitives` +
`System.Windows.Input.Manipulations` are needed to build the impl set the
runtime bundle ships, and `eng/WpfArcadeSdk` is
the Arcade SDK the WPF projects build with. Deliberately excluded:
`PresentationBuildTasks` (its net6.0 build is not part of the impl build —
with it present the WPF build tries to compile the toolchain), `WpfGfx`,
`DirectWriteForwarder`, `WindowsFormsIntegration`,
`System.Windows.Controls.Ribbon`, `Extensions`.

Verify with `git -C third_party/dotnet-wpf rev-parse HEAD` (must print the
pin). Sparse checkout and the case-alias symlinks are **local config** on
the submodule; re-apply after a fresh `submodule update`.

Do **not** keep a second clone at `/home/zznty/projects/dotnet-wpf`.

## Required: InternalsVisibleTo

`PresentationCore` must expose internals to `Nova.SdlSource` (the assembly
hosting `SdlPresentationSource`): `CompositionTarget` ctor, `Raw*` input
reports, and `MediaContext.RegisterICompositionTarget` are all internal.

```csharp
[assembly: InternalsVisibleTo("Nova.SdlSource")]
```

`Nova.SdlSource` references Core + `Nova.*`, never PresentationFramework.

## Nest files to patch

Paths below are relative to `third_party/dotnet-wpf/`.

| File | What |
|---|---|
| `src/Microsoft.DotNet.Wpf/src/Common/Graphics/exports.cs` | `DUCE.UnsafeNativeMethods` nest → managed DUCE channel |
| `src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/UnsafeNativeMethodsMilCoreApi.cs` | MilCore nest → `Nova.Mil.DuceExports` statics |
| `src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/SafeNativeMethodsMilCoreApi.cs` | partition-manager nest, same redirect |
| `src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/Composition.cs` | `MilComposition_SyncFlush` nest |
| `src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/TextFormatting/LineServices.cs` | `Lo*` nest → managed subset |
| `src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/Text/TextInterface/DWriteLoader.cs` | `LoadDWrite` no-op |
| `src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/FontCache/DWriteFactory.cs` | factory bodies → `Nova.Text` |
| `src/Microsoft.DotNet.Wpf/src/PresentationCore/ModuleInitializer.cs` | drop `NativeWPFDLLLoader.LoadDwrite` |

Prefer patching `[DllImport]` nests to managed methods. No native shim, no `wpfgfx`.

## Window.cs hunks

`src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs`
(pin line numbers):

- **~2515–2521** (`CreateSourceWindow`): create `new SdlPresentationSource(param)`
  instead of `HwndSource source = new HwndSource(param);`, and keep
  `_swh = new SourceWindowHelper(source);` wired to the retyped helper.
- **`SourceWindowHelper`** (class at ~7343): retype its `HwndSource` field and
  ctor parameter to `SdlPresentationSource`. Client/window bounds from
  `SdlWindow.Position` + `PixelSize`; `IsActive` from
  `SdlPresentationSource.IsActive`.
- **`EnsureHiddenWindow`** (~6334): still `HwndWrapper`. Not on the
  offscreen proof path. Leave until Framework Window show.

`Window.cs` is the only PresentationFramework file touched.

## Series verification (REQUIRED before any commit touches patches/)

The patch series must compose **from pristine, in order, with straight
`git apply`** — individual `--reverse --check` runs prove nothing about
composition, and `--3way` is FORBIDDEN as a verification tool: it can
**silently drop an entire file's hunks** when one hunk in that file
fails (observed: 0005's BOM hunk duplicating 0001 made `--3way`
"apply" `UnsafeNativeMethodsMilCoreApi.cs` while writing a file with
ZERO `OperatingSystem.IsWindows()` guards — the PathGeometryBounds /
RenderOptions / WpfGfx traps were lost without any error).

Run `patches/verify-series.sh` — it does the whole gate (fresh
`git archive` of the pinned SHA into a temp dir, straight apply
0001..0017, per-file MATCH/DIFFER vs the live submodule, non-zero exit
on any failed apply or DIFFER).

### Helper scripts (preferred over the manual flows)

- `patches/regenerate.sh <NNNN[-slug]>` — re-derive ONE patch's hunks as
  exactly its contribution: before-state = pristine + all prior patches,
  after-state = the live file with every LATER patch's hunks reverse-applied
  (restricted to that file). The composed patch is self-checked (apply onto
  before-state must reproduce the after-state byte-for-byte) before it
  replaces the original (backed up to patches/.regenerate-backup/), then
  verify-series runs. Use it after editing a file owned by an existing
  patch, instead of the manual strip-SERIES/rebuild-intermediate dance.
  If a later patch no longer reverse-applies, regenerate NEWEST-FIRST.
  The script also normalizes stale artifacts: no-op hunks (e.g. a BOM hunk
  whose BOM was already stripped by an earlier patch) are dropped, and
  files listed in several diff sections merge into one section.

- `patches/new-patch.sh <NNNN> <slug> "<one-line description>"` — capture
  the live submodule's changes not covered by any registered patch into a
  new patch, register it in the THREE required places (verify-series.sh
  SERIES, bootstrap-worktree.sh SERIES, README registry stub), and run
  verify-series. Fill in the README stub's narrative afterwards.

Regenerate a patch file's section with `git diff` between the correct
intermediate state and the live working tree, never by hand-editing
hunks. Each hunk must be owned by exactly ONE patch; a hunk duplicated
across patches (e.g. 0005 re-adding `ForwarderStubs.cs` /
`NovaFontTypes.cs` that 0004 already adds, or 0005 repeating 0001's
BOM strip) breaks forward composition.

Full gate:

```sh
# 1. pristine tree at the pin, WITHOUT touching the real submodule
mkdir -p /tmp/nova-patch-verify && cd /tmp/nova-patch-verify
git -C /home/zznty/projects/NovaPresentationFramework/third_party/dotnet-wpf \
  archive HEAD | tar -x
git init -q . && git add -A && git -c user.email=v@v -c user.name=v commit -qm base

# 2. apply the FULL series in order, STRAIGHT (no --3way, no fuzz)
P=/home/zznty/projects/NovaPresentationFramework/patches
for f in 0001-duce-nests-forwarder-ivt 0002-window-sdlsource \
         0003-linux-managed-restore 0004-forwarder-stubs \
         0005-linux-host-traps 0006-linux-classic-theme \
         0007-linux-line-services 0008-window-hwnd-off-show \
         0009-popup-sdlsource 0010-extended-assembly-info-incremental \
         0011-xaml-access-level-linux-guard 0012-linux-dispatcher-run \
         0013-textedit-dragselect-guard \
         0014-linux-pts \
         0015-linux-imaging \
         0016-desktop-theme-ivt \
         0017-linux-multitarget-net10; do
  git apply "$P/$f.patch" || echo "FAILED: $f"
done
# every line must print nothing; any FAILED is a series bug

# 3. every patched file must match the live submodule working tree
LIVE=/home/zznty/projects/NovaPresentationFramework/third_party/dotnet-wpf
git -C "$LIVE" status --short | awk '{print $2}' | while read f; do
  [ -f "$f" ] || continue           # skip case-alias symlink dirs
  diff -q "$f" "$LIVE/$f" >/dev/null || echo "DIFFER: $f"
done
# empty output = series reproduces the working tree; any DIFFER is
# either a genuine series bug or legitimately in-flight work not yet
# mirrored into a patch — never commit a DIFFER silently.
```

When a file is touched by more than one patch, regenerate each patch's
section against the correct intermediate state (the state produced by
all EARLIER patches). Known multi-owner files: `LineServices.cs`
(0005 + 0007), `PresentationCore.csproj` (0001 + 0004 + 0005 + 0007),
`UnsafeNativeMethodsMilCoreApi.cs` (0001 + 0005 + 0015 + 0026),
`Dispatcher.cs` (0005 + 0012),
`Application.cs` (0012 + 0027),
`PresentationFramework.csproj` (0001 + 0002 + 0005 + 0014). 0005 also owns
(single-owner) `PresentationFramework/System/Windows/Standard/NativeMethods.cs`
and `PresentationCore/System/Windows/DragDrop.cs` (win32-image-stubs
workstream, 2026-08-20).

## Rules

- Patches in this directory are the only allowed diffs against managed
  projects (PresentationCore / PresentationFramework / WindowsBase).
- **Do not vendor WpfGfx.** No native code, no wpfgfx build, no DXVK/Skia.
- The submodule is never added as a solution folder of native projects.
- `0051-pts-paravisual-offset.patch` — paragraph visuals at their arranged rects
  (2026-08-28). The visual chain positions only LINE visuals (paragraph-relative
  vrStart offsets); nothing ever placed the paragraph visual itself, so every
  paragraph in a FlowDocument rendered on top of the first line. The visual
  update walk now offsets each paragraph visual by its arranged track-relative
  rect (BaseParaClient._rect) before validating it. Patched file:
  `PresentationFramework/MS/Internal/PtsHost/PtsHelper.cs`, generated against
  the intermediate state after 0050-linux-managed-dragloop. Live-verified in
  the gallery RichTextBox (multi-paragraph content stacks correctly).

- `0001-duce-nests-forwarder-ivt.patch` — nests → `Nova.Mil.DuceExports`,
  Forwarder ProjectReference dropped,
  `InternalsVisibleTo($"Nova.SdlSource, PublicKey={BuildInfo.WCP_PUBLIC_KEY_STRING}")`,
  `LoadDWrite` no-op.
- `0002-window-sdlsource.patch` — `Window.CreateSourceWindow` constructs
  `SdlPresentationSource`; `SourceWindowHelper` retyped; Framework refs
  `Nova.SdlSource`. Linux `Show`/`Hide` call
  `SdlPresentationSource.Show`/`Hide`; `Close` runs `WmClose` instead
  of `SendMessage`. Style bits stay on the cached `_Style`/`_StyleEx`.
  HWND-only post-create (icon, taskbar filter, Fluent ThemeManager)
  is skipped. After Show, same-thread `Invoke` drains `Loaded`.
- `0003-linux-managed-restore.patch` — WPF `NuGet.config` maps every dnceng
  feed (`*` pattern) so parent `packageSourceMapping` does not NU1100 them;
  `RunRefApiCompat=false`; `PerlCommand=/usr/bin/perl` after Arcade overwrite.

  Re-apply after a clean checkout: run `scripts/bootstrap-worktree.sh`
  from the repo root, or apply the FULL 17-patch series manually exactly
  as shown in "Fresh worktree setup — ONE command" above. The old habit
  of applying 0001..0005 only leaves the tree half-patched (e.g.
  `SdlPresentationSource` fails with CS1061 `RegisterLinuxEventLoop`
  because 0012 adds it) — always apply the whole series. Do NOT
  reverse-check individual patches against the fully-patched tree; a
  later patch may have modified the same multi-owner file, so a single
  patch's `--reverse --check` fails even when the series IS complete.
  `verify-series.sh` is the authoritative completeness check.
- `0004-forwarder-stubs.patch` — `MS.Internal.Span` class plus
  Factory/Font/FontFace/FontCollection/TextAnalyzer backed by
  fontconfig + FreeType + HarfBuzz (`NovaFontTypes.cs`).
  `TrueTypeSubsetter` still throws. PresentationCore refs
  `Nova.FontConfig` / `Nova.FreeType` / `Nova.HarfBuzz` / `Nova.Geometry`.
- `0005-linux-host-traps.patch` — Linux host traps. Null-guard
  `Registry.CurrentUser`, stub `RegisterWindowMessage` / QPC /
  `IsDebuggerPresent` / `GetKeyState`, skip EventTrace ETW `Register`
  and Cicero TIP probe, skip STA on `InputManager`, skip
  `MessageOnlyHwndWrapper`. Same-thread `Invoke` drains via
  `DrainLinuxQueue` and skips `MsgWaitForMultipleObjectsEx`. Fonts URI
  is `/usr/share/fonts/`; DPI from `HostTheme.PixelsPerInch` (96);
  `OSVersionHelper` reports Windows 10. System colors / SPI RECT-int-
  bool-HIGHCONTRAST-NONCLIENTMETRICS / `GetSystemMetrics` /
  `GetDoubleClickTime` / high-contrast / `IsThemeActive` /
  `IsProcessDPIAware` marshal through `Nova.SystemTheme.HostTheme`.
  WindowsBase, PresentationCore, and UIAutomationTypes ProjectReference
  `Nova.SystemTheme`. Linux `MediaContext` skips the HWND notification
  window and milcore `MediaSystem.Startup` so
  `RegisterICompositionTarget` can run. `SystemResources` skips the
  HWND theme listener. Theme BAML skips `XamlAccessLevel` (CAS leftover
  PNSE on Linux). Inactive `ThemeName` is `"Classic"` so Linux
  `Assembly.Load` finds `PresentationFramework.Classic.dll`.
  `BeginInvoke` enqueues only; same-thread `Invoke` drains via
  `DrainLinuxQueue`. Click path: `_reportedButtons`,
  `GlobalHitTest` → `LocalHitTest` for non-HWND, managed even-odd
  polygon fill. Linux `Classification` cctor loads
  `Nova.Classification` pinned `short***` tables (own Core
  ItemGroup). `LoGetEscString` fills process-lifetime WCHAR pins
  (U+2029 / U+2028 / U+FFFF / U+00A0 / U+0009 / U+FFFC). **Those
  six values are unverified against `lslo.cpp` / PresentationNative**;
  `LoGetEscString_LinuxPinsExpectedWchars` only reads back what we
  stored. `lslo.cpp` is not in public `dotnet/wpf`. Wrap / object-run
  vs Windows LS is still outstanding. Linux `MediaSystem.Startup`
  connects the managed DUCE transport so `Compile` writes RenderData
  (`OffscreenSource_WpfDrawingVisual_ReadbackShowsRed`). **Not** a
  user32 message-loop shim; `PushFrame`/`GetMessageW` still HWND.

  Geometry workstream (2026-08-20): `MilUtility_PolygonBounds` and
  `MilUtility_PathGeometryCombine` gain Linux branches that run in
  `Nova.Geometry2D` — `MilPathFlattener` (Bezier flattening from the
  MIL path-data wire format) + `PolygonBounds` / `Combiner`
  (boolean combine, sweep-line + vendored LibTessDotNet under
  `src/Nova.Geometry2D/Tess/`, SGI Free Software License B — see
  NOTICE). This fixes `Line` layout (`LineGeometry.GetBoundsHelper`)
  and popup layout-clip (`FrameworkElement.GetLayoutClip` → `Geometry.
  Combine`) — ComboBox dropdown / Menu submenu open and
  `Line`-with-stroke were crashing on wpfgfx_cor3 DllNotFound.
  `Geometry.cs` hunks stay owned by 0005 (single owner).

  win32-image-stubs workstream (2026-08-20): the last three interactive
  Win32 traps get the 0005 pattern —
  `UnsafeNativeMethodsCLR.GetActiveWindow` returns `IntPtr.Zero` on Linux
  (AccessKeyManager null-scope Enter/Escape; all callers treat zero as "no
  active window"); `Standard.NativeMethods.DwmIsCompositionEnabled` returns
  `true` (dwmapi import would fire because 0005's OSVersionHelper reports
  Win10; Aero/glass paths behave, `SystemParameters.IsGlassEnabled` safe);
  `DragDrop.DoDragDrop` validates arguments then returns `DragDropEffects.
  None` on Linux as a documented no-op (the Windows body moved to private
  `DoDragDropCore` so `DataObject`/System.Private.Windows.Ole is never
  JIT'd — no DllNotFound/FileNotFound/abort); `SecurityHelper.
  MapUrlToZoneWrapper` returns `URLZONE_LOCAL_MACHINE` on Linux (urlmon
  COM-marshaled import cannot JIT — discovered probing the BitmapDecoder
  file path). Imaging is DEFERRED but the factory/WIC gates in
  `UnsafeNativeMethodsMilCoreApi.cs` are stubbed: `MILFactory2.
  CreateFactory`, `WICCodec.CreateImagingFactory` and `MilCoreApi.
  CreateCWICWrapperBitmap` throw catchable
  `PlatformNotSupportedException("imaging is not yet implemented on this
  host")` on Linux (Windows paths byte-identical via private `*Core`
  imports), so `Image`/`BitmapSource` types in a graph build and render
  nothing, and explicit decode/create fails clearly. Proven by
  `Win32ImageStubTests` in tests/Nova.Framework.Tests (10 tests). New
  single-owner 0005 files: `Standard/NativeMethods.cs`, `DragDrop.cs`.

  uxtheme workstream (2026-08-20): every uxtheme entry point reachable once
  `IsThemeActive` is true gets the 0005 pattern, so a themed (Aero2) dictionary
  actually loads on Linux. `UnsafeNativeMethods.GetCurrentThemeName`
  (Shared/MS/Win32/UnsafeNativeMethodsCLR.cs) branches on Linux to
  `HostTheme.FillCurrentThemeName` (real DllImport moved to private
  `GetCurrentThemeNameCore`, EntryPoint preserved); `Standard.NativeMethods.
  IsThemeActive` and `Standard.NativeMethods.GetCurrentThemeName` (Standard/
  NativeMethods.cs, same file as the DwmIsCompositionEnabled hunk) branch to
  `HostTheme.IsThemeActive` / `HostTheme.UxTheme*`. `PresentationFramework.
  csproj` gains a standalone Nova.SystemTheme ProjectReference (required —
  Arcade sets `DisableTransitiveProjectReferences`, so the WindowsBase/
  PresentationCore refs do not flow). `HostTheme` (src/Nova.SystemTheme/
  SystemTheme.cs) adds the opt-in: `NOVA_THEME` env var (or `SetTheme`)
  selects `classic` (default, bit-identical legacy behavior) / `aero` /
  `aero2`; `GetCurrentThemeName` reports the matching `*.msstyles` file.
  On the Win10 version OSVersionHelper reports, stock UxThemeWrapper maps
  `aero`→`Aero2`, so both `aero` and `aero2` load the Aero2 dictionary.
  Themed Aero2 is proven by tests/Nova.Framework.Tests/WindowThemeTests.cs
  (dictionary source + dominant-interior-fill pixel shift vs Classic) and by
  `harness all`/`feat all` under `NOVA_THEME=aero2` (zero run-failed).
  UxTheme entry points that stay UNGUARDED (theoretically reachable only):
  `CriticalSetWindowTheme` (HwndSource ctor — not constructed on Linux),
  `Standard.NativeMethods.SetWindowThemeAttribute` + `UnsafeNativeMethodsOther.
  SetWindowThemeAttribute` (`#if WCP_SYSTEM_THEMES_ENABLED`, NavigationWindow
  only), `Begin/Update/EndPanningFeedback` (HwndPanningFeedback, HwndSource
  only; Window's SourceWindowHelper panning is already a no-op), and the
  UIAutomationClientSideProviders `OpenThemeData`/`GetThemePartSize`/
  `CloseThemeData` (separate automation-proxy assembly, not in the impl
  bundle). 0005 is now multi-owner on `PresentationFramework.csproj`
  (0001 + 0002 + 0005 + 0014); its csproj section was regenerated against
  pristine + 0001..0004.
- `0006-linux-classic-theme.patch` — PresentationUI uses
  `System.Printing-ref` (not the C++ vcxproj) and `Themes/` Page
  paths (Linux case). Sparse cone must include
  `src/Microsoft.DotNet.Wpf/src/Themes`. Classic compiles and BAML
  loads. uxtheme workstream (2026-08-20): `Themes/PresentationFramework.
  Aero/PresentationFramework.Aero.csproj` also gets the Linux-case fix —
  its on-disk dir is lowercase `themes/` while the csproj said
  `Themes\Aero.NormalColor.xaml`, so the Page include is corrected to
  `themes\Aero.NormalColor.xaml` (Aero2/Classic/AeroLite already match
  on disk). This file is new to 0006 (single-owner), which raises the
  verify-series MATCH count by 1 (57 → 58). `SdlPresentationSource.
  RootVisual` now matches `HwndSource`:
  `PropagateResumeLayout` + `SetLayoutSize`. Proven:
  `Window_Show_Rectangle_LaysOut` (40×20 ActualSize) and
  `Window_Show_TextBlock_LaysOut` (`ActualWidth`/`ActualHeight` > 0)
  after `0005` loads `Nova.Classification` tables and fills
  `LoGetEscString`. Remaining `Lo*` (`LoCreateLine`, …) still HWND /
  PresentationNative.

  Linux case-sensitive checkout also needs 9 local case-alias symlinks
  (not in the patch; they are local checkout config, like sparse-checkout).
  The canonical list is in "Fresh worktree setup — ONE command" above —
  `scripts/bootstrap-worktree.sh` creates them; the manual fallback shows
  all 9. A partial set fails with CS2001 "source file could not be found"
  on the aliased paths.

- `0007-linux-line-services.patch` — wires the whole
  `LineServices.UnsafeNativeMethods` `Lo*` surface to the isolated
  `Nova.LineServices` engine on Linux. Each nest method keeps its
  Windows `DllImport` as a private `*Impl` and branches on
  `OperatingSystem.IsWindows()`; the Linux path field-copies nest
  structs into `Nova.LineServices` structs and calls
  `LoExports`. `LsContextInfo` is copied field-by-field — its typed
  delegate slots (GetAutoNumberInfo, Hyphenate, DrawTextRun, …)
  become pointer-sized `IntPtr` slots (function pointers for the
  non-driven callbacks) and the five callbacks the engine drives
  (FetchPap, FetchLineProps, GetRunCharWidths, GetRunTextMetrics,
  FetchRunRedefined) stay WPF delegate types behind trampolines that
  adapt `LsPap` / `LsLineProps` / `LsChp` / `LsTxM` field-by-field;
  **no `Unsafe.As`** on `LsContextInfo`. `pols` passes through as the
  context handle (Nova fills it, like the native API). Outputs
  (`LsLInfo`, `LsLineWidths`, `LsTextCell`, `LsBreaks`) and inputs
  (`LsDevRes`, `LsTbd*`, `LSPOINT`, `LSRECT`) are field-copied;
  `LoGetEscString` keeps the nest `FillLinuxEscString` pins (U+0009
  object terminator). PresentationCore gains a **standalone**
  `ProjectReference` ItemGroup for `Nova.LineServices` (same pattern
  as Classification in 0005 — no restack of 0001/0004). Break
  records, `ccpLim`, and object handlers remain honest v1 stubs in
  `LoExports`; this is **not** Microsoft LS. Verified: PresentationCore
  impl builds, `tests/Nova.LineServices.Tests` 23/23.

  Lineservices workstream (2026-08-20): the `Lo*` surface now drives
  real wrap/bidi/object-run behavior — `LoCreateLine` /
  `LoBreakOpt` / `LoGetBreak` / `LoGetDisplayLine` /
  `LoQuery*` implement greedy line breaking, Unicode bidi
  reordering, and caret hit-testing (`PointToCp`/`CpToPoint`) /
  line enumeration in `Nova.LineServices` (`LoExports` + `LoTypes` +
  `LoDelegates`). TextBlock `TextWrapping=Wrap` and RTL text render
  (no `ThrowExceptionFromLsError` / zero-length-line aborts); caret
  round-trips through the managed engine. Handle tables in
  `Nova.LineServices` (context/line/break handles) are converted to
  `GCHandle` — `NextPointer()` is monotonic and never reused, so
  `GCHandle` slots (which ARE reused after `Free`) would alias a
  stale context onto a new line; `DuceExports`/`DuceRuntime` handles
  deliberately stay pointer-keyed (per the DuceRuntime invariant).
  Suite: `tests/Nova.LineServices.Tests` 41/41, Framework caret/RTL
  tests (TextFormatterCaretTests, WindowTextBlockRtlWrapTests).
  Optimal-paragraph breaking is a deliberate v1 omission (greedy
  wrapping only).
- `0008-window-hwnd-off-show.patch` — leftover `Window.cs` HWND **off
  Show** routes through `SdlPresentationSource` on Linux
  (`OperatingSystem.IsWindows()` gates, Windows path unchanged):
  `WindowState` ShowWindow SW_RESTORE/MAX/MIN →
  `source.Restore/Maximize/Minimize`; `Topmost` SetWindowPos →
  `source.BringToFront()` (SDL3 has no HWND_TOPMOST z-order insert; a
  single raise, not persistent always-on-top — `SDL_SetWindowAlwaysOnTop`
  exists but is intentionally not wired); Get/SetWindowPlacement
  (RestoreBounds, restore-bounds updates) → `GetPlacement` +
  `SetBounds` (SDL co-ods are screen co-ods, no work-area transform);
  SetWindowLong GWL_HWNDPARENT → `SetOwner` (SDL_SetWindowParent;
  foreign non-SDL owner handles are untouched on Linux); DragMove
  WM_SYSCOMMAND/SC_MOUSEMOVE stays Windows-only (SDL3 has no drag
  loop — do not fake it). `SdlPresentationSource.BringToFront()` is
  public (CA1030 forbids public `Raise*`). No Framework IVT — an
  internal `CompositionFrame` ctor would still force CS0012 under
  Arcade `DisableTransitiveProjectReferences`. Smoke pumps via
  `TryPump` / `Present` / `IsClosing`. Handles stay SDL pointers;
  no fake HWND.

- `0009-popup-sdlsource.patch` — `PopupSecurityHelper` builds an SDL-backed
  `SdlPresentationSource` on Linux instead of `new HwndSource(param)`
  (Popup.cs: `BuildWindow`, `SetPopupPos` → `SetBounds`, window/parent rects
  → `GetPlacement`, `Show/HideWindow`, `SetHitTestable`, `GetMouseCursorPos`
  fallback → SDL global mouse, `ClientToScreen` → no-op (SDL client co-ods
  are screen co-ods), `ConnectedToForegroundWindow` → owner check,
  `GetPlacementOrigin`/`GetScreenBounds` → owner rect / synthetic monitor,
  `HwndTarget.BackgroundColor` skipped). `_window` retyped to
  `PresentationSource` (common base of `HwndSource` and
  `SdlPresentationSource`); `_sdlWindow` holds the Linux source; Windows
  paths byte-identical via `OperatingSystem.IsWindows()` gates. Also in
  0002: `CloseWindowFromWmClose` disposes the SDL source on Linux so its
  `Disposed` event drives `OnSourceWindowDisposed` → `InternalDispose`
  (mirrors the Windows WM_DESTROY flow) and drains DuceRuntime bindings +
  channel mappings. The monitor/rect/capture/client-screen guards
  (`SafeNativeMethodsCLR`, `UnsafeNativeMethodsCLR`) are in 0005.

  sdl-fixes workstream (2026-08-20): regenerated — `Popup.
  GetMouseCursorSize` gets a Linux branch returning the SDL cursor
  default (32×32, hotspot 0,0) instead of user32 `GetCursor`
  (DllNotFound), so ToolTip on default `Mouse` placement opens and
  renders (`PopupWindowPixelSize`/`SetPopupPos` unchanged). Mouse
  position already arrives desktop-relative in device pixels
  (`SDL_GetGlobalMouseState`), same space SDL positions popups in.
  No logical/device conversion on the mouse path.

- `0010-extended-assembly-info-incremental.patch` — incremental-build fix
  for `eng/WpfArcadeSdk/tools/ExtendedAssemblyInfo.targets`
  (`CoreGenerateExtendedAssemblyInfo`): drop the `<Delete>` of
  `$(GeneratedExtendedAssemblyInfoFile)` so `WriteLinesToFile`
  `WriteOnlyWhenDifferent="true"` preserves the file mtime when content
  is unchanged. Without this the file was deleted+rewritten every build,
  making `CoreCompile`'s input newer than its output, so Csc recompiled
  the whole assembly plus all 13 satellite resource assemblies on every
  no-change build (~10 s warm). With it, a warm no-change PresentationCore
  build is ~1.5 s with `CoreCompile` and satellites skipped. All generated
  attributes (CLSCompliant, DefaultDllImportSearchPaths,
  AssemblyDefaultAlias, AssemblyDescription, AssemblyMetadata) are kept;
  content change still triggers a rewrite. This file is not owned by any
  earlier patch (single-owner hunk).

- `0011-xaml-access-level-linux-guard.patch` — `XamlReader.LoadBaml`
  (PresentationFramework/System/Windows/Markup/XamlReader.cs) guards
  `XamlAccessLevel.AssemblyAccessTo(streamInfo.Assembly)` with
  `OperatingSystem.IsWindows()`, taking the proven `accessLevel == null`
  branch on Linux. The type lives in `System.Windows.Extensions` and its
  `AssemblyAccessTo` throws `PlatformNotSupportedException` on non-Windows,
  so any consumer app whose compiled XAML references a local type (which
  makes MarkupCompilePass emit `XamlGeneratedNamespace.GeneratedInternalTypeHelper`,
  whose presence selects this branch) crashed in `InitializeComponent` even
  though compilation succeeded. Same pattern as 0005's SystemResources
  guard; Windows path byte-identical. Proven by `samples/Nova.XamlSample`
  (public-only and local-type XAML, both compile and load headless). This
  file also carries the only BOM-strip hunk for XamlReader.cs (the file was
  already BOM-less in the working tree but never before touched by the
  series). Single owner: 0011.

- `0012-linux-dispatcher-run.patch` — makes `Dispatcher.Run` /
  `Dispatcher.PushFrame` work on Linux by replacing the Win32
  `GetMessageW`/`TranslateAndDispatchMessage` frame loop with a host-driven
  pump when `!OperatingSystem.IsWindows()`. Three hunks:

  1. `Dispatcher.PushFrameImpl` branches: Windows keeps the exact
     `GetMessage`/`TranslateAndDispatchMessage` loop; Linux calls
     `RunLinuxMessageLoop(frame)`.
  2. `RunLinuxMessageLoop` (WindowsBase) loops while `frame.Continue`,
     each pass: `PromoteTimers(Environment.TickCount)` (DispatcherTimer
     promotion normally comes from `WM_TIMER` on the message-only window,
     which Linux has none of), `DrainLinuxQueue()` (run queued operations;
     returns whether any `Render`-priority+ op ran), then a host-registered
     step (`Dispatcher.RegisterLinuxEventLoop(Func<int,bool>, Action, Action)`)
     that blocks on SDL input and dispatches it. The loop **presents only when
     the drain ran a Render-priority+ operation** (MediaContext schedules its
     render pass at Render priority) — an idle app presents nothing. The wait
     is **not a poll**: `ComputeLinuxWaitTimeout` returns 0 when real work is
     queued, the ms until the next DispatcherTimer is due when one is pending,
     and -1 (unbounded — block until an SDL event or a cross-thread wake)
     when idle. Cross-thread `BeginInvoke` wakes the wait two ways:
     `s_linuxWakeEvent` (a BCL `ManualResetEventSlim` used by the no-host
     fallback) and the host-registered wake signal (an SDL user event pushed
     via `SDL_RegisterEvents`/`SDL_PushEvent`). Single-UI-thread assumption
     (process-wide hooks), documented on the fields. The null-step fallback
     (`LinuxDefaultWait`) blocks on the BCL event too, so even a windowless
     `Dispatcher.Run` idles at ~0% CPU. Nova.SdlSource registers the step at
     type-load (SDL block via `SdlHost.WaitEventTimeout` →
     `CompositionFrame.WaitEventTimeout` → `SdlPresentationSource.PumpStep`,
     which routes events; presenting is the loop's job). `frame.Continue` goes
     false on `Application.Shutdown` (→ `Dispatcher` shutdown →
     `_hasShutdownStarted`), so `Run()` returns and the generated
     `[STAThread] Main` exits — no manual pump needed. Measured on the idle
     sample: ~0% CPU and 0 presents/sec (vs ~58% CPU / ~28 presents/sec with
     the previous 15 ms poll), because the unbounded SDL wait blocks in the OS
     and the present gate fires only on actual render work.
  3. `ShutdownImplInSecurityContext` null-guards the message-only-window
     dispose (`window?.Dispose()`): on Linux `_window` is never created
     (0005), so the previous unconditional `window.Dispose()` NRE'd the
     first time a real `Dispatcher.Run` shut down.

  Also in this patch: `WindowsBase/OtherAssemblyAttrs.cs` adds
  `InternalsVisibleTo("Nova.SdlSource, PublicKey=…")` so Nova.SdlSource can
  call the internal `RegisterLinuxEventLoop` (WindowsBase cannot
  reference Nova.SdlSource — cycle), and
  `PresentationFramework/Application.cs` `EnsureHwndSource` skips the
  parking `HwndWrapper` on Linux (`OperatingSystem.IsWindows()` guard; the
  parking window exists only to receive `WM_ACTIVATEAPP`/
  `WM_QUERYENDSESSION`, which SdlPresentationSource reports from SDL focus
  events). `Dispatcher.cs` is now a **multi-owner file: 0005 + 0012** — its
  0012 section was generated against the intermediate state (pristine +
  0001..0011), not pristine. Proven by `samples/Nova.XamlSample` converted
  to a stock WPF app (`App.xaml` `ApplicationDefinition`, generated
  `[STAThread] Main`, `StartupUri`, timer-driven `Application.Shutdown()`),
  headless exit 0.
- `0013-textedit-dragselect-guard.patch` —
  `PresentationFramework/System/Windows/Documents/TextEditorMouse.cs`
  (`OnMouseMoveWithFocus`): on Linux, skip `_dragDropProcess.
  SourceOnMouseMove` entirely (`!OperatingSystem.IsWindows() || …`) so the
  selection-extension branch always runs. Without the guard, dragging
  across a TextBox aborted the process: `SourceOnMouseMove`'s body calls
  `TextEditorCopyPaste._CreateDataObject`, whose return type `DataObject`
  (PresentationCore/dataobject.cs) implements `IComVisibleDataObject`
  from `System.Private.Windows.Ole` — the OLE interop types and the
  `PInvokeCore` P/Invokes used by `WpfOleServices` live in
  `System.Private.Windows.Core`, which ships only in the Windows Desktop
  runtime. JIT-compiling `SourceOnMouseMove` therefore fails with
  `FileNotFoundException` on Linux for ANY gesture (even plain
  drag-select), because the whole method body is compiled up front.
  Guarding the CALLER (not the method) is required: a guard inside
  `SourceOnMouseMove` would still need the assembly to JIT. Windows path
  byte-identical (the `||` short-circuit never changes behavior when
  `OperatingSystem.IsWindows()` is true). Drag-SELECT works; only actual
  OLE drag-and-drop degrades. Proven:
  `TextBox_DragSelect_SelectsRangeWithoutAbort` (real pushed SDL events
  through Poll; selection length ≥ 3, no abort). Single owner: 0013.
  (At integration the series order is 0001..0011, 0012-linux-dispatcher-run
  [App.xaml worker, main tree], then 0013; the files are disjoint.)

- `0014-linux-pts.patch` — managed PTS (paragraph text services) on Linux.
  Two hunks in `PtsHost/PtsCache.cs` (Stage 0): a failed PTS context
  acquisition (DllNotFound from `PresentationNative_cor3.dll` on Linux)
  used to abort the process at shutdown — the half-initialized context
  pool entry (null `Owner`, zero `PtsHost.Context`) was dereferenced by
  `DestroyPTSContexts` / `OnPtsContextReleased` (exit 134 NRE). The failed
  acquisition now removes the entry, and the shutdown paths skip an entry
  whose `Owner` is null instead of touching the zero handle. Windows
  behavior is byte-identical.

  `PtsHost/Pts.cs`: every `PTS.*` entry point branches on
  `OperatingSystem.IsWindows()` — Windows keeps the `*Impl` DllImport
  (renamed, `EntryPoint` preserved) byte-identical; Linux routes to
  `Nova.Pts.PtsExports` (new WPF-free project, same pattern as 0007's
  `LineServices`/`Nova.LineServices` rewire). The plain path is
  implemented: context lifecycle (`CreateInstalledObjectsInfo`,
  `CreateDocContext`, destroys), bottomless single-column page
  (`FsCreatePageBottomless`/`FsUpdateBottomlessPage`/`FsDestroyPage`),
  subtrack formatting (`FsFormatSubtrackBottomless` walking
  `GetFirstPara`/`GetNextPara` and driving `FormatLine` →
  `TextFormatter` → `Nova.LineServices`), the query readback set
  (`FsQueryPageDetails`, `FsQueryPageSectionList`, `FsQuerySectionDetails`,
  `FsQuerySectionBasicColumnList`, `FsQueryTrackDetails`,
  `FsQueryTrackParaList`, `FsQuerySubtrackDetails`,
  `FsQuerySubtrackParaList`, `FsQueryTextDetails`,
  `FsQueryLineListSingle`) and the flow-direction transforms
  (`FsTransformRectangle`/`FsTransformBbox`). The WPF `PtsHost` callbacks
  cross into `Nova.Pts` through trampolines that field-copy the shared
  layout structs (FSRECT/FSPAP/FSTXTPROPS/FSBBOX/FSFLRES/FSCOLUMNINFO) —
  no `Unsafe.As`. `PresentationFramework.csproj` gains a standalone
  `ProjectReference` to `Nova.Pts` (same pattern as Nova.SdlSource).
  Everything else (finite pages, subpages, tables, floaters/figures,
  footnotes, multi-column, optimal breaking) returns
  `fserrNotImplemented`, which the host's `PTS.Validate` surfaces as a
  nest `PtsException` — a loud honest boundary, never a silent wrong
  answer. Ownership: 0014 owns `PtsHost/Pts.cs` + `PtsCache.cs` (and the
  single Nova.Pts csproj hunk; PresentationFramework.csproj is a known
  multi-owner file — the 0014 section was generated against the
  intermediate state after 0001..0013, not pristine).

- `0015-linux-imaging.patch` — managed imaging on Linux (imaging
  workstream, 2026-08-20). Replaces the 0005 stubbed imaging gates
  (`MILFactory2.CreateFactory`, `WICCodec.CreateImagingFactory`,
  `MilCoreApi.CreateCWICWrapperBitmap`) with real managed routes into
  `Nova.Imaging` (SixLabors.ImageSharp 4.1.1-compat, a `-compat` OSS
  build from the project's own `zznty` feed with the license-key gate
  removed). The patch: (a) `UnsafeNativeMethodsMilCoreApi.cs` — the WIC
  nest classes (`WICBitmapSource`, `WICBitmapDecoder`,
  `WICBitmapFrameDecode`, `WICImagingFactory.CreateBitmapFromSource/
  FromMemory/CreateFormatConverter`, `WICFormatConverter.Initialize`,
  `WICBitmap.SetResolution`, `WICCodec.WICConvertBitmapSource` +
  `CreateImagingFactory`, `MILUnknown` AddRef/Release/QueryInterface,
  `MILFactory2.CreateFactory`) get Linux branches that route tokens into
  `Nova.Imaging.ManagedWicCodec`, Windows `*Core` DllImports preserved
  byte-identical; (b) `BitmapDecoder.cs` —
  `SetupDecoderFromUriOrStream` gains a Linux branch that decodes the
  whole stream via ImageSharp and returns a managed decoder token,
  `clsId` mapped from the ImageSharp container format; (c)
  `PresentationCore.csproj` — `ProjectReference` to the new
  `Nova.Imaging` project. 0015 is a multi-owner addition to
  `UnsafeNativeMethodsMilCoreApi.cs` (0001 + 0005) and
  `PresentationCore.csproj` (0001 + 0004 + 0005 + 0007); it was generated
  against the intermediate state after 0001..0014. 0005's original
  imaging-stub hunks (`PlatformNotSupportedException` gates) are
  superseded by 0015's real implementations; the 0005 stubs remain in
  0005 (composing order 0005 < 0015 means 0015's version wins where they
  touch the same methods — the 0005 stub bodies were preserved, not
  removed, to keep the series composable).
  Proven: `RichTextBox` with a couple of paragraphs lays out and renders
  text (glyph pixels + caret) via the managed PTS path; `Nova.Pts.Tests`
  5/5 including struct-layout pins against the PresentationCore nest.

- `0026-linux-decoderinfo-cache.patch` — WIC codec-info shim for the
  decoder-cache hit path (WPFGallery port, 2026-08-26). WPF caches the
  decoded frame per URI, so a second `ImageSource` over the same image
  reuses the cached `BitmapDecoder`; `BitmapDecoder.CheckCache`'s hit
  path derives the container-format GUID via
  `WICBitmapDecoder.GetDecoderInfo` + `WICBitmapCodecInfo.GetContainerFormat`
  / `GetMimeTypes` — three raw `WindowsCodecs` DllImports with no Linux
  trap, which threw `DllNotFoundException` (and the `ImageSourceConverter`
  swallowed it into a null `Image.Source` — a blank image, not a crash).
  The patch gives the three methods Linux branches: `GetDecoderInfo`
  creates a `ManagedWicDecoderInfo` token (container format + MIME list,
  `Nova.Imaging`), `GetContainerFormat` maps it to the
  `MILGuidData.GUID_ContainerFormat*` GUIDs, `GetMimeTypes` answers the
  WIC two-call length convention. Multi-owner addition to
  `UnsafeNativeMethodsMilCoreApi.cs` (0001 + 0005 + 0015), generated
  against the intermediate state after 0001..0025. Companion host change
  (no patch): `Nova.Imaging` `ManagedWicCodec.GetDecoderInfo` +
  `ManagedWicDecoderInfo`.

- `0027-linux-playsound-noop.patch` — `Application.PlaySound` Linux guard
  (WPFGallery port, 2026-08-26). Navigation calls
  `PlaySound` → `GetSystemSound` reads the Win32 `AppEvents` registry
  scheme; on Linux `Registry.CurrentUser` is null and every navigation
  click crashed the app with an NRE. The trap returns without playing
  (no sound host on Linux); the Windows path (registry lookup +
  `UnsafeNativeMethods.PlaySound`) stays byte-identical. Multi-owner
  addition to `PresentationFramework/Application.cs` (0012), generated
  against the intermediate state after 0001..0026.

- `0028-linux-cursor-load.patch` — `Cursor` custom/stream load Linux guards
  (WPFGallery port, 2026-08-26). Opening a page with a `GridView`
  (GridViewColumnHeader's splitter cursor) crashed the app:
  `Cursor.LoadFromStream` → `UnsafeNativeMethods.LoadImageCursor` →
  `user32.dll` DllNotFound. The three load paths (`LoadFromFile`,
  `LegacyLoadFromStream`, `LoadFromStream`) early-return on
  `!OperatingSystem.IsWindows()` with an invalid-but-non-null placeholder
  handle (the SDL host's `SetCursor` is a managed no-op that never
  consumes the HCURSOR), and `CursorHandle.ReleaseHandle` returns true on
  Linux so disposing the placeholder never reaches `DestroyCursor`.
  Multi-owner addition to `PresentationCore/Input/Cursor.cs` (0005) and
  `Shared/MS/Win32/NativeMethodsOther.cs` (0005), generated against the
  intermediate state after 0001..0027.

- `0029-linux-getstringtypeex-classify.patch` — `SafeNativeMethodsOther.GetStringTypeEx`
  Linux classification (WPFGallery port, 2026-08-26). A TextBox double-click
  crashed the app: the word selection drives `SelectionWordBreaker` →
  kernel32 `GetStringTypeEx` DllNotFound. The Linux branch delegates to
  `Nova.Classification.CharTypeClassifier` (ICU-backed, `libicuuc` — the same
  database the in-tree classification tables are generated from; degrades to
  BCL Unicode tables when ICU is missing). Adds the
  `Nova.Classification` ProjectReference to WindowsBase (the shared
  `SafeNativeMethodsOther.cs` compiles there; PresentationFramework calls
  the WindowsBase method). Multi-owner addition to
  `Shared/MS/Win32/SafeNativeMethodsOther.cs` (0005) and
  `WindowsBase.csproj` (0003),
  generated against the intermediate state after 0001..0028. Companion host
  change (no patch): `src/Nova.Classification/CharTypeClassifier.cs`.

- `0031-linux-font-source-loading.patch` — FontFamily source resolution
  (WPFGallery port, 2026-08-26). Fonts whose source is a file or a
  stream-backed URI (pack://application) resolved to tofu: the port's
  `Factory.GetFontCollection` only handled local directories and fell back
  to the system collection for everything else. Now a local FILE opens the
  face directly and any stream-backed source resolves through
  `WpfWebRequestHelper` into an in-memory FreeType face (`FontCollection.
  CreateFromFile`/`CreateFromMemory` — the Windows equivalent of handing
  DWrite the FontSource stream). Companion host changes (no patch):
  `Nova.FontConfig` registers the bundled `fonts/NovaFluentIcons.ttf` with
  fontconfig (the Windows system font registry equivalent) and maps the
  Segoe Fluent Icons / Segoe MDL2 Assets family names to it, so the Fluent
  theme's bare names resolve automatically. Multi-owner addition to
  `PresentationCore/MS/internal/Text/TextInterface/Factory.cs` and
  `NovaFontTypes.cs` (0004 + 0007), generated against the intermediate
  state after 0001..0030.

- `0030-linux-journal-binaryformat.patch` — navigation journal state
  serialization Linux wiring (WPFGallery port, 2026-08-26). Navigating
  crashed the app with FileNotFoundException for `System.Private.Windows.Core`:
  the journal's `DataStreams.Save`/`Load` serialized DP values with the
  WindowsDesktop-only NRBF writer (`BinaryFormatWriter`). The save side now
  branches: Windows keeps the NRBF writer + BinaryFormatter fallback
  byte-identical; Linux writes `Nova.Serialization.JournalValueSerializer`
  (a type-tagged JSON format over Utf8JsonReader/Writer — the reflection
  serializer round-trips the value with its runtime type, so the restored
  value matches the DP type). The load side reads the Nova JSON first and
  falls back to the `System.Formats.Nrbf` reader (a separate package) for
  journal entries created on Windows; the BinaryFormatter fallback is now
  Windows-only. Adds the `Nova.Serialization` ProjectReference to
  PresentationFramework. Multi-owner addition to
  `PresentationFramework/MS/Internal/DataStreams.cs` (newly patched) and
  `PresentationFramework.csproj` (0005), generated against the intermediate
  state after 0001..0029. Companion host change (no patch):
  `src/Nova.Serialization/` (serializer + tests).

- `0032-linux-messagebox-default.patch` — `MessageBox.ShowCore` Linux guard
  (WPFGallery port, 2026-08-26). The icons page's copy button calls
  `MessageBox.Show` → user32 `MessageBox` DllNotFound. With no native
  dialog host on the SDL backend the call answers with the default result
  (as if the default button were clicked); the Windows path stays
  byte-identical. New patched file: `PresentationFramework/MessageBox.cs`,
  generated against the intermediate state after 0001..0031.

- `0038-linux-jumplist-noop.patch` — `JumpList` Linux no-op (WPFGallery
  port, 2026-08-27). `JumpList.AddToRecentCategory` (shell32
  `SHAddToRecentDocs`) and `JumpList.Apply` (the shell-link machinery +
  `SHCreateItemFromParsingName`) crashed on Linux; there is no JumpList/taskbar
  surface on the SDL runtime, so both now validate then drop the request on
  Linux. New patched file:
  `PresentationFramework/System/Windows/Shell/JumpList.cs`, generated against
  the intermediate state after 0001..0037.

- `0039-linux-cookies-empty.patch` — cookie store Linux guard (WPFGallery
  port, 2026-08-27). `Application.GetCookie/SetCookie` (wininet
  `InternetGetCookieEx`/`InternetSetCookieEx` via
  `PresentationCore/MS/internal/AppModel/CookieHandler.cs`) crashed on Linux;
  wininet has no Linux host and the SDL runtime has no cookie store, so
  `GetCookie` returns null and `SetCookie` returns false on Linux (the
  web-request path already swallowed the DllNotFound and proceeded cookieless).
  New patched file: `PresentationCore/MS/internal/AppModel/CookieHandler.cs`,
  generated against the intermediate state after 0001..0038.

- `0050-linux-managed-dragloop.patch` — managed intra-app drag loop (2026-08-28).
  DragDrop.DoDragDrop on the Linux host no longer needs OLE: the Linux branch
  validates like Windows, raises DragDropStarted, wraps the payload in a
  managed OLE-free IDataObject (string/string[] formats; the OLE DataObject
  JIT-resolves System.Private.Windows.Core), and delegates to
  <c>DragDrop.LinuxDragLoop</c> — a hook registered by Nova.SdlSource at type
  load. The loop pushes a nested DispatcherFrame and consumes mid-drag SDL
  mouse moves (Windows parity: OLE eats them), raising
  QueryContinueDrag (cancel supported) + DragEnter/DragOver/DragLeave/Drop at
  the hit-tested target and negotiating e.Effects; the left-button release
  completes the frame and is still reported so MouseDevice state stays honest;
  Escape cancels. New patched files: PresentationCore System/Windows/DragDrop.cs
  (multi-owner, 0005) and MS/Internal/DragDropInterop.cs (multi-owner, 0047),
  generated against the intermediate state after 0001..0049. End-to-end tests
  drive a real drag through the SDL pump (drop + effects negotiation, cancel).

- `0049-linux-clipboard-image-filedrop.patch` — clipboard images and file lists
  (2026-08-28). SetImage/GetImage travel as PNG through the SDL clipboard's
  image/png mime (Nova.Imaging.PngCodec does the BGRA32 encode/decode),
  SetFileDropList/GetFileDropList travel as file:// URIs through the XDG
  text/uri-list mime, and ContainsImage/ContainsFileDropList probe
  SdlHost.HasClipboardData instead of the absent OLE DataObject. Same file as
  0034 (multi-owner), generated against the intermediate state after 0001..0048.

- `0048-linux-texteditor-paste-query.patch` — text-editor clipboard commands
  (2026-08-28). Three OLE traps in TextEditorCopyPaste crashed the context
  menu: the Paste CanExecute probe NRE'd on the null GetDataObject, and the
  Cut/Copy bodies JIT-resolved the OLE DataObject even in the Linux branch.
  The query answers from the SDL text clipboard; the Windows bodies moved to
  private *Core methods (the JIT never touches OLE on Linux); the Linux paths
  copy/cut the selection into the SDL text clipboard and paste replaces the
  selection with it. Same file as 0013 (multi-owner), generated against the
  intermediate state after 0001..0047.

- `0047-linux-dragdrop-raise.patch` — synthetic file-drop raising (2026-08-28).
  The compositor delivers SDL drop events (no OLE drag-drop pipeline), so the
  host raises the WPF DragEnter/DragOver/Drop routed events (tunneling +
  bubbling) at the hit-tested target with a managed, OLE-free
  <c>IDataObject</c> carrying FileDrop — the OLE DataObject's construction
  JIT-resolves System.Private.Windows.Core (the 0034 trap), so the synthetic
  drop never touches it. The SdlSource collects the batch (DropBegin/DropFile)
  and raises at DropComplete via the internal MS.Internal.DragDropInterop
  (PresentationCore). New patched files:
  `PresentationCore/MS/Internal/DragDropInterop.cs` plus a compile-item line in
  `PresentationCore.csproj` (multi-owner), generated against the intermediate
  state after 0001..0046. End-to-end test: pushed SDL drop events raise the
  Drop handler with the file list (Framework 103/103 x2).

- `0046-linux-printdialog-pnse.patch` — PrintDialog Linux guard
  (2026-08-28). `PrintDialog.ShowDialog` reached comdlg32/winspool DllNotFound on
  the Nova/SDL runtime (no print host); the Linux branch throws
  `PlatformNotSupportedException` before the Win32 dialog path (the Rights
  Management pattern, patch 0040). New patched file:
  `PresentationFramework/System/Windows/Controls/PrintDialog.cs`, generated
  against the intermediate state after 0001..0045.

- `0045-linux-mime-urlmon-default.patch` — urlmon MIME fallback on Linux
  (2026-08-28). `MimeTypeMapper.GetMimeTypeFromUrlMon` (urlmon
  `FindMimeFromData`, a COM call) fired for any extension outside 0024's image
  table — a pack/content resource with e.g. a `.css`/`.wav` extension crashed
  with urlmon DllNotFound. The Linux branch returns `application/octet-stream`,
  the same default the Win32 path gives for an unregistered extension. New
  hunks in the 0024-owned `Shared/MS/Internal/MimeTypeMapper.cs`, generated
  against the intermediate state after 0001..0044.

- `0044-linux-milutilities-managed.patch` — milcore utility P/Invokes managed on
  Linux (2026-08-28). `MILUtilities.MILCopyPixelBuffer` (WriteableBitmap
  WritePixels/CopyPixels) and `MILUtilities.ProjectBounds` →
  `MIL3DCalcProjected2DBounds` (Viewport3D bounds) were the last live milcore
  imports reachable from the managed surface: a WriteableBitmap copy crashed with
  milcore DllNotFound, and any Viewport3D content with the projected-bounds call.
  The implementations live in the Nova tree per contract: `Nova.Imaging.
  PixelBufferUtility.CopyPixels` (Span-based row copy — byte-aligned memcpy path,
  sub-byte bit loop, validated geometry) and `Nova.Geometry.ProjectionBounds.
  Compute` (pure-double corner projection, row-vector convention, w==0 corners
  skipped, degenerate → Rect.Empty); the patched file keeps the thin
  `!OperatingSystem.IsWindows()` branches + the Windows DllImports byte-identical
  (MILCopyPixelBuffer demoted to a private `*Core`). Tests: Nova.Imaging.Tests
  (new project) 5/5, Nova.Geometry.Tests 14/14. New patched file:
  `PresentationCore/System/Windows/Media/MILUtilities.cs`, generated against the
  intermediate state after 0001..0043.

- `0043-linux-uia-provider-noop.patch` — UIA provider event API Linux no-op
  (WPFGallery port, 2026-08-28). Clicking "Add user" on the dashboard crashed
  with `DllNotFoundException: UIAutomationCore.dll`:
  `AutomationPeer.RaisePropertyChangedEvent` → `UiaCoreProviderApi.
  UiaClientsAreListening` P/Invokes the native UIA client. There is no native
  UIAutomationCore (and no UIA client) on the Nova/SDL runtime, so every
  `UiaCoreProviderApi` entry point branches on `!OperatingSystem.IsWindows()`:
  `UiaClientsAreListening` returns false (the peers skip raising), the
  `UiaRaise*` event methods no-op, `UiaReturnRawElementProvider` returns
  `IntPtr.Zero` and `UiaHostProviderFromHwnd` returns null — the automation
  peers never observe a listening client, so none of the raw DllImports fire.
  The raise methods must not be skipped either: `AutomationInteropProvider`
  does NOT gate on `ClientsAreListening`, so a bare `UiaClientsAreListening`
  fix would leave the raise path as a live DllNotFound. New patched file:
  `UIAutomation/UIAutomationProvider/MS/Internal/Automation/UiaCoreProviderApi.cs`,
  generated against the intermediate state after 0001..0042.

- `0042-linux-progressive-image-download.patch` — progressive remote
  image decoding (WPFGallery port, 2026-08-27). Replaces 0041's spool-file
  handoff with a fully async pipeline: HttpClient streams the body
  (GetStreamAsync — headers only, no response buffering) straight into the
  ImageSharp progressive decoder — one allocator-backed chunk buffer, no temp
  file, decode attempts at most once per 150ms, each yielding a more complete
  BGRA frame. The download AND the decode run on thread-pool threads
  (ConfigureAwait(false) throughout — never on the dispatcher's sync context);
  each frame hops to the owning decoders' dispatcher only to apply the pixels
  (a single awaited Dispatcher.InvokeAsync). The first frame builds the real
  decoder from a Nova.Imaging progressive token (CreateFromManagedToken) and
  swaps the token's pixels; later frames update the token and re-materialize
  the frames (BitmapFrameDecode.UpdateDecoder → DUCE re-send → GPU texture
  cache invalidation). Progress reports flow during the download (Win32
  contract: 100% fires before the download-completed event, which
  BitmapImage unsubscribes on); failures marshal as before. Companion host
  changes (no patch): Nova.Imaging gains ProgressiveDecoderSession
  (header-probe format detection, a PrefixedStream, tolerant IgnoreData
  decodes via the 4.2.0-compat progressive DecodeAsync), ManagedWicDecoder
  progressive mode (live frame, UpdateFrame swap with ownership transfer),
  ManagedWicBitmap.UpdateFrom and ManagedWicCodec.CreateProgressiveDecoderToken/
  UpdateProgressiveDecoderFrame (the frame detaches its backing image into the
  decoder — no double-dispose). Multi-owner additions: BitmapDownload.cs
  (0041 + 0042) and the newly patched LateBoundBitmapDecoder.cs /
  BitmapDecoder.cs, generated against the intermediate state after 0001..0041.

- `0041-linux-bitmapdownload-httpclient.patch` — remote image download
  HttpClient path (WPFGallery port, 2026-08-27). `new BitmapImage("http://…")`
  crashed with wininet/kernel32 DllNotFound: `BitmapDownload.BeginDownload`
  eagerly created the cache file (InternetCacheFolder + GetTempFileName +
  CreateFile). The Linux branch replaces both: a managed temp file
  (FileOptions.DeleteOnClose, the FILE_FLAG_DELETE_ON_CLOSE equivalent) and the
  fetch itself via a shared HttpClient whose GetStreamAsync streams the body
  (no response buffering) into that file for the existing worker queue.
  Redirects are followed — the Win32 path's HttpWebRequest defaults to
  AllowAutoRedirect=true and is never overridden in WpfWebRequestHelper, and
  SocketsHttpHandler defaults match (50 hops). AutomaticDecompression stays off
  like HttpWebRequest; the WinInet uriCachePolicy is ignored (no Linux
  counterpart). New patched file:
  `PresentationCore/System/Windows/Media/Imaging/BitmapDownload.cs`, generated
  against the intermediate state after 0001..0040.

- `0040-linux-rm-pnse.patch` — Rights Management `PlatformNotSupportedException`
  (WPFGallery port, 2026-08-27). `EncryptedPackage.Create/CreateFromPackage/Open/
  IsEncryptedPackageEnvelope` crashed with ole32/msdrm DllNotFound (compound-file
  storage + DRM client); Rights Management has no Linux story, so every
  `EncryptedPackageEnvelope` constructor and both `IsEncryptedPackageEnvelope`
  overloads now throw `PlatformNotSupportedException` on Linux before any native
  work (a catchable managed exception per the repo convention). New patched
  file: `WindowsBase/System/IO/Packaging/EncryptedPackage.cs`, generated against
  the intermediate state after 0001..0039.

- `0037-linux-window-icon-sdl.patch` — `Window.Icon` SDL-backed window icon
  (WPFGallery port, 2026-08-27). Setting `Window.Icon` (or `ShowInTaskbar`) on a
  shown window crashed with shell32/kernel32 DllNotFound (ExtractIconEx/
  GetModuleFileName via IconHelper). `Window.UpdateIcon` now branches to an
  SDL path before any Win32 work: the ImageSource decodes to BGRA rows
  (BitmapSource direct, or a DrawingVisual render for non-bitmap sources) and
  goes through `SdlPresentationSource.SetWindowIcon` →
  `SdlWindow.SetWindowIcon` (SDL_CreateSurfaceFrom + SDL_SetWindowIcon with
  SDL_PIXELFORMAT_ARGB8888 = WPF Bgra32 byte order). Compositors may ignore the
  request — Wayland has no universal window-icon surface — but SDL forwards it
  where supported; a null icon clears it. Companion host changes (no patch):
  `src/Nova.Sdl/SdlWindow.cs` (SetWindowIcon) and
  `src/Nova.SdlSource/SdlPresentationSource.cs` (the passthrough). Reuses the
  multi-owner file `PresentationFramework/System/Windows/Window.cs`
  (0002 + 0008 + 0018 + 0023 + 0037), generated against the intermediate state
  after 0001..0036.

- `0036-linux-sdl-messagebox.patch` — `MessageBox` SDL-backed Linux dialog
  (WPFGallery port, 2026-08-27). Replaces 0032's blanket `defaultResult` answer:
  `MessageBox.ShowCore` on Linux now maps the Win32 style bits (button set, icon,
  MB_DEFBUTTONn, the Cancel-escape convention) to the SDL3 `SDL_ShowMessageBox`
  API — the zenity backend on Linux (the XDG Desktop Portal has no message-box
  interface, which is why SDL falls back to a subprocess there) — and maps the
  returned button id back through the same `Win32ToMessageBoxResult` mapping the
  Windows path uses (the button ids are the ID* values MessageBoxResult mirrors).
  Synchronous, like the Win32 box it replaces. Falls back to `defaultResult`
  when no dialog host exists (no zenity) or for
  ServiceNotification/DefaultDesktopOnly boxes. Companion host changes (no
  patch): `src/Nova.Sdl/MessageBox.cs` (the SDL_ShowMessageBox wrapper — pinned
  UTF-8 title/message/label buffers for the synchronous call, RETURNKEY/
  ESCAPEKEY button flags) and `src/Nova.SdlSource/MessageBox.cs` (the
  MessageBoxIconKind/MessageBoxButtonDefinition wrappers plus
  SdlPresentationSource.ShowMessageBox/FromHandleOrActive). Reuses the 0032
  patched file `PresentationFramework/System/Windows/MessageBox.cs`, generated
  against the intermediate state after 0001..0035.

- `0035-linux-sdl-file-dialogs.patch` — `CommonDialog` SDL file dialogs
  (WPFGallery port, 2026-08-27). The file/folder dialogs page crashed the app
  with FileNotFound for kernel32.dll: `CommonDialog.ShowDialog` builds a hidden
  HwndWrapper owner (HwndSubclass..cctor → GetModuleHandle → kernel32) and
  `CommonItemDialog.RunDialog` creates the IFileDialog COM objects — neither has
  a Linux host. `CommonDialog.ShowDialog` now branches to a Linux runner BEFORE
  any HWND/COM work: OpenFileDialog/SaveFileDialog/OpenFolderDialog map to the
  SDL3 dialog API (SDL_ShowOpenFileDialog / SDL_ShowSaveFileDialog /
  SDL_ShowOpenFolderDialog — the XDG Desktop Portal on Linux), pump a nested
  dispatcher frame until the async dialog completes, then apply the managed
  post-processing: MutableItemNames, FilterIndex, the FileOk cancellation hook,
  CheckFileExists filtering (Open) and AddExtension/DefaultExt (Save); the WPF
  Filter string is parsed to SDL filters ("*.ext1;*.ext2" → "ext1;ext2"). Other
  CommonDialogs (PrintDialog) return null (canceled). Companion host changes (no
  patch): `src/Nova.Sdl/FileDialog.cs` (the dialog helper — pinned UTF-8 filter/
  location buffers, the rooted UnmanagedCallersOnly callback, the
  FileDialogSession result channel) and `src/Nova.SdlSource/FileDialog.cs` (the
  FileDialogKind/FileDialogFilter/FileDialogSession wrappers plus
  SdlPresentationSource.ShowFileDialog/FromActiveWindow). New patched files:
  `PresentationFramework/Microsoft/Win32/{CommonDialog,CommonItemDialog,
  FileDialog,OpenFolderDialog}.cs`, generated against the intermediate state
  after 0001..0034.

- `0034-linux-clipboard-sdl.patch` — `Clipboard` SDL-backed Linux branch
  (WPFGallery port, 2026-08-27). The clipboard page's copy button crashed the
  app with FileNotFoundException for `System.Private.Windows.Core`: BOTH the
  Win32 OLE clipboard (ClipboardCore) and the OLE `DataObject` itself
  (DataObject.SetData → the OLE data store) have no Linux host, so every
  Clipboard entry point branches BEFORE constructing a DataObject. All
  ClipboardCore calls live in their own `*Core` methods (ClearCore, FlushCore,
  SetFileDropListCore, GetDataObjectCore, IsCurrentCore, SetDataObjectCore) —
  the JIT resolves method-body tokens eagerly, so a branch in the same method
  still crashes. Linux is text-only via
  `Nova.Sdl.SdlHost.SetClipboardText/GetClipboardText` (SDL_SetClipboardText /
  SDL_GetClipboardText, UTF-8); the image/file-drop/audio paths degrade to
  contains=false / get=null / set=no-op; GetDataObject returns null. Companion
  host change (no patch): the clipboard methods on `src/Nova.Sdl/SdlHost.cs`.
  New patched file: `PresentationCore/System/Windows/clipboard.cs` (previously
  unpatched), plus a `Nova.Sdl` ProjectReference in `PresentationCore.csproj`
  (0001 + 0004 + 0005 + 0007 + 0034), generated against the intermediate state
  after 0001..0033.

- `0033-linux-windowchrome-sdl-hittest.patch` — `WindowChromeWorker`
  SDL hit-test routing (WPFGallery port, 2026-08-27). The
  `WindowChromeWorker._ApplyNewCustomChrome` Linux branch routes the chrome
  geometry (CaptionHeight / ResizeBorderThickness / ResizeGripDirection /
  IsHitTestVisibleInChrome) into `SdlPresentationSource.ConfigureChrome`,
  which makes the window borderless and installs the SDL hit-test callback
  (`SDL_SetWindowHitTest`) — the compositor then performs the caption drag
  and the 8 resize edges natively, replacing the Win32 WM_NCHITTEST
  machinery. Companion host changes (no patch): `src/Nova.Sdl/` (the
  HitTestRegion enum, SdlWindow.SetBordered/SetHitTest with the rooted
  unmanaged callback + GCHandle self-token) and `src/Nova.SdlSource/`
  (the ChromeHitTestRegion enum + SdlPresentationSource.ConfigureChrome).
  New patched file: `PresentationFramework/System/Windows/Shell/
  WindowChromeWorker.cs`, generated against the intermediate state after
  0001..0032.

-linux-journal-binaryformat.patch` — navigation journal state
  serialization Linux guards (WPFGallery port, 2026-08-26). Navigating
  (the journal's `DataStreams.Save`/`Load`) crashed the app with
  FileNotFoundException for `System.Private.Windows.Core` — the
  `BinaryFormatWriter.TryWriteFrameworkObject` / `NrbfDecoder` NRBF
  serialization lives in the WindowsDesktop-only private assembly. Both the
  save and the load sides now branch on `OperatingSystem.IsWindows()` and
  fall back to the BinaryFormatter (the same fallback the code already uses
  for unsupported types; the BinaryFormat types are never JIT-loaded on
  Linux). New patched file: `PresentationFramework/MS/Internal/DataStreams.cs`
  (previously unpatched), generated against the intermediate state after
  0001..0029.

- `0016-desktop-theme-ivt.patch` — single-owner hunk (one line) in
  `PresentationFramework/OtherAssemblyAttrs.cs`: adds
  `InternalsVisibleTo("Nova.DesktopTheme.Host, PublicKey=…")`. The
  `Nova.DesktopTheme.Host` bridge assembly (WPF-aware, referenced only by
  the consuming app — never by the Framework build chain, so no cycle)
  calls the internal `SystemColors.InvalidateCache()` /
  `SystemParameters.InvalidateCache()` /
  `InvalidateDerivedThemeRelatedProperties()` and walks live trees via
  `TreeWalkHelper.InvalidateOnResourcesChange` to restyle a RUNNING app
  when the desktop palette changes (`NOVA_PALETTE=desktop` + file-watch /
  portal `SettingChanged`). Multi-owner files unchanged; the file is
  pristine upstream otherwise (same shape as 0001/0012's IVT hunks in
  PresentationCore/WindowsBase OtherAssemblyAttrs).

- `0017-linux-multitarget-net10.patch` — multi-targets the WPF product
  build for the Linux host (`Directory.Build.props` only; single-owner).
  `TargetFramework=net11.0` → `TargetFrameworks=net10.0;net11.0`, with
  per-TFM `TargetFrameworkVersion`. The net10.0 leg adds a per-TFM
  PropertyGroup (evaluated after the Arcade import so it overrides
  `eng/Versions.props`): `UseOOBNETCoreAppTargetingPack/RuntimePack/
  AppHostPack=false` — without it, `RuntimeFrameworkReference.targets`
  forces the OOB `MicrosoftNETCoreAppRefVersion` (11.0.0-rc.1) targeting
  pack onto ANY TFM, so net10-built assemblies reference
  `System.Runtime 11.0.0.0` and CS1705 any net10 consumer — and
  `MicrosoftPrivateWinformsVersion=10.0.11-servicing.26373.116` (the
  pinned 11.0.0-rc.1 `Microsoft.Private.Winforms` transport ships only
  `lib/net11.0` and its assemblies reference System.Runtime 11.0.0.0;
  the net10 transport's `lib/net10.0` carries the same
  compile-time-only OLE/interop references, which are never loaded on
  Linux — patch 0013's `OperatingSystem.IsWindows()` gates — and are not
  shipped in the bundle). `TargetFrameworkVersion` is now per-TFM
  conditional (was unconditional 11.0) so the SDK/Arcade version
  comparisons see the correct value per leg. Measured: the whole managed
  impl set (PresentationCore/PresentationFramework/WindowsBase/System.Xaml/
  UIAutomation*/ReachFramework/System.Printing/System.Windows.Primitives/
  Manipulations + themes + PresentationUI) compiles clean for net10.0 with
  ZERO C# errors — no net11-only API usage surfaced; the only blockers
  were the ref-pack and winforms-transport versions above. This file was
  previously owned by 0003 (Linux managed-only lines), so 0017's hunks
  were generated against the intermediate state after 0001..0016.

- Test suites that create many Vulkan devices per run disable validation by
  default (Khronos validation layer `GetDispatchDevice` aborts the process
  via a libstdc++ `__glibcxx_assert_fail` under rapid device create/destroy
  churn — reproduced with both the Intel Xe and lavapipe ICDs, with xunit
  parallelism on and off, absent entirely when the layer is unloadable).
  Set `NOVA_TEST_VULKAN_VALIDATION=1` to re-enable validation for a
  deliberate run (`tests/NovaTestVulkan.cs`, shared helper).

- The WINDOW path had the same hardcoding until it was fixed in
  `src/Nova.SdlSource/SdlPresentationSource.cs`: `CreateWindowFrame` always
  enabled validation (`ValidationMode.Enabled`), so every `Window.Show` —
  every Framework test window and every production app window — ran with
  the layer on, paying the per-frame CPU cost and the device-churn abort
  risk. It is now gated by the product switch `NOVA_VULKAN_VALIDATION=1`
  (default off), documented at `WindowValidationMode()`. The popup path
  shares the main window's Vulkan device and inherits the same decision.
  The switch is deliberately distinct from the test-only
  `NOVA_TEST_VULKAN_VALIDATION`: a deployed app can enable validation on
  its window path without implying test machinery.

- Remaining Framework `HwndSource` casts (ImmComposition, automation)
  are not in the Window construction path; leave them until those features
  are in scope. `HwndSourceParameters` stays as the ctor bag.

`Nova.SdlSource` PublicSigns with `src/Nova.SdlSource/Wcp.PublicKey.snk`
(Arcade `35MSSharedLib1024.snk` / `BuildInfo.WCP_PUBLIC_KEY_STRING`).

Verified: `dotnet restore` PresentationCore succeeds; WindowsBase +
PresentationCore-ref compile. PresentationCore impl still fails on
DirectWriteForwarder types (`MS.Internal.Text.TextInterface.{Font,FontFace,
FontCollection,GlyphMetrics,…}`) — next slice is DWrite → `Nova.Text`.
