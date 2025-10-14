#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"

dotnet tool restore | Out-Null
Write-Host "[pre-commit] Checking EditorConfig formatting (whitespace/EOL only)…"

dotnet tool run dotnet-format `
  --check `
  --fix-whitespace `
  .