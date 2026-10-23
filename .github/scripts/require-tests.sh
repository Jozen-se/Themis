#!/usr/bin/env bash
# Zero-test guard (RR-41, RR-84): every test category runs in the projects the map below names, each (category,
# project) pair into its own results directory <results-root>/<project>, and a pair whose xunit TRX files hold no
# executed test fails the job, so a category cannot pass because one of its projects ran nothing. A hygiene test
# (RepositoryHygieneTests) fails when a test project in the solution, or a Category value in use, is missing here.
# Usage: require-tests.sh <category> <results-root> [<minimum executed tests per project, default 1>]
#        require-tests.sh --projects <category>    prints the category's projects, one per line
set -euo pipefail

projects_of() {
  case "$1" in
    Architecture) echo "Themis.Architecture.Tests" ;;
    Unit) echo "Themis.Kernel.Tests" ;;
    Integration) echo "Themis.Integration.Tests" ;;
    Invariant) echo "Themis.Integration.Tests Themis.Invariants.Tests" ;;
    *) echo "::error title=Unknown test category::'$1' is not in the category map of require-tests.sh (RR-84)." >&2; return 1 ;;
  esac
}

if [ "${1:-}" = "--projects" ]; then
  list=$(projects_of "${2:?category}")
  for project in $list; do echo "$project"; done
  exit 0
fi

category="${1:?category}"
root="${2:?results root}"
minimum="${3:-1}"
projects=$(projects_of "$category")

failed=0
for project in $projects; do
  dir="$root/$project"
  if [ ! -d "$dir" ]; then
    echo "::error title=No test results::$category in $project wrote no results to $dir (RR-41, RR-84)."
    failed=1
    continue
  fi

  executed=0
  files=0
  while IFS= read -r -d '' file; do
    files=$((files + 1))
    count=$(grep -o 'executed="[0-9]*"' "$file" | head -n 1 | grep -o '[0-9]*' || echo 0)
    executed=$((executed + ${count:-0}))
  done < <(find "$dir" -name '*.trx' -print0)

  echo "$category in $project: $files result file(s) in $dir, $executed executed test(s)."
  if [ "$files" -eq 0 ] || [ "$executed" -lt "$minimum" ]; then
    echo "::error title=Zero tests executed::$category in $project executed $executed test(s) across $files file(s); a pair that runs no tests fails the job (RR-41, RR-84)."
    failed=1
  fi
done

exit "$failed"
