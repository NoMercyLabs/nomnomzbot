// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import bot.nomnomz.dashboard.core.network.PlatformTemplateJson
import bot.nomnomz.dashboard.core.network.RewardTemplatePayload
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Proves the admin reward-template form writes exactly the payload the server's RewardTemplatePayload reads:
// blank optional fields are left unset (not zero), and a saved draft reads back into the same form.
class AdminContentRewardAuthoringTest {

    @Test
    fun the_form_writes_the_server_payload_and_leaves_blank_limits_unset() {
        val fields =
            RewardTemplateFields(
                title = " Hydrate ",
                cost = "500",
                prompt = "Make me drink",
                backgroundColor = "#00AAFF",
                globalCooldownSeconds = "300",
            )

        val payload: RewardTemplatePayload =
            PlatformTemplateJson.decodeFromString(RewardTemplatePayload.serializer(), fields.toPayloadJson())

        assertEquals("Hydrate", payload.title)
        assertEquals(500, payload.cost)
        assertEquals("Make me drink", payload.prompt)
        assertNull(payload.response)
        assertEquals("#00AAFF", payload.backgroundColor)
        assertEquals(300, payload.globalCooldownSeconds)
        assertNull(payload.maxPerStream)
        assertNull(payload.maxPerUserPerStream)
        assertNull(payload.timerDurationSeconds)
    }

    @Test
    fun a_saved_draft_reads_back_into_the_same_form() {
        val original =
            RewardTemplateFields(title = "Hydrate", cost = "500", maxPerStream = "10", isUserInputRequired = true)

        assertEquals(original, RewardTemplateFields.fromPayloadJson(original.toPayloadJson()))
    }

    @Test
    fun a_reward_needs_a_title_and_a_cost_of_at_least_one() {
        assertTrue(RewardTemplateFields(title = "Hydrate", cost = "1").isComplete())
        assertFalse(RewardTemplateFields(title = "Hydrate", cost = "0").isComplete())
        assertFalse(RewardTemplateFields(title = " ", cost = "10").isComplete())
    }
}
