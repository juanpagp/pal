#!/usr/bin/env bash
# Build the PAL solution and run both test suites.
#
#   scripts/run-tests.sh              unit + integration (GPU tests run when
#                                     a Vulkan ICD is visible, else skip)
#   PAL_WINDOWED_TESTS=1 DISPLAY=:99 scripts/run-tests.sh
#                                     also run the real-window tests (Xvfb
#                                     works: Xvfb :99 -screen 0 1024x768x24)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck disable=SC1091
source "$ROOT/scripts/env.sh"

cd "$ROOT"
dotnet build PAL.sln
dotnet test PAL.sln --no-build --logger "console;verbosity=normal"
