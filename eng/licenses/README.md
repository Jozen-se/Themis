# Licence scan (D-38)

The CI job `licence-scan` runs [nuget-license](https://github.com/sensslen/nuget-license) over the whole solution,
including transitive packages, and fails on any package whose licence is not in `allowed-licenses.json`.

- The list holds permissive SPDX identifiers only. Copyleft licences (GPL, AGPL, LGPL, MPL, EPL, CDDL, EUPL, SSPL
  and similar) are never added; D-38 fails the build on any copyleft dependency.
- A package whose metadata carries a licence URL instead of an SPDX expression is reported as unknown and fails the
  scan. Map it in `license-url-mappings.json` only after reading the licence text, and say in the pull request which
  licence it is.
- A package whose licence is correct but cannot be read by the tool goes into `package-overrides.json` with the
  licence identifier and a reason. Prefer fixing the package metadata upstream.

Run locally:

```bash
dotnet tool restore
dotnet nuget-license -i Themis.sln -t -a eng/licenses/allowed-licenses.json -mapping eng/licenses/license-url-mappings.json -override eng/licenses/package-overrides.json -o Table
```
