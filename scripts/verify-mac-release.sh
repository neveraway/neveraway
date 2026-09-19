#!/bin/bash
set -euo pipefail
APP=$1
REQUIREMENT='anchor apple generic and identifier "com.royashbrook.neveraway" and certificate 1[field.1.2.840.113635.100.6.2.6] exists and certificate leaf[field.1.2.840.113635.100.6.1.13] exists and certificate leaf[subject.OU] = "44Y2L8A2CV"'
codesign --verify --deep --strict --verbose=2 -R "$REQUIREMENT" "$APP"
codesign -dv --verbose=4 "$APP" 2>&1 | grep -q 'flags=.*runtime'
spctl --assess --type execute --verbose=2 "$APP"
xcrun stapler validate "$APP"
