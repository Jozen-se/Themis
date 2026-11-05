# oasdiff (tool outside the NuGet scan, D-38)

The OpenAPI breaking-change check (RR-10, `.github/workflows/openapi.yml`) runs oasdiff, a Go binary that is not a
NuGet package, so `licence-scan` cannot see it. Its licence was confirmed by hand and is recorded here; the workflow
builds it from source, pinned by version and by the module checksum of the Go checksum database, with the Go
toolchain pinned too. A version change updates the workflow and this record together, after
the licence text of the new version has been read again.

| Field | Value |
| --- | --- |
| Name | oasdiff |
| Source | https://github.com/oasdiff/oasdiff |
| Version | 1.34.1 |
| Release date | 2026-10-21 |
| Licence | Apache-2.0 |
| Pinned module | `github.com/oasdiff/oasdiff@v1.34.1`, checksum `h1:RWfdeDpIo7uR5TXi07P5dEKIQWuds6M22vQq7BWktmg=` (sum.golang.org, read 2026-11-05) in `OASDIFF_MODULE_SUM` of `.github/workflows/openapi.yml`; built with Go `go1.26.10` (`OASDIFF_GO_TOOLCHAIN`) |
| Confirmed | 2026-11-05, by reading the LICENSE file of the v1.34.1 module archive at https://proxy.golang.org/github.com/oasdiff/oasdiff/@v/v1.34.1.zip |

Apache-2.0 is permissive and on `allowed-licenses.json`; oasdiff is run in CI only and is not distributed with Themis.
