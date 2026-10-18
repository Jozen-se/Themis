# Contributing

Themis is built from its design documents (see `CLAUDE.md` and `docs/design/`), one task per pull request, reviewed
by the owner. This page covers what every contributor must do before a pull request can be merged.

## Sign off every commit (Developer Certificate of Origin)

Contributions are accepted under the [Developer Certificate of Origin 1.1](https://developercertificate.org/)
(decision D-36). By signing off a commit you certify that you wrote the change or otherwise have the right to submit
it under the project licence, Apache-2.0.

Sign off with the `-s` flag, which adds a trailer with the name and e-mail of your git identity:

```bash
git commit -s -m "Describe the change"
```

The trailer looks like this and must match the commit's author exactly (the e-mail without regard to case):

```
Signed-off-by: Your Name <you@example.com>
```

Set your identity once with `git config user.name "Your Name"` and `git config user.email "you@example.com"`.

### The check

The workflow `.github/workflows/dco.yml` runs `.github/scripts/dco-check.sh` on every pull request and fails when any
commit of the pull request lacks a matching `Signed-off-by` line. Commits by `dependabot[bot]` and
`github-actions[bot]` and merge commits are exempt. Squash commits on `main` are made by the merge and are not
checked. You can run the same check locally before pushing:

```bash
.github/scripts/dco-check.sh origin/main HEAD
```

### Fixing a missing sign-off

- The last commit: `git commit --amend -s --no-edit`
- Every commit of the branch: `git rebase --signoff origin/main`

Then push the branch again (`git push --force-with-lease` on your own branch).

## Before you open a pull request

- `dotnet format Themis.sln --verify-no-changes`, `dotnet build` (warnings are errors) and `dotnet test` pass locally;
  the integration tests need Docker or Podman, or `THEMIS_TEST_CONNECTION` (see `docs/development.md`).
- Every behaviour traces to a rule or decision in `docs/design/`; a gap goes to the owner as a "Rule gap" issue, never
  a guess (`CLAUDE.md`, Ground rules).
- New dependencies pass the licence scan; no copyleft (D-38).
- Fill in the pull request template; ledger, inventory and posting changes are marked for the owner's explicit review.
