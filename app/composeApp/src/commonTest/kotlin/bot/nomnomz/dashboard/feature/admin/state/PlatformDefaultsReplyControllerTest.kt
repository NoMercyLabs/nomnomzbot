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

import bot.nomnomz.dashboard.core.network.BuiltinReplyDefault
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The built-in reply family of the platform-defaults tab (plan item A4): the editor opens on the wording the
 * bot currently uses, ready to edit; any edit drops the old count, the save arms only after a count for exactly
 * the edited wording, it echoes that count, the row becomes the server read-back. The built-in wording is a null
 * template on the wire, whether it came back by the reset or by typing it again, and an empty platform wording
 * can never be saved.
 */
class PlatformDefaultsReplyControllerTest {
    private val uptimeLive = BuiltinReplyDefault(
        builtinKey = "uptime",
        slot = "live",
        shippedTemplate = "{channel} has been live for {uptime}.",
        platformTemplate = null,
        channelsWithOwnReply = 2,
    )

    private suspend fun opened(api: FakePlatformDefaultsApi): PlatformDefaultsController {
        val controller = PlatformDefaultsController(api)
        controller.loadBuiltinReplyDefaults()
        controller.openReplyEdit("uptime", "live")
        return controller
    }

    @Test
    fun the_editor_opens_on_the_shipped_wording_when_no_platform_wording_is_set() = runTest {
        val controller = opened(FakePlatformDefaultsApi(emptyList(), replies = listOf(uptimeLive)))

        val edit: BuiltinReplyDefaultEdit = assertNotNull(controller.state.value.replyEdit)
        assertEquals("{channel} has been live for {uptime}.", edit.template)
        assertNull(edit.change.template, "the shipped wording is a null template on the wire")
        assertFalse(edit.differsFromShipped, "nothing to reset yet")
    }

    @Test
    fun editing_drops_the_count_and_the_save_arms_only_after_a_preview_of_the_edited_wording() = runTest {
        val api = FakePlatformDefaultsApi(emptyList(), replies = listOf(uptimeLive))
        val controller = opened(api)

        controller.editReplyTemplate("Live for {uptime}!")
        assertFalse(assertNotNull(controller.state.value.replyEdit).canSave, "no count for this wording yet")
        assertTrue(assertNotNull(controller.state.value.replyEdit).differsFromShipped)

        controller.previewReplyEdit()
        val edit: BuiltinReplyDefaultEdit = assertNotNull(controller.state.value.replyEdit)
        assertEquals("Live for {uptime}!", api.replyPreviews.single().second.template)
        assertEquals(3, edit.preview?.channelsAffected)
        assertEquals(2, edit.preview?.channelsKeepingOwnSetting)
        assertTrue(edit.canSave)

        controller.editReplyTemplate("Live for {uptime}!!")
        assertNull(controller.state.value.replyEdit?.preview, "a later edit invalidates the count")
    }

    @Test
    fun saving_echoes_the_count_and_the_row_becomes_the_read_back() = runTest {
        val api = FakePlatformDefaultsApi(emptyList(), replies = listOf(uptimeLive))
        val controller = opened(api)
        controller.editReplyTemplate("Live for {uptime}!")
        controller.previewReplyEdit()

        controller.saveReplyEdit()

        val body = api.replySaves.single().second
        assertEquals(3, body.confirmedChannelsAffected)
        assertEquals("Live for {uptime}!", body.template)
        assertEquals("Live for {uptime}!", controller.state.value.replyDefaults.single().platformTemplate)
        assertNull(controller.state.value.replyEdit)
    }

    @Test
    fun the_reset_puts_the_shipped_wording_back_and_clears_the_platform_wording() = runTest {
        val overridden: BuiltinReplyDefault = uptimeLive.copy(platformTemplate = "Live for {uptime}!")
        val api = FakePlatformDefaultsApi(emptyList(), replies = listOf(overridden))
        val controller = opened(api)
        assertEquals("Live for {uptime}!", assertNotNull(controller.state.value.replyEdit).template)

        controller.resetReplyToShipped()
        assertEquals("{channel} has been live for {uptime}.", assertNotNull(controller.state.value.replyEdit).template)
        controller.previewReplyEdit()
        controller.saveReplyEdit()

        assertNull(api.replySaves.single().second.template)
        assertNull(controller.state.value.replyDefaults.single().platformTemplate)
    }

    @Test
    fun typing_the_shipped_wording_again_saves_it_as_the_shipped_wording() = runTest {
        val overridden: BuiltinReplyDefault = uptimeLive.copy(platformTemplate = "Live for {uptime}!")
        val api = FakePlatformDefaultsApi(emptyList(), replies = listOf(overridden))
        val controller = opened(api)

        controller.editReplyTemplate("  {channel} has been live for {uptime}. ")
        controller.previewReplyEdit()
        controller.saveReplyEdit()

        assertNull(api.replySaves.single().second.template, "the same words never pin a platform copy")
    }

    @Test
    fun an_empty_platform_wording_can_never_be_saved() = runTest {
        val api = FakePlatformDefaultsApi(emptyList(), replies = listOf(uptimeLive))
        val controller = opened(api)
        controller.editReplyTemplate("   ")
        controller.previewReplyEdit()

        controller.saveReplyEdit()

        assertTrue(api.replySaves.isEmpty())
        val edit: BuiltinReplyDefaultEdit = assertNotNull(controller.state.value.replyEdit)
        assertTrue(edit.missingText)
        assertFalse(edit.canSave)
    }

    @Test
    fun without_a_shipped_wording_an_empty_box_means_the_commands_own_line() = runTest {
        val ownLine: BuiltinReplyDefault =
            uptimeLive.copy(shippedTemplate = null, platformTemplate = "Live for {uptime}!")
        val api = FakePlatformDefaultsApi(emptyList(), replies = listOf(ownLine))
        val controller = opened(api)

        controller.resetReplyToShipped()
        val edit: BuiltinReplyDefaultEdit = assertNotNull(controller.state.value.replyEdit)
        assertEquals("", edit.template)
        assertFalse(edit.missingText)
        controller.previewReplyEdit()
        controller.saveReplyEdit()

        assertNull(api.replySaves.single().second.template)
    }
}
