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
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The emoji-style picker's state-holder (frontend.md §4 — a plain StateFlow holder, not a ViewModel,
// matching LanguageController / ConnectController). It owns the operator's chosen emoji rendering style
// and persists it across restarts via the injected [EmojiStyleStore].
//
// On construction it LOADS the persisted token and maps it back to an [EmojiStyle] so the dashboard comes
// up in the last-chosen style. [select] writes the new choice through the store (the default color style
// clears it) and flips [current], which App.kt maps to `emojiColor` and feeds into NomNomzTheme — swapping
// which bundled emoji font the whole type scale uses, live, with no restart.
class EmojiStyleController(private val store: EmojiStyleStore) {

    private val _current: MutableStateFlow<EmojiStyle> = MutableStateFlow(EmojiStyle.from(store.read()))

    /** The active emoji-style choice the picker renders and App.kt maps to NomNomzTheme's `emojiColor`. */
    val current: StateFlow<EmojiStyle> = _current.asStateFlow()

    /** Persist the chosen style (the default color style clears the stored override) and apply it live. */
    fun select(style: EmojiStyle) {
        store.write(style.token)
        _current.value = style
    }
}

// The two emoji rendering styles the picker offers. [Color] is the default (the app's Twemoji COLR face) and
// carries a `null` token — its ABSENCE from the store means "default", so selecting it clears any override.
// [Monochrome] (the Noto Emoji fallback) is the escape hatch for a browser/Skia build that can't render COLR
// glyphs and would otherwise show colored emoji as boxes.
enum class EmojiStyle(val token: String?) {
    Color(null),
    Monochrome("monochrome"),
    ;

    companion object {
        // Map a persisted token back to its style; an unknown/blank token falls back to the default Color style
        // so a stale or hand-edited preference can never leave the picker in an invalid state.
        fun from(token: String?): EmojiStyle = entries.firstOrNull { it.token == token } ?: Color
    }
}
