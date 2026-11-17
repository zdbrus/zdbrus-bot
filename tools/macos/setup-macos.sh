#!/bin/bash
#
# Prepares the macOS (Steam) copy of Firestone to run MelonLoader and Zdbrus Bot.
#
# The macOS build of Firestone is a Mono build whose .NET and Unity assemblies are
# stripped, and MelonLoader cannot start on them (LavaGang/MelonLoader#1173). This
# script replaces them with the unstripped copies from Unity's own macOS package,
# for the exact Unity version the game uses, and makes MelonLoader's launch script
# work on Apple Silicon (through Rosetta).
#
# Usage:
#   tools/macos/setup-macos.sh [game folder]            set up (safe to run again)
#   tools/macos/setup-macos.sh --restore [game folder]  put the original files back
#
# The game folder defaults to the Steam library in ~/Library/Application Support.
# Set UNITY_PKG to a local Unity-<version>.pkg to skip the ~5 GB download.
#
# Run it again after every game update or Steam "Verify integrity of game files",
# both of which put the stripped assemblies back.

set -euo pipefail

RESTORE=0
if [ "${1:-}" = "--restore" ]; then
    RESTORE=1
    shift
fi

GAME_ROOT="${1:-$HOME/Library/Application Support/Steam/steamapps/common/Firestone}"
APP="$GAME_ROOT/Firestone.app"
MANAGED="$APP/Contents/Resources/Data/Managed"
LAUNCH_SCRIPT="$GAME_ROOT/melonloader-launch.sh"
BACKUP="$GAME_ROOT/MacSetupBackup"

info() { printf '==> %s\n' "$*"; }
fail() { printf 'error: %s\n' "$*" >&2; exit 1; }

[ -d "$MANAGED" ] || fail "Firestone not found in '$GAME_ROOT'. Pass the game folder as an argument."

if pgrep -f "$APP/Contents/MacOS/" >/dev/null 2>&1; then
    fail "Firestone is running. Close the game first."
fi

restore() {
    [ -d "$BACKUP" ] || fail "no backup found in '$BACKUP'; nothing to restore. If the game was set up by another tool (for example Firebot's setup-macos.sh), restore with that tool, or use Steam's Verify integrity of game files."
    for file in "$BACKUP"/*.dll; do
        cp "$file" "$MANAGED/"
    done
    if [ -f "$BACKUP/added.txt" ]; then
        while IFS= read -r name; do
            [ -n "$name" ] && rm -f "$MANAGED/$name"
        done < "$BACKUP/added.txt"
    fi
    rm -rf "$BACKUP"
    info "Original game assemblies restored."
}

if [ "$RESTORE" = 1 ]; then
    restore
    exit 0
fi

[ -f "$GAME_ROOT/MelonLoader.Bootstrap.dylib" ] && [ -f "$LAUNCH_SCRIPT" ] ||
    fail "MelonLoader for macOS is not installed in '$GAME_ROOT'. Extract MelonLoader.macOS.x64.zip there first."

# --- 1. Rosetta ---------------------------------------------------------------

if [ "$(uname -m)" = "arm64" ] && ! /usr/bin/arch -x86_64 /usr/bin/true 2>/dev/null; then
    fail "Rosetta is required on Apple Silicon. Install it with: softwareupdate --install-rosetta"
fi

# --- 2. MelonLoader launch script ---------------------------------------------

# MelonLoader's bootstrap is x86_64-only, so on Apple Silicon the game has to run
# under Rosetta. /usr/bin/arch is SIP-protected and drops DYLD_* variables, so they
# are passed on explicitly. The block goes right before the script's final exec.
if ! grep -q 'arch -x86_64' "$LAUNCH_SCRIPT"; then
    [ -f "$LAUNCH_SCRIPT.orig" ] || cp "$LAUNCH_SCRIPT" "$LAUNCH_SCRIPT.orig"
    perl -0pi -e '
        my $block = <<'\''BLOCK'\'';
# Added by setup-macos.sh: the bootstrap dylib is x86_64-only, so run the
# game under Rosetta on Apple Silicon. /usr/bin/arch drops DYLD_* vars; pass them.
if [ "$(uname -m)" = "arm64" ] && ! lipo -archs "$BOOTSTRAP_PATH" 2>/dev/null | grep -qw arm64; then
    exec /usr/bin/arch -x86_64 \
        -e DYLD_INSERT_LIBRARIES="$DYLD_INSERT_LIBRARIES" \
        -e DYLD_LIBRARY_PATH="$DYLD_LIBRARY_PATH" \
        "$@"
fi

BLOCK
        s/(\nexec "\$\@"\s*)\z/\n$block$1/ or die "final exec not found\n";
    ' "$LAUNCH_SCRIPT"
    info "Patched melonloader-launch.sh to start the game under Rosetta."
fi
chmod +x "$LAUNCH_SCRIPT"

# --- 3. Unstripped assemblies -------------------------------------------------

UNITY_VERSION=$(LC_ALL=C grep -aoE '(20[0-9]{2}|[6-9][0-9]{3})\.[0-9]+\.[0-9]+f[0-9]+' \
    "$APP/Contents/Frameworks/UnityPlayer.dylib" | head -1 || true)
[ -n "$UNITY_VERSION" ] || fail "could not read the game's Unity version."
info "Game uses Unity $UNITY_VERSION."

WORK=$(mktemp -d "${TMPDIR:-/tmp}/zdbrus-macos.XXXXXX")
trap 'rm -rf "$WORK"' EXIT

PKG="${UNITY_PKG:-}"
if [ -z "$PKG" ]; then
    FREE_KB=$(df -k "$WORK" | awk 'NR==2 {print $4}')
    [ "$FREE_KB" -gt $((11 * 1024 * 1024)) ] || fail "about 11 GB of free disk space is needed in '$WORK'."

    curl -fsSL "https://services.api.unity.com/unity/editor/release/v1/releases?version=$UNITY_VERSION&platform=MAC_OS&architecture=X86_64" \
        -o "$WORK/release.json" || fail "could not query Unity's release API."
    URL=$(plutil -extract results.0.downloads.0.url raw -o - "$WORK/release.json" 2>/dev/null || true)
    [[ "$URL" == https://download.unity3d.com/* ]] || fail "no macOS editor download found for Unity $UNITY_VERSION."

    PKG="$WORK/Unity.pkg"
    info "Downloading the Unity $UNITY_VERSION macOS editor package (~5 GB). Only a few files are kept."
    curl -fL --progress-bar -o "$PKG" "$URL"
fi
[ -f "$PKG" ] || fail "Unity package not found: $PKG"

pkgutil --check-signature "$PKG" | grep -q 'Unity Technologies' || fail "the Unity package is not signed by Unity Technologies."

info "Extracting the unstripped assemblies..."
pkgutil --expand "$PKG" "$WORK/expanded"
if [ -z "${UNITY_PKG:-}" ]; then rm -f "$PKG"; fi
PAYLOAD=$(find "$WORK/expanded" -maxdepth 2 -name Payload | head -1)
[ -n "$PAYLOAD" ] || fail "unexpected Unity package layout."

mkdir -p "$WORK/unity"
(cd "$WORK/unity" && gunzip -c "$PAYLOAD" | cpio -id --quiet \
    './Unity/Unity.app/Contents/MonoBleedingEdge/lib/mono/unityjit-macos/*.dll' \
    './Unity/Unity.app/Contents/PlaybackEngines/MacStandaloneSupport/Variations/mono/Managed/UnityEngine.CoreModule.dll')
rm -rf "$WORK/expanded"

CORLIB="$WORK/unity/Unity/Unity.app/Contents/MonoBleedingEdge/lib/mono/unityjit-macos"
PLAYER="$WORK/unity/Unity/Unity.app/Contents/PlaybackEngines/MacStandaloneSupport/Variations/mono/Managed"
[ -f "$CORLIB/mscorlib.dll" ] && [ -f "$CORLIB/Facades/netstandard.dll" ] && [ -f "$PLAYER/UnityEngine.CoreModule.dll" ] ||
    fail "the Unity package does not contain the expected assemblies."

# Back up each original once, so running the script again never overwrites the
# backup with an already replaced file.
mkdir -p "$BACKUP"
touch "$BACKUP/added.txt"
replace() { # <source> <name>
    # Already the unstripped copy (for example set up by Firebot's script): nothing to back up.
    if [ -f "$MANAGED/$2" ] && cmp -s "$1" "$MANAGED/$2"; then
        return
    fi
    if [ -f "$MANAGED/$2" ]; then
        [ -f "$BACKUP/$2" ] || cp "$MANAGED/$2" "$BACKUP/$2"
    elif ! grep -qx "$2" "$BACKUP/added.txt"; then
        echo "$2" >> "$BACKUP/added.txt"
    fi
    cp "$1" "$MANAGED/$2"
}

for file in "$MANAGED"/*.dll; do
    name=$(basename "$file")
    if [ -f "$CORLIB/$name" ]; then
        replace "$CORLIB/$name" "$name"
    fi
done
replace "$CORLIB/Facades/netstandard.dll" netstandard.dll
replace "$PLAYER/UnityEngine.CoreModule.dll" UnityEngine.CoreModule.dll
info "Unstripped .NET assemblies, UnityEngine.CoreModule and netstandard are in place (originals in $BACKUP)."

cat <<EOF

Done. In Steam, open Firestone > Properties > General > Launch Options and set:

    "$LAUNCH_SCRIPT" %command%

Then start the game from Steam (starting it outside Steam does not log in).
To undo: tools/macos/setup-macos.sh --restore
EOF
