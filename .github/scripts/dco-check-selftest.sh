#!/usr/bin/env bash
# Proves the DCO check (D-36) on synthetic repositories: a signed commit passes, an unsigned commit fails, a sign-off
# by another identity fails, a sign-off with the e-mail in another case passes, dependabot[bot] and
# github-actions[bot] commits pass without a sign-off, an impostor using a bot's name with a non-GitHub address fails,
# and a merge commit passes without a sign-off. The dispatch wrapper (dco-dispatch.sh, RR-134) derives the base from the
# main branch's full ref, fails an unsigned branch, refuses a short main name and is not shadowed by a tag.
# Run by .github/workflows/dco.yml and by hand:
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

# reset_work <dir>: the work branch starts again at main.
reset_work() {
  git -C "$1" checkout -q main
  git -C "$1" branch -q -D work
  git -C "$1" branch -q work
  git -C "$1" checkout -q work
}

# run_case <case name> <expected exit code> <dir> <command...>: runs the command in <dir> and compares its exit code.
run_case() {
  local name="$1" expected="$2" dir="$3" actual=0
  shift 3
  (cd "$dir" && "$@") >"$work/case.log" 2>&1 || actual=$?
  if [ "$actual" -eq "$expected" ]; then
    echo "pass: $name (exit $actual)"
  else
    echo "FAIL: $name expected exit $expected, got $actual"
    sed 's/^/    /' "$work/case.log"
    failures=$((failures + 1))
  fi
}

# expect <case name> <expected exit code> <dir>: the check over main..work, then the work branch is reset.
expect() {
  run_case "$1" "$2" "$3" "$check" main work
  reset_work "$3"
}

repo="$work/repo"
new_repo "$repo"

commit "$repo" a.txt "Alice Example" "alice@example.com" "Signed" -s
expect "D-36: a signed commit passes" 0 "$repo"

commit "$repo" b.txt "Alice Example" "alice@example.com" "Unsigned"
expect "D-36: an unsigned commit fails" 1 "$repo"

commit "$repo" c.txt "Alice Example" "alice@example.com" "Signed by another identity" --trailer "Signed-off-by: Bob Example <bob@example.com>"
expect "D-36: a sign-off by another identity fails" 1 "$repo"

commit "$repo" d.txt "Alice Example" "alice@example.com" "Signed with the e-mail in another case" --trailer "Signed-off-by: Alice Example <Alice@Example.com>"
expect "D-36: a sign-off whose e-mail differs only in case passes" 0 "$repo"

commit "$repo" e.txt "Alice Example" "alice@example.com" "Signed with another name" --trailer "Signed-off-by: A. Example <alice@example.com>"
expect "D-36: a sign-off with another name fails" 1 "$repo"

commit "$repo" f.txt "dependabot[bot]" "49699333+dependabot[bot]@users.noreply.github.com" "Bump a package"
expect "D-36: a dependabot[bot] commit passes without a sign-off" 0 "$repo"

commit "$repo" g.txt "github-actions[bot]" "41898282+github-actions[bot]@users.noreply.github.com" "Regenerate lock files"
expect "D-36: a github-actions[bot] commit passes without a sign-off" 0 "$repo"

commit "$repo" h.txt "dependabot[bot]" "someone@example.com" "Impostor"
expect "D-36: a commit using a bot's name with another address fails" 1 "$repo"

commit "$repo" i.txt "Alice Example" "alice@example.com" "Signed" -s
commit "$repo" j.txt "Alice Example" "alice@example.com" "Unsigned among signed"
commit "$repo" k.txt "Alice Example" "alice@example.com" "Signed" -s
expect "D-36: one unsigned commit among signed ones fails" 1 "$repo"

# A merge commit without a sign-off: main moves on with a signed commit, work merges it.
commit "$repo" l.txt "Alice Example" "alice@example.com" "Signed on work" -s
git -C "$repo" checkout -q main
commit "$repo" m.txt "Alice Example" "alice@example.com" "Signed on main" -s
git -C "$repo" checkout -q work
git -C "$repo" merge -q --no-ff --no-edit main
expect "D-36: a merge commit passes without a sign-off" 0 "$repo"

# RR-134: the dispatch path. The base is derived as the merge base with the main branch's full ref.
dispatch="$(cd "$(dirname "$0")" && pwd)/dco-dispatch.sh"
drepo="$work/dispatch"
new_repo "$drepo"
commit "$drepo" u.txt "Alice Example" "alice@example.com" "Unsigned"
run_case "RR-134: a dispatch fails an unsigned branch" 1 "$drepo" "$dispatch" "$(git -C "$drepo" rev-parse work)" refs/heads/main
# A tag named like the main branch, pointing at the head: a short name would resolve to the tag and check nothing.
git -C "$drepo" tag main work
git -C "$drepo" tag refs/heads/main work
run_case "RR-134: a short main name is refused, so a tag cannot shadow it" 2 "$drepo" "$dispatch" "$(git -C "$drepo" rev-parse work)" main
run_case "RR-134: the full main ref ignores a tag named after it and still fails the unsigned branch" 1 "$drepo" "$dispatch" "$(git -C "$drepo" rev-parse work)" refs/heads/main
git -C "$drepo" tag -d main refs/heads/main >/dev/null
reset_work "$drepo"
commit "$drepo" s.txt "Alice Example" "alice@example.com" "Signed" -s
git -C "$drepo" checkout -q main
commit "$drepo" n.txt "Alice Example" "alice@example.com" "Main moves on" -s
git -C "$drepo" checkout -q work
run_case "RR-134: a dispatch passes a signed branch after main moved on" 0 "$drepo" "$dispatch" "$(git -C "$drepo" rev-parse work)" refs/heads/main

if [ "$failures" -gt 0 ]; then
  echo "DCO self-test: $failures case(s) failed."
  exit 1
fi
echo "DCO self-test: every case passed."
