#!/usr/bin/env bash
# Proves the DCO check (D-36) on synthetic repositories: a signed commit passes, an unsigned commit fails, a sign-off
# by another identity fails, a sign-off with the e-mail in another case passes, dependabot[bot] and
# github-actions[bot] commits pass without a sign-off, an impostor using a bot's name with a non-GitHub address fails,
# and a merge commit passes without a sign-off. Run by .github/workflows/dco.yml and by hand:
#   .github/scripts/dco-check-selftest.sh
set -euo pipefail

check="$(cd "$(dirname "$0")" && pwd)/dco-check.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
failures=0

# new_repo <dir>: a repository with one base commit on main, authored and signed off by Alice.
new_repo() {
  git init -q -b main "$1"
  git -C "$1" config user.name "Alice Example"
  git -C "$1" config user.email "alice@example.com"
  git -C "$1" config commit.gpgsign false
  echo base > "$1/base.txt"
  git -C "$1" add base.txt
  git -C "$1" commit -q -s -m "Base commit"
  git -C "$1" branch -q work
  git -C "$1" checkout -q work
}

# commit <dir> <file> <name> <email> <message> [extra git commit args...]
commit() {
  local dir="$1" file="$2" name="$3" email="$4" message="$5"
  shift 5
  echo "$RANDOM $file" > "$dir/$file"
  git -C "$dir" add "$file"
  git -C "$dir" -c user.name="$name" -c user.email="$email" commit -q -m "$message" "$@"
}

# expect <case name> <expected exit code> <dir>
expect() {
  local name="$1" expected="$2" dir="$3" actual=0
  (cd "$dir" && "$check" main work) >"$work/$name.log" 2>&1 || actual=$?
  if [ "$actual" -eq "$expected" ]; then
    echo "pass: $name (exit $actual)"
  else
    echo "FAIL: $name expected exit $expected, got $actual"
    sed 's/^/    /' "$work/$name.log"
    failures=$((failures + 1))
  fi
  git -C "$dir" checkout -q main
  git -C "$dir" branch -q -D work
  git -C "$dir" branch -q work
  git -C "$dir" checkout -q work
}

repo="$work/repo"
new_repo "$repo"

commit "$repo" a.txt "Alice Example" "alice@example.com" "Signed" -s
expect "a signed commit passes" 0 "$repo"

commit "$repo" b.txt "Alice Example" "alice@example.com" "Unsigned"
expect "an unsigned commit fails" 1 "$repo"

commit "$repo" c.txt "Alice Example" "alice@example.com" "Signed by another identity" --trailer "Signed-off-by: Bob Example <bob@example.com>"
expect "a sign-off by another identity fails" 1 "$repo"

commit "$repo" d.txt "Alice Example" "alice@example.com" "Signed with the e-mail in another case" --trailer "Signed-off-by: Alice Example <Alice@Example.com>"
expect "a sign-off whose e-mail differs only in case passes" 0 "$repo"

commit "$repo" e.txt "Alice Example" "alice@example.com" "Signed with another name" --trailer "Signed-off-by: A. Example <alice@example.com>"
expect "a sign-off with another name fails" 1 "$repo"

commit "$repo" f.txt "dependabot[bot]" "49699333+dependabot[bot]@users.noreply.github.com" "Bump a package"
expect "a dependabot[bot] commit passes without a sign-off" 0 "$repo"

commit "$repo" g.txt "github-actions[bot]" "41898282+github-actions[bot]@users.noreply.github.com" "Regenerate lock files"
expect "a github-actions[bot] commit passes without a sign-off" 0 "$repo"

commit "$repo" h.txt "dependabot[bot]" "someone@example.com" "Impostor"
expect "a commit using a bot's name with another address fails" 1 "$repo"

commit "$repo" i.txt "Alice Example" "alice@example.com" "Signed" -s
commit "$repo" j.txt "Alice Example" "alice@example.com" "Unsigned among signed"
commit "$repo" k.txt "Alice Example" "alice@example.com" "Signed" -s
expect "one unsigned commit among signed ones fails" 1 "$repo"

# A merge commit without a sign-off: main moves on with a signed commit, work merges it.
commit "$repo" l.txt "Alice Example" "alice@example.com" "Signed on work" -s
git -C "$repo" checkout -q main
commit "$repo" m.txt "Alice Example" "alice@example.com" "Signed on main" -s
git -C "$repo" checkout -q work
git -C "$repo" merge -q --no-ff --no-edit main
expect "a merge commit passes without a sign-off" 0 "$repo"

if [ "$failures" -gt 0 ]; then
  echo "DCO self-test: $failures case(s) failed."
  exit 1
fi
echo "DCO self-test: every case passed."
