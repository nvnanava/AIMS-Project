#!/usr/bin/env bash
set -euo pipefail

### ------------------------------------------------------------------------
# Test runner for assemblies, classes, or methods with pretty HTML + summary
#
# Suites:
#   --suite integration   # (default) Integration tests
#   --suite unit          # Unit tests
#   --suite both          # Run Integration + Unit together (one combined HTML/JUnit)
#
# Examples:
#   ./scripts/test-class.sh                              # integration assembly
#   ./scripts/test-class.sh --suite unit                 # unit assembly
#   ./scripts/test-class.sh --suite both                 # both assemblies (one HTML)
#   ./scripts/test-class.sh AIMS.Tests.Integration.SchemaTests
#   ./scripts/test-class.sh AIMS.Tests.Integration.SchemaTests.Invalid_Assignment... --method
#   ./scripts/test-class.sh --suite both --coverage      # merged coverage
#   ./scripts/test-class.sh AIMS.UnitTests/AIMS.UnitTests.csproj --coverage
### ------------------------------------------------------------------------

# Always force UTF-8 so emojis render consistently
export LANG=en_US.UTF-8
export LC_ALL=en_US.UTF-8

# ---------------- Portable helpers -----------------------------------------
upper() { printf "%s" "$1" | tr '[:lower:]' '[:upper:]'; }

inplace_sed() {
  if sed --version >/dev/null 2>&1; then sed -i "$1" "$2"; else sed -i '' "$1" "$2"; fi
}

# ---------------- Defaults --------------------------------------------------
# Primary (integration) defaults
INT_PROJ="AIMS.Tests.Integration/AIMS.Tests.Integration.csproj"

# Unit defaults
UNIT_PROJ="AIMS.UnitTests/AIMS.UnitTests.csproj"

CONFIG="Debug"
NO_RESTORE="false"
NO_BUILD="false"
PARALLEL="none"          # default (good for integration)
RETRY=0
USE_METHOD_FILTER="false"
USE_TRAIT_FILTER=""
USE_NOTRAIT_FILTER=""
WITH_COVERAGE="false"
SUITE="auto"             # integration | unit | both | auto

# --------- Parse positional TARGET (optional) ------------------------------
if [[ $# -ge 1 && "$1" != --* ]]; then
  TARGET="$1"; shift
else
  TARGET=""   # empty means “whole assembly for the chosen suite”
fi

# --------- Parse flags -----------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --suite) SUITE="${2:-auto}"; shift 2;;
    --method) USE_METHOD_FILTER="true"; shift;;
    --config) CONFIG="${2:-$CONFIG}"; shift 2;;
    --no-restore) NO_RESTORE="true"; shift;;
    --no-build) NO_BUILD="true"; shift;;
    --parallel) PARALLEL="${2:-$PARALLEL}"; shift 2;;
    --retry) RETRY="${2:-0}"; shift 2;;
    --trait) USE_TRAIT_FILTER="${2:-}"; shift 2;;
    --notrait) USE_NOTRAIT_FILTER="${2:-}"; shift 2;;
    --coverage) WITH_COVERAGE="true"; shift;;
    -h|--help)
      cat <<EOF
Usage: $0 [<Class|Method|PathToDll|PathToCsproj>] [options]

Options:
  --suite integration|unit|both   Which suite to run (default: auto)
  --method                        Treat TARGET as a method (otherwise class)
  --config Release|Debug          Build config (default: Debug)
  --no-restore                    Skip dotnet restore
  --no-build                      Skip dotnet build
  --parallel auto|none            xunit parallel mode (default: none)
  --retry N                       Re-run failed tests once (N>0)
  --trait "k=v"                   Include trait filter
  --notrait "k=v"                 Exclude trait filter
  --coverage                      Collect coverage (coverlet.collector)

Examples:
  $0                                   # integration assembly
  $0 --suite unit                      # unit assembly
  $0 --suite both                      # both assemblies (one HTML/JUnit)
  $0 AIMS.Tests.Integration.SchemaTests
  $0 AIMS.Tests.Integration.SchemaTests.Invalid... --method
  $0 --suite both --coverage           # merged coverage
EOF
      exit 0;;
    *) echo "Unknown option: $1"; exit 2;;
  esac
done

# ---------------- Helpers (cross-platform safe) ----------------------------
# Resolve the built DLL (handles RID subfolders like osx-arm64/win-x64/linux-x64)
# Usage: resolve_dll_from_proj <path/to/test.csproj> <Debug|Release>
resolve_dll_from_proj() {
  local proj="$1"; local cfg="$2"
  local proj_dir proj_name base search out
  proj_dir="$(dirname "$proj")"
  proj_name="$(basename "$proj" .csproj)"
  base="$proj_dir/bin/$cfg"

  # Prefer net9.0 first (depth 2 catches RID folders)
  search="$base/net9.0"
  if [[ -d "$search" ]]; then
    out="$(find "$search" -type f -name "${proj_name}.dll" 2>/dev/null | head -n1 || true)"
    [[ -n "$out" ]] && { echo "$out"; return 0; }
  fi

  # Fallback: any TFM under this config (netX.Y or netX.Y/*/name.dll)
  out="$(find "$base" -type f -name "${proj_name}.dll" 2>/dev/null | head -n1 || true)"
  [[ -n "$out" ]] && { echo "$out"; return 0; }

  # Not found
  echo ""
}

# If we were given a hint DLL path that might be missing the RID segment,
# try to rediscover it by searching under the same project/config.
# Usage: resolve_from_dll_hint <dll_hint_path>
resolve_from_dll_hint() {
  local hint="$1"
  # Try to extract project dir + name from common pattern
  if [[ "$hint" =~ (.*)/bin/(Debug|Release)/net9\.0/([^/]+)\.dll ]]; then
    local proj_dir="${BASH_REMATCH[1]}"
    local cfg="${BASH_REMATCH[2]}"
    local name="${BASH_REMATCH[3]}"
    local base="$proj_dir/bin/$cfg"
    local out
    out="$(find "$base" -type f -name "${name}.dll" 2>/dev/null | head -n1 || true)"
    echo "$out"
    return 0
  fi
  echo ""
}

asciiize_html() {
  local html="$1"
  [[ ! -f "$html" ]] && return 0
  if ! grep -qi '<meta[^>]*charset' "$html"; then
    inplace_sed 's/<head>/<head>\n<meta charset="UTF-8">/I' "$html"
  fi
  if ! grep -q 'data-injected-status-css' "$html"; then
    inplace_sed 's|</head>|<style data-injected-status-css>span.success,span.failed,span.skipped{display:inline-block;margin-right:.5em;}</style>\n</head>|I' "$html"
  fi
  perl -0777 -i -pe '
    s/\xC2\xA0/ /g; s/\x{00A0}/ /g; s/Â[[:space:]]/ /g; s/Â//g;
    s/â€”/—/g; s/â€“/–/g; s/â€˜/'"'"'/g; s/â€™/'"'"'/g;
    s/â€œ/"/g; s/â€\x9D/"/g; s/â€\x9c/"/g; s/â€¦/.../g;
  ' "$html"
  if [[ "${WITH_COVERAGE:-false}" == "true" ]]; then
    inplace_sed 's/✓/[OK]/g' "$html"
    inplace_sed 's/✔/[OK]/g' "$html"
    inplace_sed 's/✗/[X]/g'  "$html"
    inplace_sed 's/✘/[X]/g'  "$html"
    inplace_sed 's/✅/[OK]/g' "$html"
    inplace_sed 's/❌/[X]/g'  "$html"
    inplace_sed 's/➖/[-]/g'  "$html"
    perl -0777 -i -pe 's#(<span class="(?:success|failed|skipped)[^"]*">.*?</span>)\s*#\1&nbsp;&nbsp;#g' "$html"
  fi
}

# ---------------- Resolve DLLs from CONFIG (initial hints) ------------------
INT_DLL_HINT="AIMS.Tests.Integration/bin/${CONFIG}/net9.0/AIMS.Tests.Integration.dll"
UNIT_DLL_HINT="AIMS.UnitTests/bin/${CONFIG}/net9.0/AIMS.UnitTests.dll"

# --------- Infer suite from TARGET if obvious ------------------------------
if [[ "$SUITE" == "auto" ]]; then
  if [[ -n "${TARGET:-}" ]]; then
    case "$TARGET" in
      *AIMS.UnitTests.csproj|*AIMS.UnitTests/bin/*/AIMS.UnitTests.dll|*AIMS.UnitTests*)
        SUITE="unit" ;;
      *AIMS.Tests.Integration.csproj|*AIMS.Tests.Integration/bin/*/AIMS.Tests.Integration.dll|*AIMS.Tests.Integration*)
        SUITE="integration" ;;
      *.csproj|*.dll)
        SUITE="integration" ;;
      *)
        if [[ "$TARGET" == AIMS.UnitTests.* ]]; then SUITE="unit"; else SUITE="integration"; fi
        ;;
    esac
  else
    SUITE="integration"
  fi
fi

# --------- Bind TEST_PROJ/TEST_DLL based on suite & TARGET -----------------
TEST_PROJ=""
TEST_DLL=""
MULTI=false

if [[ "$SUITE" == "both" ]]; then
  if [[ -z "${TARGET:-}" ]]; then
    MULTI=true
  elif [[ "$TARGET" == *.dll || "$TARGET" == *.csproj ]]; then
    MULTI=true
  else
    MULTI=false
    if [[ "$TARGET" == AIMS.UnitTests.* ]]; then
      TEST_PROJ="$UNIT_PROJ"
      TEST_DLL="$UNIT_DLL_HINT"  # will be resolved after build
      [[ "$PARALLEL" == "none" ]] && PARALLEL="auto"
    else
      TEST_PROJ="$INT_PROJ"
      TEST_DLL="$INT_DLL_HINT"
    fi
  fi
elif [[ "$SUITE" == "unit" ]]; then
  TEST_PROJ="$UNIT_PROJ"; TEST_DLL="$UNIT_DLL_HINT"
  [[ "$PARALLEL" == "none" ]] && PARALLEL="auto"
else
  TEST_PROJ="$INT_PROJ"; TEST_DLL="$INT_DLL_HINT"
fi

# If TARGET is an explicit .csproj/.dll outside our defaults, rebind to it.
if [[ -n "${TARGET:-}" ]]; then
  case "$TARGET" in
    *.csproj)
      TEST_PROJ="$TARGET"; TEST_DLL="";;
    *.dll)
      TEST_DLL="$TARGET"
      # Try to infer a nearby csproj
      if [[ "$TEST_DLL" =~ (.*)/bin/(Debug|Release)/net9\.0/([^/]+)\.dll ]]; then
        guess_proj_dir="${BASH_REMATCH[1]}"
        guess_proj_name="${BASH_REMATCH[3]}"
        if [[ -f "$guess_proj_dir/${guess_proj_name}.csproj" ]]; then
          TEST_PROJ="$guess_proj_dir/${guess_proj_name}.csproj"
        fi
      fi
      ;;
  esac
fi

# ---------------- Artifacts ------------------------------------------------
TS="$(date '+%Y-%m-%d_%H-%M-%S')"
OUT_ROOT="TestResults/$TS"
OUT_HTML="$OUT_ROOT/TestResults.html"
OUT_TRX="$OUT_ROOT/TestResults.trx"
OUT_JUNIT="$OUT_ROOT/TestResults.junit.xml"
OUT_CONSOLE_RAW="$OUT_ROOT/console.raw.txt"
OUT_CONSOLE_CLEAN="$OUT_ROOT/console.txt"
OUT_FAILS_TXT="$OUT_ROOT/failures.txt"
COVERAGE_DIR="$OUT_ROOT/coverage"
mkdir -p "$OUT_ROOT"

# ---------------- Load .env (integration) ----------------------------------
if [[ -f "AIMS.Tests.Integration/.env" ]]; then
  # shellcheck disable=SC2046
  export $(grep -E '^[A-Za-z_][A-Za-z0-9_]*=' AIMS.Tests.Integration/.env | sed 's/#.*//')
fi

# ---------------- Ensure xunit console runner ------------------------------
ensure_xunit() {
  local runner_path
  if ! command -v xunit >/dev/null 2>&1 && [[ ! -x "$HOME/.dotnet/tools/xunit" ]]; then
    echo "Installing xUnit console runner..."
    dotnet tool install --global xunit-cli || true
  fi
  runner_path="$(command -v xunit || echo "$HOME/.dotnet/tools/xunit")"

  # If the tool points to an ancient netcoreapp2.0 payload, upgrade it.
  if strings "$runner_path" 2>/dev/null | grep -q "netcoreapp2.0"; then
    echo "Upgrading xunit global tool (legacy netcoreapp2.0 detected)..."
    dotnet tool update --global xunit-cli || dotnet tool install --global xunit-cli || true
  fi

  echo "$(command -v xunit || echo "$HOME/.dotnet/tools/xunit")"
}

RUNNER="$(ensure_xunit)"

# ---------------- Build / Restore -----------------------------------------
RESTORE_ARGS=()
[[ "$NO_RESTORE" == "true" ]] || RESTORE_ARGS+=(restore --nologo)
[[ "${#RESTORE_ARGS[@]}" -gt 0 ]] && dotnet "${RESTORE_ARGS[@]}"

if [[ "$MULTI" == "true" ]]; then
  if [[ "$NO_BUILD" != "true" ]]; then
    dotnet build --nologo -c "$CONFIG" "$INT_PROJ"
    dotnet build --nologo -c "$CONFIG" "$UNIT_PROJ"
  fi
else
  if [[ -n "${TEST_PROJ:-}" && "$NO_BUILD" != "true" ]]; then
    dotnet build --nologo -c "$CONFIG" "$TEST_PROJ"
  fi
fi

# ---------------- Recalculate DLLs (RID-aware) -----------------------------
INT_DLL=""
UNIT_DLL=""

if [[ "$MULTI" == "true" ]]; then
  INT_DLL="$(resolve_dll_from_proj "$INT_PROJ" "$CONFIG")"
  UNIT_DLL="$(resolve_dll_from_proj "$UNIT_PROJ" "$CONFIG")"
  if [[ -z "$INT_DLL" || -z "$UNIT_DLL" ]]; then
    echo "ERROR: Unable to locate test DLL(s):"
    echo "  INT:  $(resolve_dll_from_proj "$INT_PROJ" "$CONFIG" || true)"
    echo "  UNIT: $(resolve_dll_from_proj "$UNIT_PROJ" "$CONFIG" || true)"
    exit 3
  fi
else
  if [[ -z "${TEST_DLL:-}" ]]; then
    # Resolve from project
    if [[ -n "${TEST_PROJ:-}" ]]; then
      TEST_DLL="$(resolve_dll_from_proj "$TEST_PROJ" "$CONFIG")"
    fi
  elif [[ ! -f "$TEST_DLL" ]]; then
    # Try to recover from hint path without RID
    recovered="$(resolve_from_dll_hint "$TEST_DLL")"
    [[ -n "$recovered" ]] && TEST_DLL="$recovered"
  fi

  if [[ -z "${TEST_DLL:-}" || ! -f "$TEST_DLL" ]]; then
    echo "ERROR: Could not find test DLL. Looked for something like:"
    if [[ -n "${TEST_PROJ:-}" ]]; then
      echo "  $(dirname "$TEST_PROJ")/bin/$CONFIG/net9.0/**/$(basename "$TEST_PROJ" .csproj).dll"
    else
      echo "  $TEST_DLL"
    fi
    echo "Make sure the build succeeded and the framework is net9.0."
    exit 4
  fi
fi

# ---------------- Compose runner args --------------------------------------
RUN_ARGS=()
if [[ "$MULTI" == "true" ]]; then
  RUN_ARGS=( "$INT_DLL" "$UNIT_DLL" -nologo -html "$OUT_HTML" -xml "$OUT_JUNIT" )
else
  RUN_ARGS=( "${TEST_DLL}" -nologo -html "$OUT_HTML" -xml "$OUT_JUNIT" )
  if [[ -n "${TARGET:-}" && "$TARGET" != *.dll && "$TARGET" != *.csproj ]]; then
    if [[ "$USE_METHOD_FILTER" == "true" ]]; then
      RUN_ARGS+=( -method "$TARGET" )
    else
      RUN_ARGS+=( -class "$TARGET" )
    fi
  fi
fi

# only add -parallel if user explicitly set something other than 'auto'
if [[ "$PARALLEL" != "auto" ]]; then
  RUN_ARGS+=( -parallel "$PARALLEL" )
fi

[[ -n "$USE_TRAIT_FILTER" ]] && RUN_ARGS+=( -trait "$USE_TRAIT_FILTER" )
[[ -n "$USE_NOTRAIT_FILTER" ]] && RUN_ARGS+=( -notrait "$USE_NOTRAIT_FILTER" )

# ---------------- Emoji ----------------------------------------------------
ICON_OK="✅"
ICON_FAIL="❌"
ICON_SKIP="➖"

# ---------------- Execution helpers ---------------------------------------
run_once() {
  local label="${1:-run}"
  echo ">>> Running tests ($(upper "$label")) ..."
  set +e
  "$RUNNER" "${RUN_ARGS[@]}" 2>&1 | tee "$OUT_CONSOLE_RAW"
  local rc=${PIPESTATUS[0]}
  set -e

  asciiize_html "$OUT_HTML"
  sed -E 's/\x1B\[[0-9;]*[mK]//g' "$OUT_CONSOLE_RAW" > "$OUT_CONSOLE_CLEAN"

  echo
  echo "Summary (per test):"
  awk -v ok="$ICON_OK" -v bad="$ICON_FAIL" -v skip="$ICON_SKIP" '
    function trim(s){sub(/^[ \t]+/,"",s);sub(/[ \t]+$/,"",s);return s}
    /^\s*.* \[FAIL\]/ {
      name=$0; sub(/\s+\[FAIL\].*$/,"",name); name=trim(name);
      failed[name]=1; next
    }
    /^\s*.* \[FINISHED\]/ {
      name=$0; sub(/\s+\[FINISHED\].*$/,"",name); name=trim(name);
      if (failed[name]) { printf "%s %s [FAILED]\n", bad, name }
      else              { printf "%s %s\n", ok, $0 }
      next
    }
  ' "$OUT_CONSOLE_CLEAN" || true

  awk '
    BEGIN { inblk=0 }
    /^\s*.* \[FAIL\]/ { inblk=1; name=$0; sub(/\s+\[FAIL\].*$/,"",name); print "\n### " name > "'"$OUT_FAILS_TXT"'"; next }
    /^\s*.* \[FINISHED\]/ { if (inblk) { inblk=0; print $0 "\n" >> "'"$OUT_FAILS_TXT"'"; } next }
    { if (inblk) print >> "'"$OUT_FAILS_TXT"'" }
  ' "$OUT_CONSOLE_CLEAN" || true

  RUNS="$(grep -E '^=== TEST EXECUTION SUMMARY ===' -A1 "$OUT_CONSOLE_CLEAN" | tail -n1 | sed -E 's/^ *//')"
  echo
  [[ -n "${RUNS:-}" ]] && echo "$RUNS"

  if [[ -s "$OUT_FAILS_TXT" ]]; then
    echo
    echo "Failure Details:"
    echo "----------------"
    echo "$ICON_FAIL (expanded stack traces below)"
    echo
    cat "$OUT_FAILS_TXT" || true
    echo
  fi

  return $rc
}

# ---------------- Main run (with optional retry) ---------------------------
rc=0
run_once "initial" || rc=$?

if [[ $rc -ne 0 && $RETRY -gt 0 ]]; then
  echo
  echo "Re-running failed tests once (best-effort)…"
  mapfile -t FAILED < <(grep -E '\[FAIL\]' "$OUT_CONSOLE_CLEAN" | sed -E 's/^\s*//; s/\s+\[FAIL\].*$//' || true)
  if [[ ${#FAILED[@]} -gt 0 ]]; then
    RETRY_HTML="$OUT_ROOT/TestResults.retry.html"
    RETRY_ARGS=()
    if [[ "$MULTI" == "true" ]]; then
      RETRY_ARGS=( "$INT_DLL" "$UNIT_DLL" -nologo -html "$RETRY_HTML" -xml "$OUT_ROOT/TestResults.retry.junit.xml" )
    else
      RETRY_ARGS=( "${TEST_DLL}" -nologo -html "$RETRY_HTML" -xml "$OUT_ROOT/TestResults.retry.junit.xml" )
    fi

    if [[ "$PARALLEL" != "auto" ]]; then
      RETRY_ARGS+=( -parallel "$PARALLEL" )
    fi

    for f in "${FAILED[@]}"; do RETRY_ARGS+=( -method "$f" ); done
    set +e
    "$RUNNER" "${RETRY_ARGS[@]}" 2>&1 | tee "$OUT_ROOT/console.retry.raw.txt"
    rc_retry=${PIPESTATUS[0]}
    set -e
    asciiize_html "$RETRY_HTML"
    [[ $rc_retry -eq 0 ]] && rc=0
  fi
fi

# ---------------- Coverage (coverlet collector) ----------------------------
if [[ "$WITH_COVERAGE" == "true" ]]; then
  echo
  echo "Collecting coverage (Coverlet collector) ..."
  mkdir -p "$COVERAGE_DIR"

  declare -a COV_FILTER_ARGS=()
  if [[ "$MULTI" != "true" ]]; then
    if [[ -n "${TARGET:-}" && "$TARGET" != *.dll && "$TARGET" != *.csproj ]]; then
      COV_FILTER_ARGS+=( --filter "FullyQualifiedName~$TARGET" )
    fi
  fi

  if [[ "$MULTI" == "true" ]]; then
    INT_OUT="$COVERAGE_DIR/int"
    UNIT_OUT="$COVERAGE_DIR/unit"
    mkdir -p "$INT_OUT" "$UNIT_OUT"

    dotnet test "$INT_PROJ" -c "$CONFIG" --no-build \
      --collect:"XPlat Code Coverage" \
      --logger "trx;LogFileName=$(basename "$INT_OUT/coverage.trx")" \
      --results-directory "$INT_OUT" \
      -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura \
         DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile="**/Migrations/*"

    dotnet test "$UNIT_PROJ" -c "$CONFIG" --no-build \
      --collect:"XPlat Code Coverage" \
      --logger "trx;LogFileName=$(basename "$UNIT_OUT/coverage.trx")" \
      --results-directory "$UNIT_OUT" \
      -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura \
         DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile="**/Migrations/*"

    COV_XML_INT="$(find "$INT_OUT" -type f -name 'coverage.cobertura.xml' | head -n1 || true)"
    COV_XML_UNIT="$(find "$UNIT_OUT" -type f -name 'coverage.cobertura.xml' | head -n1 || true)"
    if [[ -z "${COV_XML_INT:-}" && -z "${COV_XML_UNIT:-}" ]]; then
      echo "WARNING: No coverage reports found for either suite."
    else
      if ! command -v reportgenerator >/dev/null 2>&1; then
        echo "INFO: 'reportgenerator' not found; installing local dotnet tool…"
        dotnet tool install --global dotnet-reportgenerator-globaltool >/dev/null 2>&1 || true
      fi
      if command -v reportgenerator >/dev/null 2>&1; then
        COV_HTML_DIR="$OUT_ROOT/coverage_html"
        REPORTS=()
        [[ -n "${COV_XML_INT:-}" ]] && REPORTS+=("$COV_XML_INT")
        [[ -n "${COV_XML_UNIT:-}" ]] && REPORTS+=("$COV_XML_UNIT")
        IFS=';'; REPORTS_JOINED="${REPORTS[*]}"; IFS=' '
        reportgenerator \
          -reports:"$REPORTS_JOINED" \
          -targetdir:"$COV_HTML_DIR" \
          -reporttypes:"HtmlInline_AzurePipelines;TextSummary" \
          -verbosity:Warning

        if [[ -f "$COV_HTML_DIR/Summary.txt" ]]; then
          echo
          echo "Coverage Summary:"
          cat "$COV_HTML_DIR/Summary.txt"
        fi

        # Inject link to coverage into the test HTML even when tests failed
        if [[ -f "$OUT_HTML" ]]; then
          TMP_HTML="$OUT_HTML.tmp"
          {
            echo '<div style="padding:10px;margin:10px 0;border:1px solid #ccc;background:#fafafa;font-family:sans-serif">'
            echo '  <strong>Coverage:</strong> <a href="coverage_html/index.html">Open HTML report</a>'
            echo '</div>'
            cat "$OUT_HTML"
          } > "$TMP_HTML" && mv "$TMP_HTML" "$OUT_HTML"
          asciiize_html "$OUT_HTML"
        fi

        echo "Coverage HTML: $COV_HTML_DIR/index.html"
      else
        echo "NOTE: Could not generate HTML coverage (no reportgenerator)."
        echo "      Install with: dotnet tool install -g dotnet-reportgenerator-globaltool"
      fi
    fi
  else
    # Single suite
    if (( ${#COV_FILTER_ARGS[@]:-0} > 0 )); then
      dotnet test "$TEST_PROJ" -c "$CONFIG" --no-build \
        "${COV_FILTER_ARGS[@]}" \
        --collect:"XPlat Code Coverage" \
        --logger "trx;LogFileName=$(basename "$COVERAGE_DIR/coverage.trx")" \
        --results-directory "$COVERAGE_DIR" \
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura \
           DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile="**/Migrations/*"
    else
      dotnet test "$TEST_PROJ" -c "$CONFIG" --no-build \
        --collect:"XPlat Code Coverage" \
        --logger "trx;LogFileName=$(basename "$COVERAGE_DIR/coverage.trx")" \
        --results-directory "$COVERAGE_DIR" \
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura \
           DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByFile="**/Migrations/*"
    fi

    COV_XML="$(find "$COVERAGE_DIR" -type f -name 'coverage.cobertura.xml' | head -n1 || true)"
    if [[ -z "${COV_XML:-}" ]]; then
      echo "WARNING: No coverage.cobertura.xml found under $COVERAGE_DIR"
      echo "         (Ensure <PackageReference Include=\"coverlet.collector\" /> is in your test csproj)"
    else
      if ! command -v reportgenerator >/dev/null 2>&1; then
        echo "INFO: 'reportgenerator' not found; installing local dotnet tool…"
        dotnet tool install --global dotnet-reportgenerator-globaltool >/dev/null 2>&1 || true
      fi
      if command -v reportgenerator >/dev/null 2>&1; then
        COV_HTML_DIR="$OUT_ROOT/coverage_html"
        reportgenerator \
          -reports:"$COV_XML" \
          -targetdir:"$COV_HTML_DIR" \
          -reporttypes:"HtmlInline_AzurePipelines;TextSummary" \
          -verbosity:Warning

        if [[ -f "$COV_HTML_DIR/Summary.txt" ]]; then
          echo
          echo "Coverage Summary:"
          cat "$COV_HTML_DIR/Summary.txt"
        fi

        # Inject link to coverage into the test HTML even when tests failed
        if [[ -f "$OUT_HTML" ]]; then
          TMP_HTML="$OUT_HTML.tmp"
          {
            echo '<div style="padding:10px;margin:10px 0;border:1px solid #ccc;background:#fafafa;font-family:sans-serif">'
            echo '  <strong>Coverage:</strong> <a href="coverage_html/index.html">Open HTML report</a>'
            echo '</div>'
            cat "$OUT_HTML"
          } > "$TMP_HTML" && mv "$TMP_HTML" "$OUT_HTML"
          asciiize_html "$OUT_HTML"
        fi

        echo "Coverage HTML: $COV_HTML_DIR/index.html"
      else
        echo "NOTE: Could not generate HTML coverage (no reportgenerator)."
        echo "      Install with: dotnet tool install -g dotnet-reportgenerator-globaltool"
      fi
    fi
  fi
fi

# ---------------- Artifacts & open -----------------------------------------
echo
echo "Artifacts:"
echo "  HTML   : $OUT_HTML"
echo "  JUnit  : $OUT_JUNIT"
[[ -f "$OUT_TRX" ]] && echo "  TRX    : $OUT_TRX"
[[ -d "$COVERAGE_DIR" ]] && echo "  Coverage dir: $COVERAGE_DIR"

# open the HTML locally (ignore in CI)
if command -v open >/dev/null 2>&1 && [[ -t 1 ]]; then open "$OUT_HTML" || true; fi

exit $rc