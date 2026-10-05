// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/**
 * The YouTube Data API key is stored server-side but the Providers tab never sent it. These tests prove the
 * controller puts the key on the save request (blank id and secret dropped) and that the state afterwards
 * reports the key as stored, with the value itself never held in state.
 */
class AdminControllerYouTubeKeyTest {

    @Test
    fun saving_a_youtube_api_key_sends_it_and_drops_blank_id_and_secret() = runTest {
        val api = KeyRecordingAdminApi()
        val controller = AdminController(api = api, iamApi = PagedFakePlatformIamApi(), platformAdminApi = PagedFakePlatformAdminApi())

        controller.saveProviderCredential("youtube", clientId = "", clientSecret = "", apiKey = "k-123")

        assertEquals(1, api.saved.size)
        val (provider: String, body: SaveProviderCredentialBody) = api.saved.single()
        assertEquals("youtube", provider)
        assertEquals("k-123", body.apiKey)
        assertNull(body.clientId)
        assertNull(body.clientSecret)
    }

    @Test
    fun a_blank_api_key_is_dropped_and_after_the_save_the_state_shows_the_key_as_stored() = runTest {
        val api = KeyRecordingAdminApi()
        val controller = AdminController(api = api, iamApi = PagedFakePlatformIamApi(), platformAdminApi = PagedFakePlatformAdminApi())
        controller.loadProviders()
        assertEquals("unset", controller.state.value.providerCredentials.single().apiKeySource)

        controller.saveProviderCredential("youtube", clientId = "id-1", clientSecret = "", apiKey = "  ")
        assertNull(api.saved.single().second.apiKey)
        assertEquals("unset", controller.state.value.providerCredentials.single().apiKeySource)

        controller.saveProviderCredential("youtube", clientId = "", clientSecret = "", apiKey = "k-123")
        assertEquals("stored", controller.state.value.providerCredentials.single().apiKeySource)
    }
}

private class KeyRecordingAdminApi(
    private val delegate: AdminApi = PagedOpsFakeAdminApi(),
) : AdminApi by delegate {
    val saved: MutableList<Pair<String, SaveProviderCredentialBody>> = mutableListOf()
    private var keySource: String = "unset"

    override suspend fun getProviderCredentials(): ApiResult<List<ProviderCredential>> =
        ApiResult.Ok(listOf(ProviderCredential(provider = "youtube", apiKeySource = keySource)))

    override suspend fun saveProviderCredential(
        provider: String,
        body: SaveProviderCredentialBody,
    ): ApiResult<ProviderCredential> {
        saved.add(provider to body)
        if (body.apiKey != null) keySource = "stored"
        return ApiResult.Ok(ProviderCredential(provider = provider, apiKeySource = keySource))
    }
}
