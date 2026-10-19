#!/usr/bin/env bash
# Zero-test guard (RR-41): a test category whose results hold no executed test fails the job. Reads the xunit TRX
# files a `dotnet test ... --report-xunit-trx --results-directory <dir>` step wrote and sums their executed counters.
# Usage: require-tests.sh <results-directory> [<minimum executed tests, default 1>]
set -euo pipefail

dir="${1:?results directory}"
minimum="${2:-1}"
if [ ! -d "$dir" ]; then
  echo "::error title=No test results::$dir does not exist; the test step wrote no results (RR-41)."
  exit 1
fi

executed=0
files=0
while IFS= read -r -d '' file; do
  files=$((files + 1))
  count=$(grep -o 'executed="[0-9]*"' "$file" | head -n 1 | grep -o '[0-9]*' || echo 0)
  executed=$((executed + ${count:-0}))
done < <(find "$dir" -name '*.trx' -print0)

echo "$files result file(s) in $dir, $executed executed test(s)."
if [ "$files" -eq 0 ] || [ "$executed" -lt "$minimum" ]; then
  echo "::error title=Zero tests executed::$dir holds $executed executed test(s) across $files file(s); a category that runs no tests fails the job (RR-41)."
  exit 1
fi
