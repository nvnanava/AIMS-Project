#!/usr/bin/env bash
set -euo pipefail

dotnet tool restore >/dev/null

echo "[format] Fixing whitespace/EOL…"
dotnet tool run dotnet-format --fix-whitespace .

# If your machine is OK loading analyzers, uncomment below to also fix style/imports:
# echo "[format] Fixing style/imports (may load analyzers)…"
dotnet tool run dotnet-format --fix-style warn --fix-analyzers warn .

echo "[format] Done. Stage changes:"
echo "  git add -A"