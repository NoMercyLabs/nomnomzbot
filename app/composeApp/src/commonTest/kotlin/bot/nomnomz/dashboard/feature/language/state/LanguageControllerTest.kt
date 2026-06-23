// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.language.state

import bot.nomnomz.dashboard.core.i18n.LanguageStore
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// Proves the display-language holder's behavior — the consequences the dashboard relies on: the chosen
// language is PERSISTED through the store (so it survives restart) and the live [current] selection
// drives App.kt's locale tag. These assert the resulting STATE (current language + its resolved tag) and
// the SIDE EFFECT (what was written to the store), not surface calls — picking System default must
// actually CLEAR the persisted override, not write a sentinel.
class LanguageControllerTest {

    @Test
    fun loads_the_persisted_language_on_init() {
        // The store already holds a Dutch override from a previous run — the controller must come up Dutch.
        val store = FakeLanguageStore(initial = "nl")

        val controller = LanguageController(store)

        assertEquals(AppLanguage.Dutch, controller.current.value)
        assertEquals("nl", controller.current.value.tag)
    }

    @Test
    fun selecting_english_persists_the_tag_and_flips_current_to_english() {
        val store = FakeLanguageStore(initial = null)
        val controller = LanguageController(store)

        controller.select(AppLanguage.English)

        // current flips to English (the "en" tag App.kt forces on the AppEnvironment)...
        assertEquals(AppLanguage.English, controller.current.value)
        assertEquals("en", controller.current.value.tag)
        // ...and the consequence: the "en" tag was written to the store, so a restart comes up English.
        assertEquals("en", store.written)
    }

    @Test
    fun selecting_system_default_clears_the_persisted_override() {
        // Start from a forced Dutch override so clearing is observable.
        val store = FakeLanguageStore(initial = "nl")
        val controller = LanguageController(store)
        assertEquals(AppLanguage.Dutch, controller.current.value)

        controller.select(AppLanguage.System)

        // current is System (null tag — App.kt then follows the OS/browser locale)...
        assertEquals(AppLanguage.System, controller.current.value)
        assertNull(controller.current.value.tag)
        // ...and the consequence: the override was CLEARED (null written), not left as the old "nl".
        assertNull(store.written)
        assertEquals(0, store.persisted)
    }

    @Test
    fun defaults_to_system_when_nothing_is_persisted() {
        // A fresh install — the store has no saved language.
        val store = FakeLanguageStore(initial = null)

        val controller = LanguageController(store)

        assertEquals(AppLanguage.System, controller.current.value)
        assertNull(controller.current.value.tag)
    }
}

// A store backed by an in-memory tag, recording what was last written so a test can assert the exact
// persistence consequence (a tag for a forced language, `null` to clear). A `null` write models the
// real stores deleting the file / removing the localStorage key.
private class FakeLanguageStore(initial: String?) : LanguageStore {
    var written: String? = initial
        private set

    // Holds the current persisted tag the same way the real stores do; absence (`null`) = System default.
    private var value: String? = initial

    override fun read(): String? = value

    override fun write(tag: String?) {
        written = tag
        value = tag
    }

    // The count of non-null persisted tags currently held — proves clearing actually removed the override
    // rather than overwriting it with a value.
    val persisted: Int
        get() = if (value == null) 0 else 1
}
