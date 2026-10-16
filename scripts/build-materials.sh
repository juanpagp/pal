#!/usr/bin/env bash
# Recompiles PAL's materials (assets/Materials/*.mat -> *.filamat) with matc
# from the Filament SDK staged by the sibling FilamentSharp checkout.
#
# The compiled text_overlay.filamat is embedded into the PAL assembly (see
# src/PAL/PAL.csproj); after recompiling, rebuild PAL so the new bytes are
# picked up:
#
#   scripts/build-materials.sh
#   dotnet build PAL.sln
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck disable=SC1091
source "$ROOT/scripts/env.sh"

MATC="${FILAMENTSHARP_ROOT}/external/filament-sdk/bin/matc"
if [ ! -x "$MATC" ]; then
    echo "matc not found at: $MATC" >&2
    echo "Stage the Filament SDK in the sibling FilamentSharp repository first" >&2
    echo "(see FilamentSharp/docs/building.md)." >&2
    exit 1
fi

cd "$ROOT/assets/Materials"
for mat in *.mat; do
    out="${mat%.mat}.filamat"
    echo "compiling $mat -> $out"
    # -a all: every backend's shader language (SPIR-V for Vulkan included —
    # without it matc emits GLSL only and the material panics on Vulkan);
    # -p desktop: quality profile. Same flags as FilamentSharp's own
    # material build (src/FilamentSharp.Native/CMakeLists.txt).
    "$MATC" -a all -p desktop -o "$out" "$mat"
done

echo "done; rebuild PAL.sln to re-embed the updated materials"
