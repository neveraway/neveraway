#!/bin/bash
set -euo pipefail

# Both consumers execute or redistribute this archive with release authority.
VERSION=2.9.4
SHA256=ce89daf967db1e1893ed3ebd67575ed82d3902563e3191ca92aaec9164fbdef9
curl -fSL --retry 3 -o "$RUNNER_TEMP/sparkle.tar.xz" \
  "https://github.com/sparkle-project/Sparkle/releases/download/$VERSION/Sparkle-$VERSION.tar.xz"
printf '%s  %s\n' "$SHA256" "$RUNNER_TEMP/sparkle.tar.xz" | shasum -a 256 -c -
mkdir -p "$RUNNER_TEMP/sparkle"
tar -xJf "$RUNNER_TEMP/sparkle.tar.xz" -C "$RUNNER_TEMP/sparkle"
