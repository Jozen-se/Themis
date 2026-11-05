#!/usr/bin/env bash
# Developer Certificate of Origin check (D-36, milestone 1 step 11).
#
# Every commit in the range <base>..<head> (the commits of a pull request) must carry a
#   Signed-off-by: Author Name <author@example.com>
# trailer whose name and e-mail equal the commit's author. Exempt are the commits of the two bots the project uses,
# dependabot[bot] and github-actions[bot] (author name and a GitHub noreply address), and merge commits, which carry
# no contribution of their own (the same exemption the DCO GitHub app applies). Squash commits on main are never in a
# pull request's range and are therefore not checked, as D-36 says.
#
# Usage: dco-check.sh <base-ref-or-sha> <head-ref-or-sha>
# Exit code 0 when every commit passes, 1 when at least one commit lacks a matching sign-off, 2 on usage errors.
# Runs anywhere git runs; CI calls it from .github/workflows/dco.yml, a contributor can call it locally:
#   .github/scripts/dco-check.sh origin/main HEAD
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <base> <head>" >&2
  exit 2
fi

base="$1"
head="$2"
if ! git rev-parse --verify --quiet "$base^{commit}" >/dev/null || ! git rev-parse --verify --quiet "$head^{commit}" >/dev/null; then
  echo "dco-check: cannot resolve '$base' or '$head' in $(pwd); run inside the repository with the full history fetched." >&2
  exit 2
fi
failures=0
checked=0
exempt=0

is_exempt_bot() {
  local name="$1" email="$2"
  case "$name" in
    'dependabot[bot]' | 'github-actions[bot]')
      [[ "$email" =~ ^[0-9]+\+(dependabot|github-actions)\[bot\]@users\.noreply\.github\.com$ ]]
      ;;
    *) return 1 ;;
  esac
}

# One line per commit: sha, parents, author name, author email, separated by the unit separator (0x1f); the trailers
# are read per commit. RR-142: not by tab, which is IFS whitespace, so a root commit's empty parents field would
# collapse and its two-word author name would be read as two parents, passing the commit as a merge commit.
while IFS=$'\x1f' read -r sha parents name email; do
  parent_count=$(wc -w <<<"$parents")
  if [ "$parent_count" -gt 1 ]; then
    echo "exempt  $sha merge commit"
    exempt=$((exempt + 1))
    continue
  fi
  if is_exempt_bot "$name" "$email"; then
    echo "exempt  $sha $name"
    exempt=$((exempt + 1))
    continue
  fi

  checked=$((checked + 1))
  expected="$name <$email>"
  matched=0
  while IFS= read -r signoff; do
    [ -z "$signoff" ] && continue
    # The e-mail is compared without regard to case, the name exactly.
    if [ "${signoff,,}" = "${expected,,}" ] && [ "${signoff%% <*}" = "$name" ]; then
      matched=1
      break
    fi
  done < <(git log -1 --format='%(trailers:key=Signed-off-by,valueonly,unfold)' "$sha")

  if [ "$matched" -eq 1 ]; then
    echo "ok      $sha $expected"
  else
    echo "MISSING $sha $expected"
    echo "::error title=DCO sign-off missing::Commit $sha by $expected has no 'Signed-off-by: $expected' line (D-36). Amend it with 'git commit --amend -s' or sign off the branch with 'git rebase --signoff'."
    failures=$((failures + 1))
  fi
done < <(git log --format='%H%x1f%P%x1f%an%x1f%ae' --reverse "$base..$head")

echo "DCO: $checked commit(s) checked, $exempt exempt, $failures without a matching sign-off."
if [ "$failures" -gt 0 ]; then
  echo "See CONTRIBUTING.md: every commit needs a Signed-off-by line matching its author (git commit -s)."
  exit 1
fi
