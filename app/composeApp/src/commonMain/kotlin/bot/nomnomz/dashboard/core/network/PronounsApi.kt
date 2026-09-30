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

/** REST interface for `/api/v1/pronouns/` — pronoun catalog + viewer self-service. */
interface PronounsApi {
    /** Anonymous catalog from the dedicated endpoint (includes `key` field for alejo matching). */
    suspend fun catalog(): ApiResult<List<PronounOption>>

    /** The authenticated viewer's current pronoun state. */
    suspend fun getMyPronouns(): ApiResult<UserPronounResponse>

    /** Set or clear the authenticated viewer's pronouns. */
    suspend fun setMyPronouns(body: SetPronounBody): ApiResult<UserPronounResponse>
}

internal class PronounsApiImpl(private val client: ApiClient) : PronounsApi {
    override suspend fun catalog(): ApiResult<List<PronounOption>> =
        client.getEnvelope("api/v1/pronouns/catalog")

    override suspend fun getMyPronouns(): ApiResult<UserPronounResponse> =
        client.getEnvelope("api/v1/pronouns/me")

    override suspend fun setMyPronouns(body: SetPronounBody): ApiResult<UserPronounResponse> =
        client.putEnvelope("api/v1/pronouns/me", body)
}
