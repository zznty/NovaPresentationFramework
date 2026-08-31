#!/usr/bin/env bash
# bootstrap-worktree.sh — take a fresh worktree from nothing to a state where
# the full build, the 19 test suites, and the ControlProbeHarness all work.
#
#   scripts/bootstrap-worktree.sh            # bootstrap + build + verify-series
#   scripts/bootstrap-worktree.sh --no-build # bootstrap (submodule+cone+aliases+patches) only
#   scripts/bootstrap-worktree.sh --tests    # also run the 19 suites sequentially
#   scripts/bootstrap-worktree.sh --harness  # also run `harness all` + `feat all`
#   scripts/bootstrap-worktree.sh --clean    # wipe WPF artifacts/obj+bin first (stale-incremental
#                                            # recovery: "my rebuild did nothing" — see README)
#
# Idempotent: safe to re-run. Fail-loudly: exits non-zero with an actionable
# message on the first problem, never leaving a half-built tree that appears
# to work.
#
# The submodule is the dangerous bit: its dirty working tree IS the applied
# patch series. We NEVER run `git submodule update`/`checkout`/`reset`/
# `clean`/`stash` against an already-initialized submodule — only a fresh
# (empty/missing) submodule dir is initialized. Everything else is verified
# and repaired in place.
set -euo pipefail

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SUBMODULE="$REPO_ROOT/third_party/dotnet-wpf"
PATCH_DIR="$REPO_ROOT/patches"
PIN="$(grep -oE '[0-9a-f]{40}' "$PATCH_DIR/UPSTREAM.txt" | head -1)"

SERIES=(
    0001-duce-nests-forwarder-ivt
    0002-window-sdlsource
    0003-linux-managed-restore
    0004-forwarder-stubs
    0005-linux-host-traps
    0006-linux-classic-theme
    0007-linux-line-services
    0008-window-hwnd-off-show
    0009-popup-sdlsource
    0010-extended-assembly-info-incremental
    0011-xaml-access-level-linux-guard
    0012-linux-dispatcher-run
    0013-textedit-dragselect-guard
    0014-linux-pts
    0015-linux-imaging
    0016-desktop-theme-ivt
    0017-linux-multitarget-net10
    0018-window-showdialog-linux-guard
    0019-nlgspeller-linux-guard
    0020-readerwriterlock-linux-wait
    0021-safenativemethods-tickcount-linux
    0022-popup-clientscreen-linux-origin
    0023-thememode-linux-light
    0024-mimetypemapper-image-extensions
    0025-cursorpos-linux-trap
    0026-linux-decoderinfo-cache
    0027-linux-playsound-noop
    0028-linux-cursor-load
    0029-linux-getstringtypeex-classify
    0030-linux-journal-binaryformat
    0031-linux-font-source-loading
    0032-linux-messagebox-default
    0033-linux-windowchrome-sdl-hittest
    0034-linux-clipboard-sdl
    0035-linux-sdl-file-dialogs
    0036-linux-sdl-messagebox
    0037-linux-window-icon-sdl
    0038-linux-jumplist-noop
    0039-linux-cookies-empty
    0040-linux-rm-pnse
    0041-linux-bitmapdownload-httpclient
    0042-linux-progressive-image-download
    0043-linux-uia-provider-noop
    0044-linux-milutilities-managed
    0045-linux-mime-urlmon-default
    0046-linux-printdialog-pnse
    0047-linux-dragdrop-raise
    0048-linux-texteditor-paste-query
    0049-linux-clipboard-image-filedrop
    0050-linux-managed-dragloop
    0051-pts-paravisual-offset
    0052-wpf-nuget-config-feeds
    0053-unregistered-live-fixes





)

# The full 15-directory sparse cone (patches/README.md "Fresh worktree setup").
CONE=(
    eng/WpfArcadeSdk
    src/Microsoft.DotNet.Wpf/cycle-breakers
    src/Microsoft.DotNet.Wpf/src/Common
    src/Microsoft.DotNet.Wpf/src/PresentationCore
    src/Microsoft.DotNet.Wpf/src/PresentationFramework
    src/Microsoft.DotNet.Wpf/src/PresentationUI
    src/Microsoft.DotNet.Wpf/src/ReachFramework
    src/Microsoft.DotNet.Wpf/src/Shared
    src/Microsoft.DotNet.Wpf/src/System.Printing
    src/Microsoft.DotNet.Wpf/src/System.Windows.Input.Manipulations
    src/Microsoft.DotNet.Wpf/src/System.Windows.Primitives
    src/Microsoft.DotNet.Wpf/src/System.Xaml
    src/Microsoft.DotNet.Wpf/src/System.Windows.Controls.Ribbon
    src/Microsoft.DotNet.Wpf/src/Themes
    src/Microsoft.DotNet.Wpf/src/UIAutomation
    src/Microsoft.DotNet.Wpf/src/WindowsBase
)

# Case-alias symlinks: Linux is case-sensitive; the csprojs reference
# differently-cased paths. `target` is the real on-disk dir, `link` the
# alias path. Idempotent (ln -sfn re-links).
ALIASES=(
    "internal|src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/Internal"
    "MS|src/Microsoft.DotNet.Wpf/src/Shared/ms"
    "Internal|src/Microsoft.DotNet.Wpf/src/Shared/MS/internal"
    "Generated|src/Microsoft.DotNet.Wpf/src/Shared/MS/Internal/generated"
    "InterOp|src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Interop"
    "documents|src/Microsoft.DotNet.Wpf/src/PresentationFramework/MS/Internal/Documents"
    "Windows|src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/windows"
    "manager|src/Microsoft.DotNet.Wpf/src/ReachFramework/Serialization/Manager"
    "Packaging|src/Microsoft.DotNet.Wpf/src/ReachFramework/packaging"
)

# 19 test suites, run SEQUENTIALLY (parallel Vulkan aborts Nova.Text on Xe).
TESTS=(
    tests/Nova.Framework.Tests/Nova.Framework.Tests.csproj
    tests/Nova.Sdl.Tests/Nova.Sdl.Tests.csproj
    tests/Nova.Vulkan.Tests/Nova.Vulkan.Tests.csproj
    tests/Nova.SdlSource.Tests/Nova.SdlSource.Tests.csproj
    tests/Nova.Mil.Tests/Nova.Mil.Tests.csproj
    tests/Nova.Pts.Tests/Nova.Pts.Tests.csproj
    tests/Nova.Geometry2D.Tests/Nova.Geometry2D.Tests.csproj
    tests/Nova.LineServices.Tests/Nova.LineServices.Tests.csproj
    tests/Nova.MilCmd.Tests/Nova.MilCmd.Tests.csproj
    tests/Nova.Host.Tests/Nova.Host.Tests.csproj
    tests/Nova.Geometry.Tests/Nova.Geometry.Tests.csproj
    tests/Nova.Text.Tests/Nova.Text.Tests.csproj
    tests/Nova.Classification.Tests/Nova.Classification.Tests.csproj
    tests/Nova.SystemTheme.Tests/Nova.SystemTheme.Tests.csproj
    tests/Nova.FontConfig.Tests/Nova.FontConfig.Tests.csproj
    tests/Nova.FreeType.Tests/Nova.FreeType.Tests.csproj
    tests/Nova.HarfBuzz.Tests/Nova.HarfBuzz.Tests.csproj
    tests/Nova.DesktopTheme.Tests/Nova.DesktopTheme.Tests.csproj
    tests/Nova.DesktopTheme.Host.Tests/Nova.DesktopTheme.Host.Tests.csproj
)

# Build order. SdlSource pulls in every Nova.* + PresentationCore + WindowsBase;
# PresentationFramework pulls in the full impl set (System.Xaml, UIAutomation*,
# ReachFramework, System.Printing, System.Windows.Primitives,
# System.Windows.Input.Manipulations) through the cycle-breakers. Classic and
# PresentationUI are NOT transitive dependencies of anything — they must be
# built explicitly, in this order, or the harness/tests silently drop the
# theme references at runtime.
BUILD_ORDER=(
    "src/Nova.SdlSource/Nova.SdlSource.csproj"
    "third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/PresentationFramework/PresentationFramework.csproj"
    "third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/System.Windows.Controls.Ribbon/System.Windows.Controls.Ribbon.csproj"
    "third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Classic/PresentationFramework.Classic.csproj"
    "third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Aero2/PresentationFramework.Aero2.csproj"
    "third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/PresentationFramework.Fluent.csproj"
    "third_party/dotnet-wpf/src/Microsoft.DotNet.Wpf/src/PresentationUI/PresentationUI.csproj"
    "samples/ControlProbeHarness/ControlProbeHarness.csproj"
    "samples/Nova.Smoke/Nova.Smoke.csproj"
    "samples/Nova.XamlSample/Nova.XamlSample.csproj"
)

DO_BUILD=1
DO_TESTS=0
DO_HARNESS=0
DO_CLEAN=0
for arg in "$@"; do
    case "$arg" in
        --no-build) DO_BUILD=0 ;;
        --tests) DO_TESTS=1 ;;
        --harness) DO_HARNESS=1 ;;
        --clean) DO_CLEAN=1 ;;
        *) echo "usage: $0 [--no-build] [--tests] [--harness] [--clean]" >&2; exit 2 ;;
    esac
done

die() { echo "ERROR: $*" >&2; exit 1; }

# True if the submodule has modified TRACKED files (untracked case-alias
# symlinks do not count as dirt).
tracked_dirty() {
    ! git -C "$SUBMODULE" diff --quiet || ! git -C "$SUBMODULE" diff --cached --quiet
}

# ---------------------------------------------------------------------------
# 1. Prerequisites
# ---------------------------------------------------------------------------
for cmd in git dotnet perl; do
    command -v "$cmd" >/dev/null 2>&1 || die "missing prerequisite '$cmd' (system packages: see README 'Requirements')"
done
[ -n "$PIN" ] || die "no 40-hex pin found in $PATCH_DIR/UPSTREAM.txt"
[ -d "$PATCH_DIR" ] || die "patches/ directory not found at $PATCH_DIR"

echo "== bootstrap-worktree: repo=$REPO_ROOT pin=$PIN"

# ---------------------------------------------------------------------------
# 2. Submodule: initialize ONLY when missing/empty. Never update a live one.
# ---------------------------------------------------------------------------
if [ ! -e "$SUBMODULE/.git" ]; then
    if [ -d "$SUBMODULE" ] && [ -n "$(ls -A "$SUBMODULE" 2>/dev/null)" ]; then
        die "third_party/dotnet-wpf exists with content but is not a git checkout. Remove it manually and re-run (it is not an initialized submodule)."
    fi
    echo "== initializing submodule (fresh)..."
    git -C "$REPO_ROOT" submodule update --init third_party/dotnet-wpf
fi

HEAD="$(git -C "$SUBMODULE" rev-parse HEAD 2>/dev/null || true)"
if [ -z "$HEAD" ]; then
    die "submodule at $SUBMODULE has no HEAD; fix manually (git submodule update --init third_party/dotnet-wpf)"
fi
if [ "$HEAD" != "$PIN" ]; then
    die "submodule HEAD is $HEAD but the pin is $PIN. Do NOT run git checkout/reset against it. Check out the pin manually, or recreate this worktree."
fi
echo "== submodule at pin $PIN"

# ---------------------------------------------------------------------------
# 3. Sparse cone: the full 15-directory cone. A full checkout (fresh
#    `git submodule update --init`) makes every dir "present", so the check
#    must also verify the DELIBERATE EXCLUSION: PresentationBuildTasks must
#    be absent (its net6.0 build breaks the impl build — patches/README.md).
#    Apply only when the tree is pristine; refuse on a patched tree.
# ---------------------------------------------------------------------------
cone_correct() {
    local d
    for d in "${CONE[@]}"; do
        [ -d "$SUBMODULE/$d" ] || return 1
    done
    [ ! -e "$SUBMODULE/src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks" ]
}

if ! cone_correct; then
    if tracked_dirty; then
        die "sparse cone is incorrect (dirs missing or PresentationBuildTasks present) and the submodule has modified tracked files (patches applied). Refusing to run sparse-checkout on a patched tree. Fix manually per patches/README.md 'Fresh worktree setup', or recreate this worktree."
    fi
    echo "== applying sparse cone (narrowing full checkout)..."
    git -C "$SUBMODULE" sparse-checkout init --cone
    git -C "$SUBMODULE" sparse-checkout set "${CONE[@]}"
fi
echo "== sparse cone OK ($((${#CONE[@]})) dirs, PresentationBuildTasks excluded)"

# ---------------------------------------------------------------------------
# 4. Case-alias symlinks (idempotent).
# ---------------------------------------------------------------------------
for entry in "${ALIASES[@]}"; do
    target="${entry%%|*}"
    link="${entry##*|}"
    link_path="$SUBMODULE/$link"
    # The link's parent must exist and the real target dir must exist.
    [ -d "$(dirname "$link_path")" ] || die "alias parent missing for $link (tree incomplete?)"
    if [ -e "$link_path" ] && [ ! -L "$link_path" ]; then
        die "case-alias path $link exists as a real file/dir, not a symlink; remove it and re-run"
    fi
    ln -sfn "$target" "$link_path"
done
echo "== case aliases OK (${#ALIASES[@]})"

# ---------------------------------------------------------------------------
# 5. Patch series. Authoritative check: verify-series.sh (applies the whole
#    series from pristine and diffs against the live tree). Individual
#    `git apply --reverse --check` per patch is NOT reliable here: later
#    patches modify the same multi-owner files (PresentationCore.csproj,
#    PresentationFramework.csproj, ...), so a single patch's reverse-check
#    fails against the fully-patched tree even when the series IS applied.
#
#    verify-series FAILING on a PRISTINE tree is EXPECTED (it diffs patched
#    files against the live tree, which is unpatched). Distinguish the three
#    states by tracked-file dirtiness:
#      verify-series PASS              -> series fully applied, skip
#      verify-series FAIL + clean tree -> pristine, apply the full series
#      verify-series FAIL + dirty tree -> mixed state, refuse
#    The 9 case-alias symlinks are untracked and must NOT count as dirt.
# ---------------------------------------------------------------------------
if "$PATCH_DIR/verify-series.sh" >/dev/null 2>&1; then
    echo "== patch series already fully applied (verify-series: PASS)"
elif tracked_dirty; then
    die "patch series is NOT fully applied and the submodule has modified tracked files. verify-series.sh failed. The tree is in a mixed state; do not force patches onto it. Recreate this worktree (git worktree remove + add) and re-run, or repair the submodule manually."
else
    echo "== submodule pristine; applying patch series (${#SERIES[@]} patches, straight git apply)..."
    for name in "${SERIES[@]}"; do
        patch="$PATCH_DIR/$name.patch"
        [ -f "$patch" ] || die "patch file missing: $patch"
        git -C "$SUBMODULE" apply "$patch" || die "git apply failed on $name.patch; verify-series cannot compose. Do not use --3way."
        echo "  applied: $name"
    done
fi

# ---------------------------------------------------------------------------
# 6. Series verification gate (compose from pristine + per-file MATCH).
# ---------------------------------------------------------------------------
echo "== running patches/verify-series.sh..."
VERIFY_OUT="$("$PATCH_DIR/verify-series.sh")" || die "verify-series.sh FAILED (exit $?); the patch series does not reproduce the working tree. Do not build on this tree."
MATCH="$(echo "$VERIFY_OUT" | grep -oE 'MATCH=[0-9]+' | head -1)"
echo "$VERIFY_OUT" | tail -3
echo "== verify-series $MATCH (series composes from pristine)"

# ---------------------------------------------------------------------------
# 7. Staleness pre-flight + optional clean (--clean). The WPF build's
#    CoreCompile up-to-date check is mtime-based: a source file whose mtime
#    is OLDER than a previously-built intermediate dll (artifacts/obj) is
#    silently skipped even when its CONTENT changed — the build "succeeds"
#    while reusing the stale dll ("my rebuild did nothing"). The cache
#    (CoreCompileInputs.cache) hashes only paths, not contents. Triggers:
#    git archive | tar -x / cp -a of a patched tree (mtime-preserving),
#    branch-switch within a worktree, re-applying patches over an
#    already-built tree. Recovery: remove the intermediate state
#    (artifacts/obj + artifacts/bin are gitignored build output, never
#    tracked files) — the --clean flag does exactly that.
# ---------------------------------------------------------------------------
if [ "$DO_CLEAN" -eq 1 ]; then
    echo "== cleaning WPF artifacts (artifacts/obj + artifacts/bin)..."
    rm -rf "$SUBMODULE/artifacts/obj" "$SUBMODULE/artifacts/bin"
fi

if [ "$DO_BUILD" -eq 1 ] && [ -d "$SUBMODULE/artifacts/obj" ]; then
    # Warn when an impl project's newest source is OLDER than its
    # intermediate dll while the tree is patched (potential silent skip).
    for proj in WindowsBase PresentationCore PresentationFramework PresentationUI; do
        for tfmdir in net10.0 net11.0; do
            dll="$SUBMODULE/artifacts/obj/$proj/Debug/$tfmdir/$proj.dll"
            [ -f "$dll" ] || continue
            srcroot="$SUBMODULE/src/Microsoft.DotNet.Wpf/src"
            case "$proj" in
                WindowsBase) src="$srcroot/WindowsBase" ;;
                PresentationCore) src="$srcroot/PresentationCore" ;;
                PresentationFramework) src="$srcroot/PresentationFramework" ;;
                PresentationUI) src="$srcroot/PresentationUI" ;;
            esac
            newest="$(find "$src" -name '*.cs' -newer "$dll" 2>/dev/null | head -1)"
            if [ -z "$newest" ]; then
                echo "  WARNING: $proj: no source is newer than artifacts/obj/$proj/.../$tfmdir/$proj.dll —" \
                     "if this build appears to 'do nothing', the intermediate dll is stale." \
                     "Run with --clean to force a fresh compile."
            fi
        done
    done
fi

# ---------------------------------------------------------------------------
# 8. Build in dependency order (theme/UI before any consumer).
# ---------------------------------------------------------------------------
if [ "$DO_BUILD" -eq 1 ]; then
    for proj in "${BUILD_ORDER[@]}"; do
        echo "== dotnet build $proj"
        dotnet build "$REPO_ROOT/$proj" -c Debug
    done
    echo "== full build OK"
else
    echo "== build skipped (--no-build)"
fi

# ---------------------------------------------------------------------------
# 9. Optional: 19 test suites sequentially.
# ---------------------------------------------------------------------------
if [ "$DO_TESTS" -eq 1 ]; then
    [ "$DO_BUILD" -eq 1 ] || die "--tests requires the build (drop --no-build)"
    for proj in "${TESTS[@]}"; do
        echo "== dotnet test $proj"
        dotnet test "$REPO_ROOT/$proj" -c Debug
    done
    echo "== all test suites OK"
fi

# ---------------------------------------------------------------------------
# 10. Optional: harness.
# ---------------------------------------------------------------------------
if [ "$DO_HARNESS" -eq 1 ]; then
    [ "$DO_BUILD" -eq 1 ] || die "--harness requires the build (drop --no-build)"
    echo "== harness all"
    SDL_VIDEODRIVER=offscreen dotnet run --project "$REPO_ROOT/samples/ControlProbeHarness" --framework net11.0 -- all
    echo "== feat all"
    SDL_VIDEODRIVER=offscreen dotnet run --project "$REPO_ROOT/samples/ControlProbeHarness" --framework net11.0 -- feat all
    echo "== harness OK"
fi

echo "== bootstrap-worktree: DONE"
