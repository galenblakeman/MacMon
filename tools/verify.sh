#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f artifacts/local-feed/MonCore.3.4.1-macos.1.nupkg ]]; then
    echo "Run bash tools/bootstrap-moncore.sh first." >&2
    exit 1
fi
dotnet restore MacMon.slnx --locked-mode
dotnet build MacMon.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tools/LocalizationAudit -c Release --no-build --no-restore
dotnet test tests/MonCore.MacOS.Tests/MonCore.MacOS.Tests.csproj -c Release --no-build --no-restore -m:1 -p:UseSharedCompilation=false
bash -n tools/package-app.sh tools/package-release.sh tools/bootstrap-sparkle.sh tools/ci-signing.sh tools/build-native.sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/tests -p 'test_*.py'
if [[ "${1:-}" == "--smoke" ]]; then
    dotnet run --project src/MacMon -c Release --no-build --no-restore -- --smoke
    dotnet run --project src/MacMon -c Release --no-build --no-restore -- --launchd-smoke
    dotnet run --project src/MacMon -c Release --no-build --no-restore -- --hardware-smoke
fi
