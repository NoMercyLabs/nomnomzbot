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

import io.ktor.client.HttpClient
import io.ktor.client.HttpClientConfig

// The platform engine is the ONLY per-target piece of the REST client (frontend.md §3.1): CIO on
// jvm, Js/Fetch on wasmJs. The shared config (JSON, default request, auth header) is applied once in
// commonMain by [ApiClient] through this factory, which the actuals back with their engine.
expect fun buildHttpClient(config: HttpClientConfig<*>.() -> Unit): HttpClient
