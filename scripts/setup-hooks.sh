#!/usr/bin/env bash
# Point this clone at the in-repo hooks dir.
# Run once after clone; idempotent.

set -euo pipefail

repo_root=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
cd "$repo_root"

git config core.hooksPath .githooks
chmod +x .githooks/*

echo "hooks configured: $(git config --get core.hooksPath)"
