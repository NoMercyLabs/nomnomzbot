// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.attention.ui

import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import kotlin.test.Test
import kotlin.test.assertEquals
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.attention_integration_decrypt_failed_message

// The server raises an integration_token_dead item with its own message key when a stored token can no longer
// be decrypted. The inbox must show that explanation, not drop the line as an unknown key.
class AttentionIntegrationMessageTest {

    @Test
    fun an_undecryptable_token_item_maps_to_its_own_explanation() {
        val item =
            ActionRequiredItem(
                kind = "integration_token_dead",
                severity = "critical",
                titleKey = "attention_integration_reauth_title",
                messageKey = "attention_integration_decrypt_failed_message",
                parameters = mapOf("provider" to "spotify", "failureCount" to "0"),
                deepLinkRoute = "integrations",
            )

        assertEquals(
            AttentionText(Res.string.attention_integration_decrypt_failed_message),
            attentionMessageOf(item),
        )
    }
}
