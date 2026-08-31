#!/usr/bin/env bash
# Verify the patch series composes from pristine and reproduces the live
# submodule working tree. REQUIRED before any commit touching patches/
# (see patches/README.md "Series verification").
#
#  1. extracts a pristine archive of the pinned SHA (git archive, never
#     touches the real submodule) into a temp dir
#  2. applies 0001..0015 with STRAIGHT `git apply` (no --3way, no fuzz)
#  3. diffs every patched file against the live submodule working tree
#     and prints a MATCH/DIFFER table
#  4. exits non-zero on any failed apply or any DIFFER
#
# Dependency-light: bash + git + diff. Cleans up its temp dir on exit.
set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH_DIR="$REPO_ROOT/patches"
SUBMODULE="$REPO_ROOT/third_party/dotnet-wpf"
TMPDIR_VERIFY="$(mktemp -d)"
trap 'rm -rf "$TMPDIR_VERIFY"' EXIT

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





)

fail=0

# 1. Pristine tree at the pinned SHA.
PIN="$(grep -oE '[0-9a-f]{40}' "$PATCH_DIR/UPSTREAM.txt" | head -1)"
if [ -z "$PIN" ]; then
    echo "verify-series: no pin found in patches/UPSTREAM.txt" >&2
    exit 2
fi
HEAD="$(git -C "$SUBMODULE" rev-parse HEAD)"
if [ "$PIN" != "$HEAD" ]; then
    echo "verify-series: submodule HEAD ($HEAD) != UPSTREAM pin ($PIN)" >&2
    exit 2
fi
if ! git -C "$SUBMODULE" archive "$PIN" | tar -x -C "$TMPDIR_VERIFY" >/dev/null 2>&1; then
    echo "verify-series: git archive failed (submodule missing objects?)" >&2
    exit 2
fi
git -C "$TMPDIR_VERIFY" init -q .
git -C "$TMPDIR_VERIFY" add -A >/dev/null 2>&1
git -C "$TMPDIR_VERIFY" -c user.email=v@v -c user.name=v commit -qm base

# 2. Apply the full series in order, straight.
echo "== applying $SERIES from pristine (straight git apply) =="
for name in "${SERIES[@]}"; do
    if git -C "$TMPDIR_VERIFY" apply "$PATCH_DIR/$name.patch" >/dev/null 2>&1; then
        echo "  OK   $name"
    else
        echo "  FAIL $name"
        fail=1
    fi
done

# 3. Diff every patched file against the live submodule.
echo "== per-file MATCH/DIFFER vs live submodule =="
patched_files="$TMPDIR_VERIFY/patched-files.txt"
: > "$patched_files"
for name in "${SERIES[@]}"; do
    awk '/^diff --git/{print $3}' "$PATCH_DIR/$name.patch" | sed 's|a/||' >> "$patched_files"
done
sort -u "$patched_files" -o "$patched_files"

match=0
differ=0
while IFS= read -r f; do
    [ -f "$TMPDIR_VERIFY/$f" ] || continue
    [ -f "$SUBMODULE/$f" ] || { echo "  DIFFER $f (missing in live)"; differ=$((differ+1)); continue; }
    if diff -q "$TMPDIR_VERIFY/$f" "$SUBMODULE/$f" >/dev/null 2>&1; then
        match=$((match+1))
    else
        echo "  DIFFER $f"
        differ=$((differ+1))
    fi
done < "$patched_files"

echo "== MATCH=$match DIFFER=$differ =="
if [ "$fail" -ne 0 ] || [ "$differ" -ne 0 ]; then
    echo "verify-series: FAILED (failed applies: $fail, differs: $differ)" >&2
    exit 1
fi
echo "verify-series: PASS (series composes and reproduces the working tree)"
exit 0
