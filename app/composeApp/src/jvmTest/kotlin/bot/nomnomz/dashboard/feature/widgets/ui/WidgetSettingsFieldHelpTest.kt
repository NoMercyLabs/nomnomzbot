// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.LocalizedTextDto
import bot.nomnomz.dashboard.core.network.WidgetSettingsFieldDto
import bot.nomnomz.dashboard.core.network.WidgetSettingsFieldOptionDto
import kotlin.test.Test
import kotlin.test.assertEquals

// A field's help text explains it to the streamer. Every field kind must show it, not only some.
@OptIn(ExperimentalTestApi::class)
class WidgetSettingsFieldHelpTest {
    private val help: String = "Explains this field to the streamer"

    private fun field(type: String, min: Double? = null, max: Double? = null, step: Double? = null) =
        WidgetSettingsFieldDto(
            key = "f",
            label = LocalizedTextDto(text = "Field label"),
            type = type,
            help = LocalizedTextDto(text = help),
            options = listOf(WidgetSettingsFieldOptionDto("a", LocalizedTextDto(text = "Option A"))),
            min = min,
            max = max,
            step = step,
        )

    private fun androidx.compose.ui.test.ComposeUiTest.show(field: WidgetSettingsFieldDto) =
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    FieldControl(
                        field = field,
                        rawValue = "5",
                        onRawChange = {},
                        boolValue = false,
                        onBoolChange = {},
                        selectValue = "a",
                        onSelectChange = {},
                        multiValue = emptyList(),
                        onMultiToggle = {},
                        jsonError = false,
                        invalidJsonText = "",
                    )
                }
            }
        }

    @Test
    fun sliderShowsItsHelp() = runComposeUiTest {
        show(field("number", min = 0.0, max = 10.0, step = 1.0))
        onNodeWithText(help).assertExists()
    }

    @Test
    fun selectShowsItsHelp() = runComposeUiTest {
        show(field("select"))
        onNodeWithText(help).assertExists()
    }

    @Test
    fun multiselectShowsItsHelp() = runComposeUiTest {
        show(field("multiselect"))
        onNodeWithText(help).assertExists()
    }

    @Test
    fun aZeroOrNegativeStepIsContinuousNotBillionsOfStops() {
        assertEquals(0, sliderStepCount(0f, 10f, 0.0))
        assertEquals(0, sliderStepCount(0f, 10f, -1.0))
        assertEquals(9, sliderStepCount(0f, 10f, 1.0))
    }
}
