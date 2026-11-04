#!/usr/bin/env bash
# The DCO check of a dispatched run (RR-134, D-36): checks <merge-base>..<head> with dco-check.sh, where the base is
# derived, never chosen by the caller: the merge base of the head and the main branch. A dispatcher therefore cannot
# shrink the range (a caller-chosen base equal to the head would check nothing and pass).
#
# The main branch is named by its full ref (refs/...), resolved once with rev-parse --verify: a short name such as
# origin/main is resolved by git's ref rules, which try refs/tags/<name> before refs/remotes/<name>, so a tag with that
# name would shadow the branch. A name that does not start with refs/ is refused.
#
# Usage: dco-dispatch.sh <head-sha> <main-full-ref>
# Exit code 0 or 1 as dco-check.sh, 2 when the main ref is not a full ref or cannot be resolved, or no merge base exists.
# Run by .github/workflows/dco.yml on workflow_dispatch; proved by dco-check-selftest.sh.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <head-sha> <main-full-ref>" >&2
  exit 2
fi

head="$1"
main_ref="$2"
if [[ "$main_ref" != refs/* ]]; then
  echo "::error title=DCO dispatch::The main branch must be named by its full ref (refs/...), not '$main_ref' (RR-134)."
  exit 2
fi
if ! main_sha="$(git rev-parse --verify --quiet "${main_ref}^{commit}")"; then
  echo "::error title=DCO dispatch::Cannot resolve $main_ref; check out with the full history (RR-134)."
  exit 2
fi
if ! base="$(git merge-base "$main_sha" "$head")"; then
  echo "::error title=DCO dispatch::$head has no merge base with $main_ref (RR-134)."
  exit 2
fi
echo "DCO dispatch: checking $base..$head (merge base with $main_ref)."
exec "$(cd "$(dirname "$0")" && pwd)/dco-check.sh" "$base" "$head"
