#!/usr/bin/env bash
# Regenerate ONE patch of the series from the live submodule working tree.
#
# Usage: patches/regenerate.sh <NNNN[-slug]>            (with or without .patch)
#
# The patch's hunks are re-derived as exactly its contribution: for every file
# it touches, before-state = pristine + all PRIOR patches, after-state = the
# live file with all LATER patches reverse-applied (restricted to that file).
# The composed patch is self-checked (apply onto before-state must reproduce
# every after-state byte-for-byte) before it replaces the original, then
# verify-series runs.
#
# If a later patch no longer reverse-applies (its context depends on this
# patch's edits), regeneration must proceed NEWEST-FIRST: regenerate the later
# patch before this one. A file whose contribution is fully superseded by
# later patches is dropped from the regenerated patch with a NOTE.
#
# Dependency-light: bash + git + diff. Cleans up its temp dir on exit.
set -u

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PATCH_DIR="$REPO_ROOT/patches"
SUBMODULE="$REPO_ROOT/third_party/dotnet-wpf"

die() { echo "regenerate: $1" >&2; exit 2; }

name="${1:-}"
name="${name%.patch}"
[ -n "$name" ] || die "usage: regenerate.sh <NNNN[-slug]>"

# Load the registered series in order from verify-series.sh.
series=()
while IFS= read -r entry; do
    [[ "$entry" =~ ^[[:space:]]*([0-9]{4}-[a-z0-9-]+)[[:space:]]*$ ]] || continue
    series+=("${BASH_REMATCH[1]}")
done < <(sed -n '/^SERIES=(/,/^)/p' "$PATCH_DIR/verify-series.sh")

idx=-1
for i in "${!series[@]}"; do
    if [ "${series[$i]}" = "$name" ]; then idx=$i; break; fi
done
[ "$idx" -ge 0 ] || die "'$name' is not registered in patches/verify-series.sh SERIES"
[ -f "$PATCH_DIR/$name.patch" ] || die "patches/$name.patch does not exist"

# Pristine tree at the pinned SHA.
PIN="$(grep -oE '[0-9a-f]{40}' "$PATCH_DIR/UPSTREAM.txt" | head -1)"
[ -n "$PIN" ] || die "no pin found in patches/UPSTREAM.txt"
[ "$PIN" = "$(git -C "$SUBMODULE" rev-parse HEAD)" ] || die "submodule HEAD != UPSTREAM pin"

tmp="$(mktemp -d)"
trap 'chmod -R u+w "$tmp" 2>/dev/null; rm -rf "$tmp" 2>/dev/null || { sleep 0.2; rm -rf "$tmp" 2>/dev/null; }' EXIT
git -C "$SUBMODULE" archive "$PIN" | tar -x -C "$tmp" >/dev/null 2>&1 || die "git archive failed"
git -C "$tmp" init -q .
git -C "$tmp" add -A >/dev/null 2>&1
git -C "$tmp" -c user.email=v@v -c user.name=v commit -qm base

# 1. Apply all PRIOR patches (they must apply — a valid series guarantees it).
for ((i = 0; i < idx; i++)); do
    git -C "$tmp" apply "$PATCH_DIR/${series[$i]}.patch" >/dev/null 2>&1 \
        || die "prior patch ${series[$i]} fails to apply — series is broken"
done

# The files this patch owns, in the order they appear in its diff headers.
# Unique file list in first-appearance order: the old patch may legitimately
# list one file in several diff sections (a file patched twice historically);
# the regenerated patch merges them into one section with all hunks.
files=()
while IFS= read -r path; do
    files+=("$path")
done < <(awk '/^diff --git/{print $3}' "$PATCH_DIR/$name.patch" | sed 's|^a/||' | awk '!seen[$0]++')

[ "${#files[@]}" -gt 0 ] || die "patches/$name.patch declares no files"

new_patch="$tmp/$name.patch"
: > "$new_patch"
after_dir="$tmp/after"
mkdir -p "$after_dir"
git -C "$after_dir" init -q .
kept=()

for f in "${files[@]}"; do
    # 2. after-state: the live file minus every LATER patch's contribution to it
    #    (reverse order; only patches that actually touch this file).
    after="$after_dir/$f"
    mkdir -p "$(dirname "$after")"
    if [ -f "$SUBMODULE/$f" ]; then
        cp "$SUBMODULE/$f" "$after"
    fi

    for ((i = ${#series[@]} - 1; i > idx; i--)); do
        grep -q "^diff --git a/$f b/$f$" "$PATCH_DIR/${series[$i]}.patch" || continue
        if [ -f "$after" ]; then
            git -C "$after_dir" apply -R --include="$f" "$PATCH_DIR/${series[$i]}.patch" >/dev/null 2>&1 \
                || die "later patch ${series[$i]} no longer reverse-applies to $f — regenerate ${series[$i]} first (newest-first)"
        fi
    done

    before="$tmp/$f"
    [ -f "$before" ] || before=/dev/null

    if [ ! -f "$after" ]; then
        if [ -f "$SUBMODULE/$f" ]; then
            # The live file consists entirely of later patches' contributions:
            # this patch's hunks for it are fully superseded.
            echo "regenerate: NOTE: $f fully superseded by later patches — hunks dropped"
            continue
        fi
        if [ -f "$tmp/$f" ]; then
            # The patch deletes the file.
            kept+=("$f")
            {
                echo "diff --git a/$f b/$f"
                echo "deleted file mode 100644"
                echo "index $(git hash-object "$tmp/$f" | cut -c1-7)..0000000"
                echo "--- a/$f"
                echo "+++ /dev/null"
                diff -u "$tmp/$f" /dev/null | tail -n +3
            } >> "$new_patch"
        else
            die "file $f exists in neither before nor after state"
        fi
        continue
    fi

    if [ ! -f "$tmp/$f" ]; then
        # The patch creates the file.
        kept+=("$f")
        {
            echo "diff --git a/$f b/$f"
            echo "new file mode 100644"
            echo "index 0000000..$(git hash-object "$after" | cut -c1-7)"
            echo "--- /dev/null"
            echo "+++ b/$f"
            diff -u /dev/null "$after" | tail -n +3
        } >> "$new_patch"
        continue
    fi

    if diff -q "$tmp/$f" "$after" >/dev/null 2>&1; then
        echo "regenerate: NOTE: $f unchanged by this patch (later patches supersede?) — hunks dropped"
        continue
    fi

    kept+=("$f")
    {
        echo "diff --git a/$f b/$f"
        echo "index $(git hash-object "$tmp/$f" | cut -c1-7)..$(git hash-object "$after" | cut -c1-7) 100644"
        echo "--- a/$f"
        echo "+++ b/$f"
        diff -u "$tmp/$f" "$after" | tail -n +3
    } >> "$new_patch"
done

[ "${#kept[@]}" -gt 0 ] || die "every file in $name.patch is superseded — delete the patch instead"

# 3. Self-check: the regenerated patch applied onto before-state must reproduce
#    every kept after-state byte-for-byte.
check_dir="$tmp/check"
mkdir -p "$check_dir"
git -C "$SUBMODULE" archive "$PIN" | tar -x -C "$check_dir" >/dev/null 2>&1
git -C "$check_dir" init -q .
git -C "$check_dir" add -A >/dev/null 2>&1
git -C "$check_dir" -c user.email=v@v -c user.name=v commit -qm base
for ((i = 0; i < idx; i++)); do
    git -C "$check_dir" apply "$PATCH_DIR/${series[$i]}.patch" >/dev/null 2>&1 \
        || die "self-check: prior patch ${series[$i]} fails to apply"
done
git -C "$check_dir" apply "$new_patch" >/dev/null 2>&1 \
    || die "self-check: the regenerated patch does not apply onto before-state"
for f in "${kept[@]}"; do
    if [ -f "$after_dir/$f" ]; then
        diff -q "$check_dir/$f" "$after_dir/$f" >/dev/null 2>&1 \
            || die "self-check: $f does not reproduce the after-state"
    elif [ -f "$check_dir/$f" ]; then
        die "self-check: $f should be absent after applying the patch"
    fi
done

# 4. Replace the original (backing it up) and verify the full series.
backup_dir="$PATCH_DIR/.regenerate-backup"
mkdir -p "$backup_dir"
if ! cmp -s "$new_patch" "$PATCH_DIR/$name.patch"; then
    cp "$PATCH_DIR/$name.patch" "$backup_dir/$name.patch.$(date +%Y%m%d%H%M%S)"
    cp "$new_patch" "$PATCH_DIR/$name.patch"
    echo "regenerate: $name.patch rewritten (previous version in patches/.regenerate-backup/)"
else
    echo "regenerate: $name.patch is byte-identical (no live changes to fold in)"
fi

"$PATCH_DIR/verify-series.sh"
