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

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.LightTokens
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.designsystem.theme.Radii
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelBotStatusDetail
import bot.nomnomz.dashboard.core.network.ChannelScopesResponse
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateInboundBody
import bot.nomnomz.dashboard.core.network.CreateOutboundBody
import bot.nomnomz.dashboard.core.network.CreatePickListBody
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.OAuthStart
import bot.nomnomz.dashboard.core.network.OutboundDelivery
import bot.nomnomz.dashboard.core.network.PickList
import bot.nomnomz.dashboard.core.network.PickListPreview
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineGraph
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelineTestRunBody
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.UpdateInboundBody
import bot.nomnomz.dashboard.core.network.UpdateOutboundBody
import bot.nomnomz.dashboard.core.network.UpdatePickListBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.WebhooksApi
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesController
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesState
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

/**
 * S-PIPE-TREE-VIS: the pipeline tree editor previously read as a flat, indented list — a two-level-deep
 * "if -> if -> send message" chain was legible only from repeated "Als: ..." headers and a growing left
 * padding, not from any real containment. These tests prove the three concrete defects found on the rendered
 * client are actually fixed, not merely "looks fixed" from reading the source:
 *   1. [containment_tier_changes_at_every_nesting_level] — the SAME depth->tier functions [TreeNodeContainer]
 *      renders through resolve to different, real design tokens at each level.
 *   2. [leaf_delete_control_is_present_and_enabled_two_levels_deep] — the leaf step's delete glyph exists and
 *      is enabled two lanes deep, without any hover interaction.
 *   3. [PipelineListNameWrapTest] (separate class below) — a long pipeline name wraps instead of ellipsizing on
 *      the list page at Compact width.
 */
class PipelineTreeVisTest {

    @Test
    fun containment_tier_changes_at_every_nesting_level() {
        // The radius shrinks one step per level (sleak's concentric-radius rule) and floors at the smallest
        // token rather than inventing a fourth tier once nesting goes past two levels.
        val depth0Radius: TreeNodeRadiusTier = treeNodeRadiusTier(0)
        val depth1Radius: TreeNodeRadiusTier = treeNodeRadiusTier(1)
        val depth2Radius: TreeNodeRadiusTier = treeNodeRadiusTier(2)
        val depth3Radius: TreeNodeRadiusTier = treeNodeRadiusTier(3)
        assertNotEquals(depth0Radius, depth1Radius, "root and first nesting level must not share a radius tier")
        assertNotEquals(depth1Radius, depth2Radius, "first and second nesting level must not share a radius tier")
        assertEquals(TreeNodeRadiusTier.Sm, depth2Radius, "depth 2 floors at the smallest radius tier")
        assertEquals(TreeNodeRadiusTier.Sm, depth3Radius, "depth 3+ stays at the floor, not a new invented tier")

        // Those tiers must resolve to REAL, distinct radius tokens from the actual design-system scale — not
        // just distinct enum values that happen to render identically.
        val radii: Radii = Radii()
        assertNotEquals(
            depth0Radius.resolve(radii),
            depth1Radius.resolve(radii),
            "depth 0 and depth 1 must paint at different corner radii",
        )
        assertNotEquals(
            depth1Radius.resolve(radii),
            depth2Radius.resolve(radii),
            "depth 1 and depth 2 must paint at different corner radii",
        )

        // The neutral surface cycles by depth too, so two adjacent levels are visibly distinct even where the
        // radius step is subtle — resolved against the real light token set, never an invented color.
        val depth0Surface: TreeNodeSurfaceTier = treeNodeSurfaceTier(0)
        val depth1Surface: TreeNodeSurfaceTier = treeNodeSurfaceTier(1)
        val depth2Surface: TreeNodeSurfaceTier = treeNodeSurfaceTier(2)
        assertNotEquals(
            depth0Surface.resolve(LightTokens),
            depth1Surface.resolve(LightTokens),
            "depth 0 and depth 1 must paint on different neutral surfaces",
        )
        assertNotEquals(
            depth1Surface.resolve(LightTokens),
            depth2Surface.resolve(LightTokens),
            "depth 1 and depth 2 must paint on different neutral surfaces",
        )

        // Never the accent color (sleak: accent stays scarce) — every depth resolves to exactly `card` or
        // `sidebar`, structurally, never to `accent`/`secondary`/`muted` (which this theme's own light palette
        // defines as the literal SAME value as `accent` — a pixel-level equality check here would be testing
        // OKLCH->sRGB float rounding near white, not the actual design decision).
        val neutralTones: Set<Color> = setOf(LightTokens.card, LightTokens.sidebar)
        assertTrue(depth0Surface.resolve(LightTokens) in neutralTones)
        assertTrue(depth1Surface.resolve(LightTokens) in neutralTones)
        assertTrue(depth2Surface.resolve(LightTokens) in neutralTones)
    }

    @OptIn(ExperimentalTestApi::class)
    @Test
    fun leaf_delete_control_is_present_and_enabled_two_levels_deep() {
        // "if1" (depth 0) -> its "then" lane holds "if2" (depth 1) -> its "then" lane holds "leaf" (depth 2):
        // exactly the "if -> if -> send message" shape the owner measured as reading flat in the browser.
        val ifCondition = PipelineNode(type = "always")
        val steps: List<PipelineStep> =
            listOf(
                PipelineStep(action = PipelineNode(type = "block"), blockKind = "if", condition = ifCondition, id = "if1", order = 0),
                PipelineStep(
                    action = PipelineNode(type = "block"),
                    blockKind = "if",
                    condition = ifCondition,
                    id = "if2",
                    parentStepId = "if1",
                    branch = "then",
                    order = 0,
                ),
                // A leading no-op step in "if2"'s "then" lane so "leaf" sits at lane index 1 ("Delete step 2")
                // instead of colliding with "if1"'s and "if2"'s own delete labels, which are BOTH "Delete step
                // 1" — each block's delete label is numbered by its own lane-relative position, a pre-existing
                // product quirk unrelated to this slice; this only disambiguates which control the test targets.
                PipelineStep(action = PipelineNode(type = "stop"), id = "spacer", parentStepId = "if2", branch = "then", order = 0),
                PipelineStep(
                    action = PipelineNode(type = "send_message", params = mapOf("message" to "hi")),
                    id = "leaf",
                    parentStepId = "if2",
                    branch = "then",
                    order = 1,
                ),
            )
        val channel = ChannelSummary(id = "chan-1")
        val detail =
            PipelineDetail(id = "pipe-1", name = "Nested If Chain", graph = PipelineGraph(steps).toJson())
        val controller =
            PipelinesController(
                channelsApi = FakeChannelsApiForTreeVisTest(channel),
                pipelinesApi = FakePipelinesApiForTreeVisTest(detail),
                webhooksApi = FakeWebhooksApiForTreeVisTest(),
                pickListsApi = FakePickListsApiForTreeVisTest(),
            )

        runTest {
            controller.load()
            controller.openEditor(PipelineSummary(id = detail.id, name = detail.name))
        }
        assertTrue(controller.state.value is PipelinesState.Editing, "the fake API round-trip must land the editor open")

        val editing: PipelinesState.Editing = controller.state.value as PipelinesState.Editing

        runComposeUiTest {
            setContent {
                EnglishThemedContent {
                    val scope = androidx.compose.runtime.rememberCoroutineScope()
                    ChainEditor(
                        editing = editing,
                        manage = ManageDecision.Allowed,
                        controller = controller,
                        scope = scope,
                        templateHelpersApi = FakeTemplateHelpersApiForTreeVisTest(),
                        onOpenCodeScript = {},
                    )
                }
            }
            waitForIdle()

            // "Delete step 2" is the leaf's own delete control (see the "spacer" comment above) — it must exist
            // uniquely and be enabled at rest, without any hover/pointer interaction ever being simulated here
            // (S-PIPE-TREE-VIS #2).
            onNodeWithContentDescription("Delete step 2").assertIsEnabled()
        }
    }
}

/** S-PIPE-TREE-VIS #3: a long pipeline name on the list page wraps instead of ellipsizing at Compact width. */
@OptIn(ExperimentalTestApi::class)
class PipelineListNameWrapTest {

    @Test
    fun long_pipeline_name_wraps_across_multiple_lines_instead_of_ellipsizing() {
        // Long enough that, at a Compact-width row, it MUST take more than one line once it is no longer
        // clamped to `maxLines = 1` — the exact "Raid..." / "Pijplij..." truncation the owner measured.
        val longName = "Wwwwwwwwwww Response And Full Community Announcement Automation Pipeline"
        val shortName = "Wwwwwwwwwww"

        runComposeUiTest {
            setContent {
                EnglishThemedContent {
                    Column {
                        Box(androidx.compose.ui.Modifier.width(220.dp)) {
                            PipelineRow(
                                pipeline = PipelineSummary(id = "long", name = longName, isEnabled = true),
                                manage = ManageDecision.Allowed,
                                onOpen = {},
                                onEdit = {},
                                onToggle = {},
                                onDelete = {},
                            )
                        }
                        Box(androidx.compose.ui.Modifier.width(220.dp)) {
                            PipelineRow(
                                pipeline = PipelineSummary(id = "short", name = shortName, isEnabled = true),
                                manage = ManageDecision.Allowed,
                                onOpen = {},
                                onEdit = {},
                                onToggle = {},
                                onDelete = {},
                            )
                        }
                    }
                }
            }
            waitForIdle()

            // PipelineRow clears and re-sets its own semantics (a single combined row description for a11y), so
            // the individual Text nodes only exist in the UNMERGED tree.
            val longNameHeight: Int = onNodeWithText(longName, useUnmergedTree = true).fetchSemanticsNode().size.height
            val shortNameHeight: Int = onNodeWithText(shortName, useUnmergedTree = true).fetchSemanticsNode().size.height

            // Proven by the rendered text actually growing taller (wrapping to 2+ lines), not merely by the
            // semantics tree still carrying the full un-truncated string — ellipsis never changes that string,
            // only the paint, so only a real layout-height check catches a regression back to `maxLines = 1`.
            assertTrue(
                longNameHeight > shortNameHeight * 3 / 2,
                "expected the long pipeline name to wrap onto multiple lines (long=$longNameHeight, one-line=$shortNameHeight)",
            )
        }
    }
}

@Composable
private fun EnglishThemedContent(content: @Composable () -> Unit) {
    AppEnvironment(tag = "en") {
        NomNomzTheme { content() }
    }
}

private val NotImplementedInTest: ApiResult<Nothing> =
    ApiResult.Failure(ApiError(status = 501, code = "NOT_IMPLEMENTED", message = "not implemented in this test fake"))

private class FakeChannelsApiForTreeVisTest(private val channel: ChannelSummary) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(channel)

    override suspend fun list(): ApiResult<List<ChannelSummary>> = NotImplementedInTest

    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = NotImplementedInTest

    override suspend fun join(channelId: String): ApiResult<Unit> = NotImplementedInTest

    override suspend fun leave(channelId: String): ApiResult<Unit> = NotImplementedInTest

    override suspend fun reset(channelId: String): ApiResult<Unit> = NotImplementedInTest

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = NotImplementedInTest

    override suspend fun channelScopes(channelId: String): ApiResult<ChannelScopesResponse> = NotImplementedInTest

    override suspend fun startChannelBotConnect(channelId: String): ApiResult<OAuthStart> = NotImplementedInTest

    override suspend fun channelBotStatus(channelId: String): ApiResult<ChannelBotStatusDetail> = NotImplementedInTest

    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = NotImplementedInTest
}

private class FakePipelinesApiForTreeVisTest(private val detail: PipelineDetail) : PipelinesApi {
    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> =
        ApiResult.Ok(listOf(PipelineSummary(id = detail.id, name = detail.name)))

    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(PipelineCatalogueRemote())

    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> = ApiResult.Ok(detail)

    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> = NotImplementedInTest

    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        NotImplementedInTest

    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> =
        NotImplementedInTest

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> = NotImplementedInTest

    override suspend fun blastRadius(channelId: String, id: String): ApiResult<PipelineBlastRadiusSummary> =
        NotImplementedInTest

    override suspend fun testRun(channelId: String, id: String, body: PipelineTestRunBody): ApiResult<TestRunResult> =
        NotImplementedInTest
}

private class FakeWebhooksApiForTreeVisTest : WebhooksApi {
    override suspend fun listInbound(channelId: String) = NotImplementedInTest
    override suspend fun createInbound(channelId: String, body: CreateInboundBody) = NotImplementedInTest
    override suspend fun updateInbound(channelId: String, endpointId: String, body: UpdateInboundBody) = NotImplementedInTest
    override suspend fun toggleInbound(channelId: String, endpointId: String, enabled: Boolean) = NotImplementedInTest
    override suspend fun rotateInboundToken(channelId: String, endpointId: String) = NotImplementedInTest
    override suspend fun deleteInbound(channelId: String, endpointId: String) = NotImplementedInTest
    override suspend fun inboundBlastRadius(channelId: String, endpointId: String) = NotImplementedInTest
    override suspend fun outboundEventCatalogue(channelId: String) = NotImplementedInTest
    override suspend fun listOutbound(channelId: String) = NotImplementedInTest
    override suspend fun createOutbound(channelId: String, body: CreateOutboundBody) = NotImplementedInTest
    override suspend fun updateOutbound(channelId: String, endpointId: String, body: UpdateOutboundBody) = NotImplementedInTest
    override suspend fun toggleOutbound(channelId: String, endpointId: String, enabled: Boolean) = NotImplementedInTest
    override suspend fun reenableOutbound(channelId: String, endpointId: String) = NotImplementedInTest
    override suspend fun rotateOutboundSecret(channelId: String, endpointId: String) = NotImplementedInTest
    override suspend fun testOutbound(channelId: String, endpointId: String) = NotImplementedInTest
    override suspend fun outboundDeliveries(channelId: String, endpointId: String) = NotImplementedInTest
    override suspend fun retryOutboundDelivery(channelId: String, endpointId: String, deliveryId: Long): ApiResult<OutboundDelivery> =
        NotImplementedInTest
    override suspend fun deleteOutbound(channelId: String, endpointId: String) = NotImplementedInTest
}

private class FakePickListsApiForTreeVisTest : PickListsApi {
    override suspend fun list(): ApiResult<List<PickList>> = NotImplementedInTest
    override suspend fun get(id: String): ApiResult<PickList> = NotImplementedInTest
    override suspend fun create(body: CreatePickListBody): ApiResult<Unit> = NotImplementedInTest
    override suspend fun update(id: String, body: UpdatePickListBody): ApiResult<Unit> = NotImplementedInTest
    override suspend fun delete(id: String): ApiResult<Unit> = NotImplementedInTest
    override suspend fun blastRadius(id: String) = NotImplementedInTest
    override suspend fun pick(id: String): ApiResult<PickListPreview> = NotImplementedInTest
}

private class FakeTemplateHelpersApiForTreeVisTest : TemplateHelpersApi {
    override suspend fun helpers(context: TemplateHelperContext, eventType: String?): ApiResult<List<TemplateHelperDto>> =
        NotImplementedInTest
}
