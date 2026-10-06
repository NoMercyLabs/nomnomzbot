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
import bot.nomnomz.dashboard.core.network.ApiError
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
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * The step form wires the `{` variable list into its template fields: for step 2, the variable step 1 declares
 * is in the list next to the registry helpers, and a pick lands at the cursor, mid-text. Asserts the step the
 * form submits, the state the streamer keeps.
 */
@OptIn(ExperimentalTestApi::class)
class StepFormVariablePickerTest {

    private fun block(type: String, vararg fields: BlockField): PaletteBlock =
        PaletteBlock(
            type = type,
            role = BlockRole.Action,
            category = "",
            description = "",
            labelKey = null,
            fields = fields.toList(),
            hasHints = true,
        )

    private val palette: RuntimePalette =
        RuntimePalette(
            actions =
                listOf(
                    block(
                        "set_variable",
                        BlockField(key = "name", labelKey = "name", required = true, declaresVariable = true),
                    ),
                    block("send_message", BlockField(key = "message", labelKey = "message", required = true)),
                ),
            conditions = emptyList(),
        )

    private val declaringStep: PipelineStep =
        PipelineStep(id = "s1", action = PipelineNode(type = "set_variable", params = mapOf("name" to "winner")))
    private val messageStep: PipelineStep =
        PipelineStep(id = "s2", action = PipelineNode(type = "send_message", params = mapOf("message" to "Hello , welcome")))

    private class FakeHelpersApi(private val result: ApiResult<List<TemplateHelperDto>>) : TemplateHelpersApi {
        override suspend fun helpers(context: TemplateHelperContext, eventType: String?): ApiResult<List<TemplateHelperDto>> =
            result
    }

    private fun androidx.compose.ui.test.ComposeUiTest.openStepTwo(
        api: TemplateHelpersApi,
        onSubmit: (PipelineStep) -> Unit = {},
    ): SemanticsNodeInteraction {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    StepFormDialog(
                        initial = messageStep,
                        steps = listOf(declaringStep, messageStep),
                        index = 1,
                        palette = palette,
                        options = EditorOptions(),
                        templateHelpersApi = api,
                        onOpenCodeScript = {},
                        createCodeScript = { null },
                        onDismiss = {},
                        onSubmit = onSubmit,
                    )
                }
            }
        }
        waitForIdle()
        return onNode(hasSetTextAction() and hasText("Hello , welcome"))
    }

    private val okApi: TemplateHelpersApi =
        FakeHelpersApi(
            ApiResult.Ok(listOf(TemplateHelperDto(key = "user.name", descriptionKey = "desc.user", sample = "Viewer42")))
        )

    @Test
    fun typing_an_open_brace_lists_the_earlier_step_variable_and_a_pick_lands_mid_text() = runComposeUiTest {
        var submitted: PipelineStep? = null
        val field: SemanticsNodeInteraction = openStepTwo(okApi) { submitted = it }

        field.performClick()
        field.performTextInputSelection(TextRange(6))
        field.performTextInput("{")
        waitForIdle()

        // The variable step 1 declares is in the list, beside the registry helper.
        onNodeWithText("{winner}").assertExists()
        onNodeWithText("{user.name}").assertExists()

        onNodeWithText("{winner}").performClick()
        waitForIdle()
        onNodeWithText("Save").performClick()
        waitForIdle()

        assertEquals("Hello {winner}, welcome", submitted?.action?.params?.get("message"))
    }

    @Test
    fun a_failed_helpers_load_still_lists_the_declared_variable() = runComposeUiTest {
        val failing: TemplateHelpersApi =
            FakeHelpersApi(ApiResult.Failure(ApiError(status = 500, code = null, message = "boom")))
        val field: SemanticsNodeInteraction = openStepTwo(failing)

        field.performClick()
        field.performTextInputSelection(TextRange(6))
        field.performTextInput("{")
        waitForIdle()

        onNodeWithText("{winner}").assertExists()
        onNodeWithText("{user.name}").assertDoesNotExist()
    }
}
