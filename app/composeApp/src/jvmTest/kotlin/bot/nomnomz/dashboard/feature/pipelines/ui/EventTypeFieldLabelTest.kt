// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull

// The field key `event_type` is shared by two steps: `widget_event` (required) and `send_webhook`
// (optional). The editor resolves the label by key only and appends " *" itself for a required field
// (`fieldLabelWithRequired`), so the label text must be neutral. Baked-in "(optional)" would show a
// required widget_event field as optional.
class EventTypeFieldLabelTest {
    private fun label(file: String): String? {
        val text: String = File("src/commonMain/composeResources/$file/strings.xml").readText()
        return Regex("""<string name="pipelines_field_event_type">([^<]*)</string>""")
            .find(text)
            ?.groupValues
            ?.get(1)
    }

    @Test
    fun englishLabelIsNeutralSoRequiredAndOptionalStepsBothRenderCorrectly() {
        assertEquals("Event type", assertNotNull(label("values")))
    }

    @Test
    fun dutchLabelIsNeutralToo() {
        assertEquals("Gebeurtenistype", assertNotNull(label("values-nl")))
    }
}
