#!/usr/bin/env bash
# Central environment helper for PAL development scripts.
# Source this file (do not execute):  source scripts/env.sh
#
# Resolution order for every override:
#   1. Already-set environment variable (honored as-is).
#   2. scripts/env.local.sh (git-ignored, machine specific).
#   3. Repo-relative defaults derived from a sibling FilamentSharp checkout.
#
# What this provides:
#   FILAMENTSHARP_NATIVE_DIR  -> where libFilamentSharp.Native.so lives
#   VK_ICD_FILENAMES          -> software Vulkan (lavapipe) when the sibling
#                                repo ran scripts/setup-headless-vulkan.sh
#   LD_LIBRARY_PATH           -> lavapipe + (optionally) a user-local libc++
#                                runtime needed by the native bridge
PAL_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Machine-specific overrides (optional).
if [ -z "${PAL_NO_LOCAL_ENV:-}" ] && [ -f "$PAL_ROOT/scripts/env.local.sh" ]; then
    # shellcheck disable=SC1091
    source "$PAL_ROOT/scripts/env.local.sh"
fi

# The sibling FilamentSharp repository (the local NuGet feed in
# nuget.config already expects this layout: ../FilamentSharp).
FILAMENTSHARP_ROOT="${FILAMENTSHARP_ROOT:-$PAL_ROOT/../FilamentSharp}"

# Native bridge produced by FilamentSharp's scripts/build-native.sh.
if [ -z "${FILAMENTSHARP_NATIVE_DIR:-}" ] &&
        [ -f "$FILAMENTSHARP_ROOT/external/native-install/lib/libFilamentSharp.Native.so" ]; then
    export FILAMENTSHARP_NATIVE_DIR="$FILAMENTSHARP_ROOT/external/native-install/lib"
fi

# Software Vulkan runtime (lavapipe) set up by FilamentSharp's
# scripts/setup-headless-vulkan.sh (no root required).
if [ -z "${VK_ICD_FILENAMES:-}" ] &&
        [ -f "$FILAMENTSHARP_ROOT/external/vulkan-runtime/lvp_icd_local.json" ]; then
    export VK_ICD_FILENAMES="$FILAMENTSHARP_ROOT/external/vulkan-runtime/lvp_icd_local.json"
    _PAL_LVP_LIB="$FILAMENTSHARP_ROOT/external/vulkan-runtime/usr/lib/x86_64-linux-gnu"
    export LD_LIBRARY_PATH="${_PAL_LVP_LIB}${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    unset _PAL_LVP_LIB
fi
