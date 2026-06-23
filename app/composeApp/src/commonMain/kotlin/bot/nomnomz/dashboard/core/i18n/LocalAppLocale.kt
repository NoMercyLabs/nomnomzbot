// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.i18n

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.ProvidedValue
import androidx.compose.runtime.key

// The runtime display-language override (Compose Multiplatform locale-environment pattern). Forcing the
// UI language regardless of the OS/browser locale lets a Dutch-system streamer pin the dashboard to
// English so their viewers can follow — switched live, with no restart.
//
// [LocalAppLocale] is the per-target seam that rewrites the platform's "current locale" so every
// `stringResource` resolves against the chosen language: on desktop it swaps the JVM default Locale; on
// web it overrides the read-only browser locale via a JS hook. The selected tag (null = System, "en",
// "nl") is held by the LanguageController and fed in through [AppEnvironment].
expect object LocalAppLocale {
    val current: String
        @Composable get

    @Composable
    infix fun provides(value: String?): ProvidedValue<*>
}

// Wraps the whole app tree so a language change re-renders every `stringResource` with no restart. The
// `key(tag)` forces the subtree to recompose when the override flips, so resources re-resolve against the
// new locale immediately. [tag] is the BCP-47-ish language tag (`null` = follow the platform locale).
@Composable
fun AppEnvironment(tag: String?, content: @Composable () -> Unit) {
    CompositionLocalProvider(LocalAppLocale provides tag) {
        key(tag) { content() }
    }
}
