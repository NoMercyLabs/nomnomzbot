// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.emoji.state

import bot.nomnomz.dashboard.core.emoji.EmojiStyleStore
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// Proves the emoji-style holder's behavior — the consequences the dashboard relies on: the chosen style is
// PERSISTED through the store (so it survives restart) and the live [current] selection drives App.kt's
// `emojiColor`. These assert the resulting STATE (current style + its resolved token) and the SIDE EFFECT
// (what was written to the store), not surface calls — picking the default Color must actually CLEAR the
// persisted override, not write a sentinel.
class EmojiStyleControllerTest {

    @Test
    fun loads_the_persisted_style_on_init() {
        // The store already holds a monochrome override from a previous run — the controller must come up mono.
        val store = FakeEmojiStyleStore(initial = "monochrome")

        val controller = EmojiStyleController(store)

        assertEquals(EmojiStyle.Monochrome, controller.current.value)
        assertEquals("monochrome", controller.current.value.token)
    }

    @Test
    fun selecting_monochrome_persists_the_token_and_flips_current_to_monochrome() {
        val store = FakeEmojiStyleStore(initial = null)
        val controller = EmojiStyleController(store)

        controller.select(EmojiStyle.Monochrome)

        // current flips to Monochrome (the style App.kt maps to emojiColor = false)...
        assertEquals(EmojiStyle.Monochrome, controller.current.value)
        assertEquals("monochrome", controller.current.value.token)
        // ...and the consequence: the token was written to the store, so a restart comes up monochrome.
        assertEquals("monochrome", store.written)
    }

    @Test
    fun selecting_color_clears_the_persisted_override() {
        // Start from a forced monochrome override so clearing is observable.
        val store = FakeEmojiStyleStore(initial = "monochrome")
        val controller = EmojiStyleController(store)
        assertEquals(EmojiStyle.Monochrome, controller.current.value)

        controller.select(EmojiStyle.Color)

        // current is the default Color (null token — App.kt then maps emojiColor = true)...
        assertEquals(EmojiStyle.Color, controller.current.value)
        assertNull(controller.current.value.token)
        // ...and the consequence: the override was CLEARED (null written), not left as the old "monochrome".
        assertNull(store.written)
        assertEquals(0, store.persisted)
    }

    @Test
    fun defaults_to_color_when_nothing_is_persisted() {
        // A fresh install — the store has no saved style.
        val store = FakeEmojiStyleStore(initial = null)

        val controller = EmojiStyleController(store)

        assertEquals(EmojiStyle.Color, controller.current.value)
        assertNull(controller.current.value.token)
    }
}

// A store backed by an in-memory token, recording what was last written so a test can assert the exact
// persistence consequence (a token for a forced style, `null` to clear). A `null` write models the real
// stores deleting the file / removing the localStorage key.
private class FakeEmojiStyleStore(initial: String?) : EmojiStyleStore {
    var written: String? = initial
        private set

    // Holds the current persisted token the same way the real stores do; absence (`null`) = default Color.
    private var value: String? = initial

    override fun read(): String? = value

    override fun write(token: String?) {
        written = token
        value = token
    }

    // The count of non-null persisted tokens currently held — proves clearing actually removed the override
    // rather than overwriting it with a value.
    val persisted: Int
        get() = if (value == null) 0 else 1
}
