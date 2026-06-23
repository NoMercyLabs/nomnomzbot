// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:OptIn(ExperimentalWasmJsInterop::class)

package bot.nomnomz.dashboard.core.i18n

import androidx.compose.runtime.Composable
import androidx.compose.runtime.ProvidedValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.text.intl.Locale
import kotlin.js.ExperimentalWasmJsInterop

// Web locale override (Compose Multiplatform locale-environment pattern). The browser locale that Compose
// resources read is read-only, so the override is published to `window.__customLocale`; the navigator
// `languages` getter in index.html returns that override when set (BCP-47 form, e.g. `nl-NL`). `null`
// (System default) clears the override and the browser's own locale takes over again.
actual object LocalAppLocale {
    private val local = staticCompositionLocalOf { Locale.current }

    actual val current: String
        @Composable get() = local.current.toString()

    @Composable
    actual infix fun provides(value: String?): ProvidedValue<*> {
        setCustomLocale(value?.replace('_', '-'))
        return local.provides(Locale.current)
    }
}

// Publish (or clear) the override the index.html navigator `languages` hook reads. A `null` clears it so
// the browser's own locale is used again.
private fun setCustomLocale(tag: String?) {
    if (tag == null) {
        clearCustomLocale()
    } else {
        writeCustomLocale(tag)
    }
}

private fun writeCustomLocale(tag: String): Unit = js("{ window.__customLocale = tag; }")

private fun clearCustomLocale(): Unit = js("{ window.__customLocale = null; }")
