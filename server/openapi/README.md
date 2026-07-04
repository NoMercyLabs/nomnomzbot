<!--
  Copyright (c) NoMercy Labs.
  Part of NomNomzBot, licensed under the GNU AGPL v3.0 or later.
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# API contract — server ↔ app schema sync

`v1.json` is a committed snapshot of the backend's OpenAPI document — the **single source of truth** for
every DTO the API returns. It exists so the hand-written Kotlin DTOs in the KMP app can be guarded against
silent drift instead of vetted by eye on every change.

## How drift is caught

The backend C# DTOs (`*Dto`) and the app's typed Kotlin DTOs are written by hand on both sides. When a C#
DTO gains, loses, or renames a field, the Kotlin DTO would normally drift out of sync **silently** — the
mismatched field just deserializes to its default, and nothing fails.

The frontend test
[`ApiContractTest`](../../app/composeApp/src/jvmTest/kotlin/bot/nomnomz/dashboard/core/network/ApiContractTest.kt)
closes that gap. For every typed DTO it checks the *serialized field names* (from the
kotlinx.serialization descriptor — the exact names that go on the wire) against this document's matching
schema. A Kotlin field that no longer exists on the backend schema **fails the build** — that is the
dangerous case (a renamed/removed backend field the client would now read as a default). A backend field the
Kotlin DTO omits is allowed: the client deliberately reads a subset (`ApiClient`'s JSON ignores unknown keys),
so adding a backend field never breaks the app.

Adding a typed response DTO? Add one line to the `contracts` list in `ApiContractTest`. It is then guarded
forever — no manual review of field lists, ever again.

## Refreshing the snapshot

Regenerate this file whenever you change a DTO or add an endpoint, then commit it — the diff makes the
contract change reviewable, and the frontend test re-checks the app against it:

With the API running (`http://localhost:5080`), from the repo root:

**Linux / macOS**

```bash
curl -s http://localhost:5080/openapi/v1.json -o server/openapi/v1.json
```

**Windows (PowerShell)**

```powershell
Invoke-WebRequest http://localhost:5080/openapi/v1.json -OutFile server\openapi\v1.json
```

If a DTO change lands without refreshing this snapshot, `ApiContractTest` keeps checking the app against the
*old* contract — so refreshing is part of the same vertical slice as the DTO change, exactly like updating a
test. The drift check itself is automatic; only the regenerate-and-commit step is a deliberate one-liner.
