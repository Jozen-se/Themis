#!/usr/bin/env bash
# The DCO check of a dispatched run (RR-134, D-36): checks <main>..<head> with dco-check.sh, the commits on the head
# that the main branch does not have (git log computes the set difference). The base is never chosen by the caller, so a
# dispatcher cannot shrink the range (a caller-chosen base equal to the head would check nothing and pass).
#
# The main branch is named by its full ref (refs/...), which must exist as exactly that ref (git show-ref --verify, no
# name lookup) before rev-parse resolves it. git's name lookup tries refs/tags/<name> before refs/remotes/<name>, and
# for a full ref that is absent it still falls back to refs/tags/<full ref>, so a tag with that name would otherwise
# shadow the branch. A name that does not start with refs/ is refused.
#
# Usage: dco-dispatch.sh <head-sha> <main-full-ref>
# Exit code 0 or 1 as dco-check.sh, 2 when the main ref is not a full ref or cannot be resolved.
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
if ! git show-ref --verify --quiet "$main_ref" || ! main_sha="$(git rev-parse --verify --quiet "${main_ref}^{commit}")"; then
  echo "::error title=DCO dispatch::Cannot resolve $main_ref; check out with the full history (RR-134)."
  exit 2
fi
echo "DCO dispatch: checking $main_ref..$head."
exec "$(cd "$(dirname "$0")" && pwd)/dco-check.sh" "$main_sha" "$head"
