#!/usr/bin/env bash
# Proves the OpenAPI breaking-change check (RR-10) on synthetic repositories built from the committed openapi/v1.json:
# an unchanged document passes; an added path passes; a synthetic breaking change (a path removed, a response property
# removed) fails without the api-breaking label and passes with it; a base commit without openapi/v1.json passes with
# a notice; a document oasdiff cannot read, an unknown base commit, a missing head document and a label value other
# than true or false fail, the label included. The stale-document half of RR-10 is the unit test OpenApiDocumentTests.
# Run by .github/workflows/openapi.yml and by hand, with oasdiff on PATH or named by OASDIFF:
#   OASDIFF=/path/to/oasdiff .github/scripts/openapi-check-selftest.sh
set -euo pipefail

scripts="$(cd "$(dirname "$0")" && pwd)"
check="$scripts/openapi-check.sh"
document="$(cd "$scripts/../.." && pwd)/openapi/v1.json"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
failures=0

if [ ! -f "$document" ]; then
  echo "FAIL: $document is missing; the self-test builds its cases from it (RR-10)."
  exit 1
fi

# mutate <kind> <output>: a copy of the committed document with one change, written by the standard library only.
mutate() {
  python3 -I - "$1" "$document" "$2" <<'PY'
import json
import sys

kind, source, target = sys.argv[1], sys.argv[2], sys.argv[3]
with open(source, encoding="utf-8") as f:
    doc = json.load(f)
paths = doc["paths"]
if kind == "remove-path":
    del paths[sorted(paths)[0]]
elif kind == "remove-response-property":
    # The first object schema with properties that a 200 response of a GET refers to loses its first property.
    schemas = doc["components"]["schemas"]
    for path in sorted(paths):
        get = paths[path].get("get")
        if not get:
            continue
        content = get.get("responses", {}).get("200", {}).get("content", {})
        ref = next(iter(content.values()), {}).get("schema", {}).get("$ref", "")
        name = ref.rsplit("/", 1)[-1]
        if name in schemas and schemas[name].get("properties"):
            properties = schemas[name]["properties"]
            del properties[next(iter(properties))]
            if "required" in schemas[name]:
                schemas[name]["required"] = [p for p in schemas[name]["required"] if p in properties]
            break
    else:
        sys.exit("no GET response schema with properties to mutate")
else:
    sys.exit("unknown mutation " + kind)
with open(target, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2)
PY
}

# repo_with <dir> [<base document>]: a repository whose main commit holds openapi/v1.json, or no document at all.
repo_with() {
  git init -q -b main "$1"
  git -C "$1" config user.name "Alice Example"
  git -C "$1" config user.email "alice@example.com"
  git -C "$1" config commit.gpgsign false
  echo base > "$1/base.txt"
  if [ "$#" -ge 2 ]; then
    mkdir -p "$1/openapi"
    cp "$2" "$1/openapi/v1.json"
  fi
  git -C "$1" add -A
  git -C "$1" commit -q -m "Base commit"
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

mutate remove-path "$work/path-removed.json"
mutate remove-response-property "$work/property-removed.json"
echo '{"openapi": "3.1.1", "paths": ' > "$work/unreadable.json"

committed="$work/committed"
repo_with "$committed" "$document"
added="$work/added"
repo_with "$added" "$work/path-removed.json"
empty="$work/empty"
repo_with "$empty"

run_case "RR-10: an unchanged document passes" 0 "$committed" "$check" main "$document" false
run_case "RR-10: an added path is not breaking and passes" 0 "$added" "$check" main "$document" false
run_case "RR-10: a synthetic breaking change (path removed) fails without the api-breaking label" 1 "$committed" "$check" main "$work/path-removed.json" false
run_case "RR-10: a synthetic breaking change (path removed) passes with the api-breaking label" 0 "$committed" "$check" main "$work/path-removed.json" true
run_case "RR-10: a synthetic breaking change (response property removed) fails without the api-breaking label" 1 "$committed" "$check" main "$work/property-removed.json" false
run_case "RR-10: a synthetic breaking change (response property removed) passes with the api-breaking label" 0 "$committed" "$check" main "$work/property-removed.json" true
run_case "RR-10: a base without openapi/v1.json passes with a notice" 0 "$empty" "$check" main "$document" false
if ! grep -q '^::notice title=OpenAPI check::' "$work/case.log"; then
  echo "FAIL: RR-10: the pass without a base document gives no notice"
  failures=$((failures + 1))
fi
run_case "RR-10: a document oasdiff cannot read fails, also with the label" 2 "$committed" "$check" main "$work/unreadable.json" true
run_case "RR-10: an unknown base commit fails, also with the label" 2 "$committed" "$check" 0000000000000000000000000000000000000000 "$document" true
run_case "RR-10: a missing head document fails, also with the label" 2 "$committed" "$check" main "$work/missing.json" true
run_case "RR-10: a label value other than true or false fails" 2 "$committed" "$check" main "$work/path-removed.json" yes

if [ "$failures" -ne 0 ]; then
  echo "$failures case(s) failed."
  exit 1
fi
echo "All cases passed."
