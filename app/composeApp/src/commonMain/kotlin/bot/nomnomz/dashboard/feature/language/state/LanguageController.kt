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
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The display-language picker's state-holder (frontend.md §4 — a plain StateFlow holder, not a
// ViewModel, matching ConnectController / SetupController). It owns the operator's chosen UI language
// and persists it across restarts via the injected [LanguageStore].
//
// On construction it LOADS the persisted tag and maps it back to an [AppLanguage] so the dashboard
// comes up in the language the operator last forced. [select] writes the new choice through the store
// (System default clears it) and flips [current], which App.kt resolves to a tag and feeds into the
// AppEnvironment — re-rendering every `stringResource` live, with no restart.
class LanguageController(private val store: LanguageStore) {

    private val _current: MutableStateFlow<AppLanguage> = MutableStateFlow(AppLanguage.from(store.read()))

    /** The active display-language choice the picker renders and App.kt resolves to a locale tag. */
    val current: StateFlow<AppLanguage> = _current.asStateFlow()

    /** Persist the chosen language (System default clears the stored override) and apply it live. */
    fun select(language: AppLanguage) {
        store.write(language.tag)
        _current.value = language
    }
}

// The three display-language options the picker offers, each mapped to the locale tag the
// AppEnvironment forces. [System] carries a `null` tag — follow the OS/browser locale.
enum class AppLanguage(val tag: String?) {
    System(null),
    English("en"),
    Dutch("nl"),
    ;

    companion object {
        // Map a persisted tag back to its option; an unknown/blank tag falls back to System default so a
        // stale or hand-edited preference can never leave the picker in an invalid state.
        fun from(tag: String?): AppLanguage = entries.firstOrNull { it.tag == tag } ?: System
    }
}
