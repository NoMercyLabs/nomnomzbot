<!--
  Copyright (c) NoMercy Labs.
  Part of NomNomzBot. You may use, change and share it for free. You may not earn money with it.
  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

# API contract: server and Vue dashboard

The committed [v1.json](v1.json) is a generated snapshot of the server's HTTP contract.
The current client is the Vue dashboard, not the removed Kotlin application.
Its generated types are [dashboard/src/api/schema.d.ts](../../dashboard/src/api/schema.d.ts).
The source controllers and DTOs define behavior; a snapshot can lag behind them.

## Read the contract

No running service is needed to open the snapshot.
A Development instance also serves `/openapi/v1.json` and Scalar at `/scalar`.
Production exposes these only when the operator opts in with `Api:ExposeDocs=true`.
The public read-aloud endpoint is explicitly excluded, and HTTP OpenAPI does not describe the complete WebSocket protocol.
See [Developer documentation](../../docs/developer-guide.md) for those surfaces.

## Refresh after an API change

From the repository root, with the .NET SDK and PowerShell available:

```powershell
pwsh -File scripts/refresh-openapi-build.ps1
npm --prefix dashboard run gen:api
```

Install dashboard dependencies first with `npm ci` from `dashboard/`.
The build-time script temporarily adds the document generator to the API project, runs a build, restores the project file in a finally block and writes the snapshot.
Use it in a checkout without concurrent builds or edits to that project file.
It does not start the bot host or prove that live endpoint behavior works.

The alternative `scripts/refresh-openapi.ps1` starts an API, fetches the served document and stops it.
It is not a read-only download from an already running bot.

Review and submit the snapshot and generated TypeScript together.
CI regenerates the TypeScript and fails if it differs from the committed types.
This catches snapshot/type drift; it does not itself prove the snapshot matches an unrefreshed controller change.
