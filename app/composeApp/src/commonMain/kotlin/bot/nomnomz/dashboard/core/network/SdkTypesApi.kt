// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

// The dev-platform SDK type declarations facade (backend `GET /api/v1/sdk/types.d.ts?context=widget|script`).
// Returns the generated `nnz.d.ts` as raw `text/plain` — the ambient TypeScript declarations for the `nnz.*`
// script/widget SDK. The editor fetches this so a later slice can feed it to an in-browser TypeScript language
// service for autocomplete + inline errors over the SDK surface; this slice wires the fetch so the declarations
// are available, the language-service integration follows.
interface SdkTypesApi {
    /**
     * Fetch the `nnz.d.ts` declarations for [context] — `widget` (the overlay SDK) or `script` (the code-script
     * SDK). The two contexts expose different globals, so the editor requests the one matching what it is editing.
     */
    suspend fun types(context: String): ApiResult<String>
}

class RestSdkTypesApi(private val client: ApiClient) : SdkTypesApi {
    override suspend fun types(context: String): ApiResult<String> =
        client.getText("api/v1/sdk/types.d.ts?context=${context.encodeQuery()}")
}
