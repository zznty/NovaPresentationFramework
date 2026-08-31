#!/usr/bin/env bash
# Capture the live submodule's un-patched changes as a NEW patch and register it.
#
# Usage: patches/new-patch.sh <NNNN> <slug> "<one-line description>"
#
# The new patch is the exact diff between (pristine + all registered patches)
# and the live submodule working tree. The script then registers the patch in
# the THREE required places (patches/verify-series.sh SERIES,
# scripts/bootstrap-worktree.sh SERIES, patches/README.md registry) and runs
# verify-series. Fill in the README stub's narrative afterwards.
#
# Dependency-light: bash + git + diff. Cleans up its temp dir on exit.
set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH_DIR="$REPO_ROOT/patches"
SUBMODULE="$REPO_ROOT/third_party/dotnet-wpf"

die() { echo "new-patch: $1" >&2; exit 2; }

number="${1:-}"
slug="${2:-}"
description="${3:-}"
[[ "$number" =~ ^[0-9]{4}$ ]] || die "usage: new-patch.sh <NNNN> <slug> \"<one-line description>\""
[[ "$slug" =~ ^[a-z0-9-]+$ ]] || die "slug must be [a-z0-9-]+"
[ -n "$description" ] || die "missing description"
name="$number-$slug"
[ ! -f "$PATCH_DIR/$name.patch" ] || die "patches/$name.patch already exists"

# Load the registered series in order from verify-series.sh.
series=()
while IFS= read -r entry; do
    [[ "$entry" =~ ^[[:space:]]*([0-9]{4}-[a-z0-9-]+)[[:space:]]*$ ]] || continue
    series+=("${BASH_REMATCH[1]}")
done < <(sed -n '/^SERIES=(/,/^)/p' "$PATCH_DIR/verify-series.sh")

for existing in "${series[@]}"; do
    [ "$existing" != "$name" ] || die "'$name' is already registered"
done

# Pristine tree at the pinned SHA.
PIN="$(grep -oE '[0-9a-f]{40}' "$PATCH_DIR/UPSTREAM.txt" | head -1)"
[ -n "$PIN" ] || die "no pin found in patches/UPSTREAM.txt"
[ "$PIN" = "$(git -C "$SUBMODULE" rev-parse HEAD)" ] || die "submodule HEAD != UPSTREAM pin"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
git -C "$SUBMODULE" archive "$PIN" | tar -x -C "$tmp" >/dev/null 2>&1 || die "git archive failed"
git -C "$tmp" init -q .
git -C "$tmp" add -A >/dev/null 2>&1
git -C "$tmp" -c user.email=v@v -c user.name=v commit -qm base

# The intermediate must be pristine + every registered patch.
for existing in "${series[@]}"; do
    git -C "$tmp" apply "$PATCH_DIR/$existing.patch" >/dev/null 2>&1 \
        || die "registered patch $existing fails to apply — the series is broken"
done

# Collect the live changes not covered by any patch: modified/deleted tracked
# files plus untracked (new) files. Untracked entries that are not regular
# files (case-alias symlinks, directories) are skipped — only real new files
# can become patch content.
changed=()
while IFS= read -r path; do
    [ -f "$SUBMODULE/$path" ] || continue
    changed+=("$path")
done < <(git -C "$SUBMODULE" diff --name-only HEAD; git -C "$SUBMODULE" ls-files --others --exclude-standard)

# Uniquify preserving order.
mapfile -t changed < <(printf '%s\n' "${changed[@]}" | awk '!seen[$0]++')

[ "${#changed[@]}" -gt 0 ] || die "no live changes to capture (submodule tree matches the registered series)"

new_patch="$tmp/$name.patch"
: > "$new_patch"
for f in "${changed[@]}"; do
    [ -f "$SUBMODULE/$f" ] || {
        # Deleted file.
        if [ -f "$tmp/$f" ]; then
            {
                echo "diff --git a/$f b/$f"
                echo "deleted file mode 100644"
                echo "index $(git hash-object "$tmp/$f" | cut -c1-7)..0000000"
                echo "--- a/$f"
                echo "+++ /dev/null"
                diff -u "$tmp/$f" /dev/null | tail -n +3
            } >> "$new_patch"
        else
            die "live file $f is neither modified nor deleted relative to the series"
        fi
        continue
    }

    if [ -f "$tmp/$f" ]; then
        # Only files that actually differ from the patched intermediate belong
        # in the new patch; emit nothing for unchanged ones (the submodule's
        # HEAD is pristine, so git diff lists every patched file too).
        body="$(diff -u "$tmp/$f" "$SUBMODULE/$f" | tail -n +3)"
        [ -n "$body" ] || continue
        {
            echo "diff --git a/$f b/$f"
            echo "index $(git hash-object "$tmp/$f" | cut -c1-7)..$(git hash-object "$SUBMODULE/$f" | cut -c1-7) 100644"
            echo "--- a/$f"
            echo "+++ b/$f"
            printf '%s\n' "$body"
        } >> "$new_patch"
    else
        # New file.
        {
            echo "diff --git a/$f b/$f"
            echo "new file mode 100644"
            echo "index 0000000..$(git hash-object "$SUBMODULE/$f" | cut -c1-7)"
            echo "--- /dev/null"
            echo "+++ b/$f"
            diff -u /dev/null "$SUBMODULE/$f" | tail -n +3
        } >> "$new_patch"
    fi
done

[ -s "$new_patch" ] || die "the computed diff is empty"

cp "$new_patch" "$PATCH_DIR/$name.patch"

# Register in the three required places.
for script in "$PATCH_DIR/verify-series.sh" "$REPO_ROOT/scripts/bootstrap-worktree.sh"; do
    if grep -q "^    $name$" "$script"; then
        continue
    fi
    last="$(grep -n '^    [0-9]\{4\}-[a-z0-9-]\+$' "$script" | tail -1 | cut -d: -f1)"
    [ -n "$last" ] || die "no SERIES entries found in $script"
    sed -i "${last}a\\    $name" "$script"
done

readme_stub="- \`$name.patch\` — $description (2026-08-28).
  New patched files:"
for f in "${changed[@]}"; do
    readme_stub+="
  \`$f\`,"
done
readme_stub+=" generated against the intermediate state after ${series[${#series[@]}-1]}."
# The README registry is newest-first: insert the stub (with a separating blank
# line) before the first entry.
awk -v stub="$readme_stub" '
    /^- `[0-9]{4}-[a-z0-9-]*\.patch`/ && !done { print stub "\n"; done = 1 }
    { print }
' "$PATCH_DIR/README.md" > "$tmp/README.md.new"
if grep -q '^- `[0-9]\{4\}-[a-z0-9-]*\.patch`' "$tmp/README.md.new"; then
    mv "$tmp/README.md.new" "$PATCH_DIR/README.md"
else
    printf '\n%s\n' "$readme_stub" >> "$PATCH_DIR/README.md"
fi

echo "new-patch: captured ${#changed[@]} file(s) into patches/$name.patch and registered it"
"$PATCH_DIR/verify-series.sh"
