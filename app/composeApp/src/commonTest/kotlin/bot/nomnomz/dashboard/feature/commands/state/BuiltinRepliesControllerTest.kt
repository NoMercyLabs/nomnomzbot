// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.state

import bot.nomnomz.dashboard.core.network.BuiltinReply
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// The reply editor's state-holder against a catalogue that behaves like the backend: saving changes exactly
// the edited slot, a refused template stays in the editor with the reason, and reset brings the default back.
class BuiltinRepliesControllerTest {
    private fun BuiltinRepliesState.reply(key: String, slot: String): BuiltinReply =
        groups.first { it.builtinKey == key }.replies.first { it.slot == slot }

    @Test
    fun open_lists_only_the_requested_group_and_the_bot_group_lists_commandless_groups() = runTest {
        val controller = BuiltinRepliesController(PrimaryChannelOnly(), InMemoryReplyCatalogue())

        controller.open("sr")
        assertEquals(listOf("sr"), controller.state.value.visibleGroups.map { it.builtinKey })
        assertFalse(controller.state.value.loading)

        controller.open(BOT_REPLIES_GROUP)
        assertEquals(listOf("system"), controller.state.value.visibleGroups.map { it.builtinKey })
    }

    @Test
    fun saving_an_edit_changes_that_slot_only_and_closes_the_editor() = runTest {
        val api = InMemoryReplyCatalogue()
        val controller = BuiltinRepliesController(PrimaryChannelOnly(), api)
        controller.open("sr")

        controller.startEdit(controller.state.value.reply("sr", "duplicate"))
        controller.editTemplate("Hold on,")
        controller.insertVariable("requested.by")
        controller.save()

        val state: BuiltinRepliesState = controller.state.value
        assertEquals(listOf("set sr/duplicate=Hold on, {requested.by}"), api.writes)
        assertNull(state.editing)
        assertEquals("Hold on, {requested.by}", state.reply("sr", "duplicate").effectiveTemplate)
        assertTrue(state.reply("sr", "duplicate").isOverridden)
        assertEquals("Added {track.name} to the queue.", state.reply("sr", "added").effectiveTemplate)
        assertFalse(state.reply("sr", "added").isOverridden)
    }

    @Test
    fun a_refused_template_keeps_the_editor_open_with_the_reason_and_writes_nothing() = runTest {
        val api = InMemoryReplyCatalogue()
        val controller = BuiltinRepliesController(PrimaryChannelOnly(), api)
        controller.open("sr")

        controller.startEdit(controller.state.value.reply("sr", "added"))
        controller.editTemplate("Added {track.nmae}")
        controller.save()

        val edit: ReplyEdit = assertNotNull(controller.state.value.editing)
        assertEquals("Added {track.nmae}", edit.template)
        assertTrue(edit.error!!.contains("track.nmae"))
        assertFalse(edit.saving)
        assertTrue(api.writes.isEmpty())
    }

    @Test
    fun reset_brings_the_default_back() = runTest {
        val api = InMemoryReplyCatalogue()
        val controller = BuiltinRepliesController(PrimaryChannelOnly(), api)
        controller.open("sr")
        controller.startEdit(controller.state.value.reply("sr", "added"))
        controller.editTemplate("Mine: {track.name}")
        controller.save()

        controller.reset(controller.state.value.reply("sr", "added"))

        val added: BuiltinReply = controller.state.value.reply("sr", "added")
        assertEquals("Added {track.name} to the queue.", added.effectiveTemplate)
        assertFalse(added.isOverridden)
        assertEquals("reset sr/added", api.writes.last())
    }

    @Test
    fun the_preview_fills_every_variable_with_its_sample() = runTest {
        val controller = BuiltinRepliesController(PrimaryChannelOnly(), InMemoryReplyCatalogue())
        controller.open("sr")

        val added: BuiltinReply = controller.state.value.reply("sr", "added")

        assertEquals("Queued Never Gonna Give You Up!", previewReply("Queued {track.name}!", added))
    }
}
