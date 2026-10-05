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

import java.io.File
import kotlin.test.Test
import kotlin.test.fail

// S-I18N-NOCHANNELERROR: no controller may carry a hardcoded English "No active channel" literal. Enumerates the
// real source tree (not a hand-typed list), so a controller added tomorrow with its own literal is caught.
class NoChannelErrorLiteralGuardTest {

    @Test
    fun no_controller_declares_a_hardcoded_no_channel_error_literal() {
        val literal: Regex = Regex("""const val NoChannelError\s*:\s*String\s*=""")
        val offenders: List<String> =
            File("src/commonMain/kotlin/bot/nomnomz/dashboard")
                .walkTopDown()
                .filter { it.isFile && it.extension == "kt" }
                .filter { literal.containsMatchIn(it.readText()) }
                .map { it.name }
                .toList()

        if (offenders.isNotEmpty()) fail("Hardcoded NoChannelError literal in: ${offenders.joinToString()}")
    }
}
