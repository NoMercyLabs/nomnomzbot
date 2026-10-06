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

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.SemanticsNodeInteraction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.performTextInputSelection
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.text.TextRange
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlockField
import bot.nomnomz.dashboard.core.network.BlockRole
import bot.nomnomz.dashboard.core.network.PaletteBlock
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.RuntimePalette
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.feature.pipelines.state.EditorOptions
import bot.nomnomz.dashboard.feature.pipelines.state.VariableScope
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * The "if" and the loop's "while" dialogs wire the `{` variable list into their condition template field, the
 * same as the step form: a variable an earlier step declares is listed and a pick lands mid-text. Asserts the
 * condition each dialog submits, the state the streamer keeps.
 */
@OptIn(ExperimentalTestApi::class)
class IfLoopVariablePickerTest {

    private val palette: RuntimePalette =
        RuntimePalette(
            actions =
                listOf(
                    PaletteBlock(
                        type = "set_variable",
                        role = BlockRole.Action,
                        category = "",
                        description = "",
                        labelKey = null,
                        fields = listOf(BlockField(key = "name", labelKey = "name", required = true, declaresVariable = true)),
                        hasHints = true,
                    )
                ),
            conditions =
                listOf(
                    PaletteBlock(
                        type = "message_contains",
                        role = BlockRole.Condition,
                        category = "",
                        description = "",
                        labelKey = null,
                        fields = listOf(BlockField(key = "text", labelKey = "text", required = true)),
                        hasHints = true,
                    )
                ),
        )

    private val steps: List<PipelineStep> =
        listOf(PipelineStep(id = "s1", action = PipelineNode(type = "set_variable", params = mapOf("name" to "winner"))))

    private val initialCondition: PipelineNode = PipelineNode(type = "message_contains", params = mapOf("text" to "Hello , welcome"))

    private val api: TemplateHelpersApi =
        object : TemplateHelpersApi {
            override suspend fun helpers(context: TemplateHelperContext, eventType: String?): ApiResult<List<TemplateHelperDto>> =
                ApiResult.Ok(listOf(TemplateHelperDto(key = "user.name", descriptionKey = "desc.user", sample = "Viewer42")))
        }

    private fun androidx.compose.ui.test.ComposeUiTest.typeBraceMidText(): SemanticsNodeInteraction {
        val field: SemanticsNodeInteraction = onNode(hasSetTextAction() and hasText("Hello , welcome"))
        field.performClick()
        field.performTextInputSelection(TextRange(6))
        field.performTextInput("{")
        waitForIdle()
        return field
    }

    @Test
    fun the_if_dialog_lists_the_earlier_step_variable_and_a_pick_lands_mid_text() = runComposeUiTest {
        var submitted: PipelineNode? = null
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    IfBlockFormDialog(
                        initial = initialCondition,
                        steps = steps,
                        scope = VariableScope(),
                        palette = palette,
                        options = EditorOptions(),
                        templateHelpersApi = api,
                        onOpenCodeScript = {},
                        createCodeScript = { null },
                        onDismiss = {},
                        onSubmit = { submitted = it },
                    )
                }
            }
        }
        waitForIdle()
        typeBraceMidText()

        onNodeWithText("{winner}").assertExists()
        onNodeWithText("{user.name}").assertExists()

        onNodeWithText("{winner}").performClick()
        waitForIdle()
        onNodeWithText("Save").performClick()
        waitForIdle()

        assertEquals("Hello {winner}, welcome", submitted?.params?.get("text"))
    }

    @Test
    fun a_variable_declared_after_the_if_is_not_listed_inside_its_lane_but_the_ones_before_are() = runComposeUiTest {
        val tree: List<PipelineStep> =
            listOf(
                PipelineStep(id = "a", order = 0, action = PipelineNode(type = "set_variable", params = mapOf("name" to "winner"))),
                PipelineStep(id = "if1", order = 1, action = PipelineNode(type = "block"), blockKind = "if"),
                PipelineStep(id = "c", order = 2, action = PipelineNode(type = "set_variable", params = mapOf("name" to "afterIf"))),
                PipelineStep(
                    id = "i1",
                    order = 0,
                    parentStepId = "if1",
                    branch = "then",
                    action = PipelineNode(type = "set_variable", params = mapOf("name" to "sameLane")),
                ),
            )
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    IfBlockFormDialog(
                        initial = initialCondition,
                        steps = tree,
                        scope = VariableScope(parentStepId = "if1", branch = "then"),
                        palette = palette,
                        options = EditorOptions(),
                        templateHelpersApi = api,
                        onOpenCodeScript = {},
                        createCodeScript = { null },
                        onDismiss = {},
                        onSubmit = {},
                    )
                }
            }
        }
        waitForIdle()
        typeBraceMidText()

        onNodeWithText("{winner}").assertExists()
        onNodeWithText("{sameLane}").assertExists()
        onNodeWithText("{afterIf}").assertDoesNotExist()
    }

    @Test
    fun the_loop_while_dialog_lists_the_earlier_step_variable_and_a_pick_lands_mid_text() = runComposeUiTest {
        var whileCondition: PipelineNode? = null
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    LoopBlockFormDialog(
                        initialMode = "while",
                        initialCount = null,
                        initialListVar = null,
                        initialMaxIterations = null,
                        initialMaxLoopRuntimeSeconds = null,
                        initialCondition = initialCondition,
                        steps = steps,
                        scope = VariableScope(),
                        palette = palette,
                        options = EditorOptions(),
                        templateHelpersApi = api,
                        onOpenCodeScript = {},
                        createCodeScript = { null },
                        onDismiss = {},
                        onSubmit = { _, _, _, _, _, condition -> whileCondition = condition },
                    )
                }
            }
        }
        waitForIdle()
        typeBraceMidText()

        onNodeWithText("{winner}").assertExists()

        onNodeWithText("{winner}").performClick()
        waitForIdle()
        onNodeWithText("Save").performClick()
        waitForIdle()

        assertEquals("Hello {winner}, welcome", whileCondition?.params?.get("text"))
    }
}
