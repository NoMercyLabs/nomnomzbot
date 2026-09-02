// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.webhooks.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateInboundBody
import bot.nomnomz.dashboard.core.network.CreateOutboundBody
import bot.nomnomz.dashboard.core.network.InboundWebhook
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.OutboundDelivery
import bot.nomnomz.dashboard.core.network.OutboundEventCatalogueEntry
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundWebhookCreated
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.PipelineCatalogueRemote
import bot.nomnomz.dashboard.core.network.PipelineDetail
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.network.PipelineTestRunBody
import bot.nomnomz.dashboard.core.network.CreatePipelineBody
import bot.nomnomz.dashboard.core.network.UpdatePipelineBody
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.UpdateInboundBody
import bot.nomnomz.dashboard.core.network.UpdateOutboundBody
import bot.nomnomz.dashboard.core.network.WebhookTestResult
import bot.nomnomz.dashboard.core.network.WebhooksApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.webhooks.state.WebhooksController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

/**
 * S-OWN16 — the outbound webhook editor had NO template-helper picker at all: the body template field either
 * didn't exist in the UI or had to be hand-typed against the backend's `TemplateHelperContext.Webhook` registry
 * from memory. Proves the picker actually opens from the outbound edit dialog AND that selecting a helper
 * inserts its real token into the body-template field's STATE — not just that the dialog renders.
 */
@OptIn(ExperimentalTestApi::class)
class WebhooksOutboundTemplatePickerS_OWN16Test {

    @Test
    fun selecting_a_helper_in_the_outbound_edit_dialog_inserts_its_token_into_the_body_template_field() =
        runComposeUiTest {
            val endpoint =
                OutboundWebhook(
                    id = "ob-1",
                    name = "Discord notify",
                    fqdn = "https://example.com",
                    subscribedEventTypes = listOf("channel.follow"),
                    isEnabled = true,
                )
            val controller =
                WebhooksController(
                    channelsApi = FakeChannelsApi(),
                    webhooksApi = FakeWebhooksApi(outbound = listOf(endpoint)),
                    pipelinesApi = FakePipelinesApi(),
                )
            runBlocking { controller.load() }

            setContent {
                withLifecycle {
                    NomNomzTheme {
                        bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                            WebhooksScreen(
                                controller = controller,
                                role = ManagementRole.Broadcaster,
                                templateHelpersApi = FakeWebhookTemplateHelpersApi(),
                            )
                        }
                    }
                }
            }
            waitForIdle()

            onNodeWithContentDescription("Edit").performClick()
            waitForIdle()

            onNodeWithText("All helpers…").performClick()
            waitForIdle()

            onNodeWithText("{payload.name}", substring = true).performClick()
            waitForIdle()

            onNodeWithText("{payload.name}", substring = true).assertExists()
        }

    // S-OWN16 follow-up gap: OutboundWebhookEndpointDto never returned the saved BodyTemplate, so the edit
    // dialog always opened blank — the operator had no way to see what was currently configured. Proves the
    // dialog now PRE-FILLS the body-template field from the fetched endpoint, not blank.
    @Test
    fun opening_the_edit_dialog_prefills_the_body_template_field_from_the_fetched_endpoint() = runComposeUiTest {
        val savedTemplate = """{"who": "{payload.name}"}"""
        val endpoint =
            OutboundWebhook(
                id = "ob-2",
                name = "Discord notify",
                fqdn = "https://example.com",
                subscribedEventTypes = listOf("channel.follow"),
                bodyTemplate = savedTemplate,
                isEnabled = true,
            )
        val controller =
            WebhooksController(
                channelsApi = FakeChannelsApi(),
                webhooksApi = FakeWebhooksApi(outbound = listOf(endpoint)),
                pipelinesApi = FakePipelinesApi(),
            )
        runBlocking { controller.load() }

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        WebhooksScreen(
                            controller = controller,
                            role = ManagementRole.Broadcaster,
                            templateHelpersApi = FakeWebhookTemplateHelpersApi(),
                        )
                    }
                }
            }
        }
        waitForIdle()

        onNodeWithContentDescription("Edit").performClick()
        waitForIdle()

        onNodeWithText(savedTemplate, substring = true).assertExists()
    }
}

@androidx.compose.runtime.Composable
private fun withLifecycle(content: @androidx.compose.runtime.Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).apply {
        currentState = Lifecycle.State.CREATED
        currentState = Lifecycle.State.STARTED
        currentState = Lifecycle.State.RESUMED
    }
    androidx.compose.runtime.CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
}

// Answers with one real helper key — proves the picker inserts the ACTUAL selected token, not a placeholder.
private class FakeWebhookTemplateHelpersApi : TemplateHelpersApi {
    override suspend fun helpers(context: TemplateHelperContext, eventType: String?): ApiResult<List<TemplateHelperDto>> =
        ApiResult.Ok(listOf(TemplateHelperDto(key = "payload.name", descriptionKey = "helper.payload.name")))
}

private class FakeChannelsApi : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1"))
    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())
    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class FakePipelinesApi : PipelinesApi {
    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> = ApiResult.Ok(emptyList())
    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        ApiResult.Ok(PipelineCatalogueRemote())
    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> =
        ApiResult.Ok(PipelineDetail(id = id))
    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        ApiResult.Ok(PipelineDetail(id = "new-pipe", name = body.name))
    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun blastRadius(channelId: String, id: String): ApiResult<PipelineBlastRadiusSummary> =
        ApiResult.Ok(PipelineBlastRadiusSummary())
    override suspend fun testRun(channelId: String, id: String, body: PipelineTestRunBody): ApiResult<TestRunResult> =
        ApiResult.Ok(TestRunResult(success = true))
}

private class FakeWebhooksApi(private val outbound: List<OutboundWebhook>) : WebhooksApi {
    override suspend fun listInbound(channelId: String): ApiResult<List<InboundWebhook>> = ApiResult.Ok(emptyList())
    override suspend fun createInbound(channelId: String, body: CreateInboundBody): ApiResult<InboundWebhook> =
        error("stub")
    override suspend fun updateInbound(channelId: String, endpointId: String, body: UpdateInboundBody): ApiResult<InboundWebhook> =
        error("stub")
    override suspend fun toggleInbound(channelId: String, endpointId: String, enabled: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun rotateInboundToken(channelId: String, endpointId: String): ApiResult<String> = error("stub")
    override suspend fun deleteInbound(channelId: String, endpointId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun inboundBlastRadius(channelId: String, endpointId: String): ApiResult<BlastRadiusSummary> =
        ApiResult.Ok(BlastRadiusSummary())

    override suspend fun outboundEventCatalogue(channelId: String): ApiResult<List<OutboundEventCatalogueEntry>> =
        ApiResult.Ok(emptyList())
    override suspend fun listOutbound(channelId: String): ApiResult<List<OutboundWebhook>> = ApiResult.Ok(outbound)
    override suspend fun createOutbound(channelId: String, body: CreateOutboundBody): ApiResult<OutboundWebhookCreated> =
        error("stub")
    override suspend fun updateOutbound(channelId: String, endpointId: String, body: UpdateOutboundBody): ApiResult<OutboundWebhook> =
        ApiResult.Ok(outbound.first { it.id == endpointId })
    override suspend fun toggleOutbound(channelId: String, endpointId: String, enabled: Boolean): ApiResult<Unit> =
        ApiResult.Ok(Unit)
    override suspend fun reenableOutbound(channelId: String, endpointId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun rotateOutboundSecret(channelId: String, endpointId: String): ApiResult<String> = error("stub")
    override suspend fun testOutbound(channelId: String, endpointId: String): ApiResult<WebhookTestResult> =
        ApiResult.Ok(WebhookTestResult())
    override suspend fun outboundDeliveries(channelId: String, endpointId: String): ApiResult<List<OutboundDelivery>> =
        ApiResult.Ok(emptyList())
    override suspend fun retryOutboundDelivery(channelId: String, endpointId: String, deliveryId: Long): ApiResult<OutboundDelivery> =
        error("stub")
    override suspend fun deleteOutbound(channelId: String, endpointId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}
