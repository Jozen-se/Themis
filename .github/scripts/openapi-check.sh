#!/usr/bin/env bash
# OpenAPI breaking-change check (RR-10, Build Brief milestone 2 step 4). Compares the head's OpenAPI document with
# openapi/v1.json of the base commit using oasdiff (pinned by version and SHA-256 in .github/workflows/openapi.yml,
# licence recorded in eng/licenses/oasdiff.md). A breaking change, as oasdiff classifies it at level ERR, fails the
# check unless the pull request carries the api-breaking label, which the owner sets. When the base commit has no
# openapi/v1.json yet there is nothing to compare: the check passes with a notice. A tool error, an unknown base commit
# or a missing head document fails whatever the label says.
#
# oasdiff runs in an empty directory, so no .oasdiff.* configuration from the checkout applies, and with external
# $refs disabled, so a document cannot make it fetch anything.
#
# Usage: openapi-check.sh <base-commit> <head-document> <api-breaking label: true|false>
#        run inside the repository; OASDIFF names the oasdiff binary (default: oasdiff on PATH).
# Exit:  0 pass, 1 breaking change without the label, 2 usage, input or tool error.
# Run by .github/workflows/openapi.yml; proved by openapi-check-selftest.sh.
set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo "usage: $0 <base-commit> <head-document> <api-breaking label: true|false>" >&2
  exit 2
fi

base="$1"
head_document="$2"
label="$3"
oasdiff="${OASDIFF:-oasdiff}"
readonly document_path="openapi/v1.json"

if [ "$label" != "true" ] && [ "$label" != "false" ]; then
  echo "::error title=OpenAPI check::The api-breaking label value must be true or false, not '$label' (RR-10)."
  exit 2
fi
if [ ! -f "$head_document" ]; then
  echo "::error title=OpenAPI check::The head has no $head_document; generate it as docs/development.md describes (RR-10)."
  exit 2
fi
case "$head_document" in
  /*) ;;
  *) head_document="$PWD/$head_document" ;;
esac
if ! base_sha="$(git rev-parse --verify --quiet "${base}^{commit}")"; then
  echo "::error title=OpenAPI check::Cannot resolve the base commit $base; check out with the full history (RR-10)."
  exit 2
fi
if [ -z "$(git ls-tree --name-only "$base_sha" -- "$document_path")" ]; then
  echo "::notice title=OpenAPI check::The base commit $base_sha has no $document_path yet; there is nothing to compare, so the check passes (RR-10)."
  exit 0
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
git show "${base_sha}:${document_path}" > "$work/base.json"
mkdir "$work/run"

echo "OpenAPI check: $document_path of $base_sha against $head_document, api-breaking label: $label."
status=0
(cd "$work/run" && "$oasdiff" breaking "$work/base.json" "$head_document" --fail-on ERR --allow-external-refs=false --color never) || status=$?

case "$status" in
  0)
    echo "No breaking change (RR-10)."
    exit 0
    ;;
  1)
    if [ "$label" = "true" ]; then
      echo "::warning title=OpenAPI breaking change::The document has a breaking change; the api-breaking label accepts it (RR-10)."
      exit 0
    fi
    echo "::error title=OpenAPI breaking change::The document has a breaking change against the base branch; it passes only with the api-breaking label, which the owner sets (RR-10)."
    exit 1
    ;;
  *)
    echo "::error title=OpenAPI check::oasdiff failed with exit code $status; the label does not excuse a tool error (RR-10)."
    exit 2
    ;;
esac
