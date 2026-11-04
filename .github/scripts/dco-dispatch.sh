#!/usr/bin/env bash
# The DCO check of a dispatched run (RR-134, D-36): checks <base>..<head> with dco-check.sh, after proving that the
# base is a full commit SHA and a commit on <main-ref>. A dispatcher chooses the base, so without the second check it
# could shrink the range (base = head checks nothing and passes); a base on main can only widen it.
#
# Usage: dco-dispatch.sh <base-sha> <head-sha> <main-ref>
# Exit code 0 or 1 as dco-check.sh, 2 when the base is malformed or not on <main-ref>.
# Run by .github/workflows/dco.yml on workflow_dispatch; proved by dco-check-selftest.sh.
set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo "usage: $0 <base-sha> <head-sha> <main-ref>" >&2
  exit 2
fi

base="$1"
head="$2"
main_ref="$3"
if ! [[ "$base" =~ ^[0-9a-f]{40}$ ]]; then
  echo "::error title=DCO dispatch input::The base input must be a 40-character commit SHA (RR-134)."
  exit 2
fi
if ! git merge-base --is-ancestor "$base" "$main_ref" 2>/dev/null; then
  echo "::error title=DCO dispatch input::The base input must be a commit on main, so the range cannot be shrunk (RR-134, D-36)."
  exit 2
fi
exec "$(cd "$(dirname "$0")" && pwd)/dco-check.sh" "$base" "$head"
