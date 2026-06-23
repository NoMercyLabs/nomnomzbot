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
import androidx.compose.runtime.ProvidedValue
import androidx.compose.runtime.staticCompositionLocalOf
import java.util.Locale

// Desktop locale override (Compose Multiplatform locale-environment pattern). Compose resources resolve
// against the JVM default Locale, so the override swaps `Locale.setDefault(...)` to the chosen language
// (or restores the original when the user picks System default). The original default is captured once on
// first override so System default always returns the true OS locale, never a previously forced one.
actual object LocalAppLocale {
    private var default: Locale? = null
    private val local = staticCompositionLocalOf { Locale.getDefault().toString() }

    actual val current: String
        @Composable get() = local.current

    @Composable
    actual infix fun provides(value: String?): ProvidedValue<*> {
        if (default == null) {
            default = Locale.getDefault()
        }
        val resolved: Locale =
            when (value) {
                null -> default!!
                else -> Locale.forLanguageTag(value)
            }
        Locale.setDefault(resolved)
        return local.provides(resolved.toString())
    }
}
