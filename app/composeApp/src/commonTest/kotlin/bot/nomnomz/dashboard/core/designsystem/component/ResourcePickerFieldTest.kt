// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PickerKind
import bot.nomnomz.dashboard.core.network.PipelineOptionDto
import bot.nomnomz.dashboard.core.network.PipelineOptionListResultDto
import bot.nomnomz.dashboard.core.network.PipelineOptionsApi
import kotlin.test.Test

/**
 * S-RICH-PICKERS (dashboard half): proves [ResourcePickerField] actually RENDERS the backend's rich option
 * shape — label + secondary context + a shown reason for an unselectable option + a source-unavailable
 * message distinct from a genuinely empty list — for EVERY [PickerKind] the backend's `IPipelineOptionProvider`
 * registry supplies (backend commit 910d200c), never a bare id or a raw list. [PickerKind.entries] is walked
 * rather than hand-listed, so a picker kind added to the shared enum without matching option data here fails
 * this test by name instead of silently falling back to the old bare-id box. Every render is pinned to the
 * `en` locale via [AppEnvironment] — the test host's platform default is not guaranteed to be English (the
 * `nl` translation renders otherwise and the English assertions below spuriously fail).
 */
@OptIn(ExperimentalTestApi::class)
class ResourcePickerFieldTest {

    // One representative rich option per picker kind — the exact secondary-context shape the backend documents
    // for that kind (reward cost + paused state, voice locale/gender/provider, sound-clip duration, Discord
    // channel category/type, widget kind, user display name + login). Real fields, never a placeholder string.
    private val sampleByKind: Map<PickerKind, PipelineOptionDto> =
        mapOf(
            PickerKind.Reward to
                PipelineOptionDto(value = "rw1", label = "Hydrate!", secondaryText = "500 points — paused"),
            PickerKind.Widget to
                PipelineOptionDto(value = "wg1", label = "Follower Alert", secondaryText = "Alert widget"),
            PickerKind.Voice to
                PipelineOptionDto(value = "vc1", label = "Aria", secondaryText = "en-US · Female · Azure"),
            PickerKind.SoundClip to
                PipelineOptionDto(value = "sc1", label = "Airhorn", secondaryText = "2.4s"),
            PickerKind.DiscordChannel to
                PipelineOptionDto(value = "dc1", label = "#general", secondaryText = "Text · General"),
            PickerKind.DiscordRole to
                PipelineOptionDto(value = "dr1", label = "Moderator", secondaryText = "128 members"),
            PickerKind.TwitchUser to
                PipelineOptionDto(value = "tu1", label = "StreamFan22", secondaryText = "streamfan22"),
            PickerKind.Asset to
                PipelineOptionDto(value = "as1", label = "banner.png", secondaryText = "PNG · 480 KB"),
        )

    @Test
    fun every_picker_kind_renders_the_label_and_the_secondary_context() {
        for (kind in PickerKind.entries) {
            val option: PipelineOptionDto = requireNotNull(sampleByKind[kind]) { "no fixture for $kind" }
            runComposeUiTest {
                setContent {
                    AppEnvironment(tag = "en") {
                        NomNomzTheme {
                            ResourcePickerField(
                                kind = kind,
                                api = FakePipelineOptionsApi(ApiResult.Ok(PipelineOptionListResultDto(items = listOf(option)))),
                                selectedId = null,
                                onSelect = {},
                            )
                        }
                    }
                }
                waitUntil(timeoutMillis = 5_000) {
                    onAllNodesWithTextCount(option.label) > 0
                }
                onNodeWithText(option.label).assertExists()
                onNodeWithText(option.secondaryText.orEmpty()).assertExists()
            }
        }
    }

    @Test
    fun an_unselectable_option_renders_disabled_with_its_reason() = runComposeUiTest {
        val unavailable: PipelineOptionDto =
            PipelineOptionDto(
                value = "dr2",
                label = "Admin",
                secondaryText = "3 members",
                reason = "You don't have permission to assign this role.",
            )
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ResourcePickerField(
                        kind = PickerKind.DiscordRole,
                        api = FakePipelineOptionsApi(ApiResult.Ok(PipelineOptionListResultDto(items = listOf(unavailable)))),
                        selectedId = null,
                        onSelect = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) { onAllNodesWithTextCount(unavailable.label) > 0 }
        onNodeWithText(unavailable.label).assertExists()
        onNodeWithText(unavailable.reason.orEmpty()).assertExists()
    }

    @Test
    fun source_unavailable_renders_a_distinct_message_from_a_genuinely_empty_list() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ResourcePickerField(
                        kind = PickerKind.Asset,
                        api =
                            FakePipelineOptionsApi(
                                ApiResult.Ok(
                                    PipelineOptionListResultDto(
                                        sourceAvailable = false,
                                        unavailableReason = "The asset store is not reachable right now.",
                                    )
                                )
                            ),
                        selectedId = null,
                        onSelect = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithTextCount("The asset store is not reachable right now.") > 0
        }
        onNodeWithText("The asset store is not reachable right now.", substring = true).assertExists()
        // A genuinely empty (but AVAILABLE) source renders the generic "none found" copy instead — proving the
        // two states are two different sentences, never the same collapsed empty message.
        onNodeWithText("None found.").assertDoesNotExist()
    }

    @Test
    fun a_genuinely_empty_source_renders_the_empty_message_not_the_unavailable_message() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ResourcePickerField(
                        kind = PickerKind.SoundClip,
                        api = FakePipelineOptionsApi(ApiResult.Ok(PipelineOptionListResultDto(sourceAvailable = true, items = emptyList()))),
                        selectedId = null,
                        onSelect = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) { onAllNodesWithTextCount("None found.") > 0 }
        onNodeWithText("None found.").assertExists()
    }
}

private class FakePipelineOptionsApi(private val result: ApiResult<PipelineOptionListResultDto>) : PipelineOptionsApi {
    override suspend fun getOptions(kind: PickerKind, search: String?): ApiResult<PipelineOptionListResultDto> = result
}

@OptIn(ExperimentalTestApi::class)
private fun ComposeUiTest.onAllNodesWithTextCount(text: String): Int =
    onAllNodesWithText(text, substring = true).fetchSemanticsNodes().size
