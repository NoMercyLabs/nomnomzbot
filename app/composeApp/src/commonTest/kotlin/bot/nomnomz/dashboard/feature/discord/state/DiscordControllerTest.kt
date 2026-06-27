// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.discord.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateDiscordConfigBody
import bot.nomnomz.dashboard.core.network.CreateDiscordRoleBody
import bot.nomnomz.dashboard.core.network.DiscordApi
import bot.nomnomz.dashboard.core.network.DiscordConfigPreview
import bot.nomnomz.dashboard.core.network.DiscordDispatchLogEntry
import bot.nomnomz.dashboard.core.network.DiscordEmbed
import bot.nomnomz.dashboard.core.network.DiscordGuildConnection
import bot.nomnomz.dashboard.core.network.DiscordNotificationConfig
import bot.nomnomz.dashboard.core.network.DiscordNotificationRole
import bot.nomnomz.dashboard.core.network.UpdateDiscordRoleBody
import bot.nomnomz.dashboard.core.network.UpdateDiscordConfigBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Discord page state machine the screen renders: resolve the active channel, then surface the
// channel's real linked guild(s) and each guild's notification rules — empty when no guild is linked, error if
// resolving the channel or reading the link fails. Writes mutate the fake's backing store, so the controller's
// post-write reload observes the real consequence (a new rule, a flipped flag, an edited message, a removed
// rule), not merely that a call happened. The whole-row PUT contract is asserted on the recorded body. The
// screen is a pure projection of this, so testing it proves the page shows real data and manages it correctly.
class DiscordControllerTest {

    @Test
    fun load_surfaces_the_linked_guild_and_its_notification_rules() = runTest {
        val controller =
            DiscordController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingDiscordApi(
                    connections = listOf(guild("g1", "My Server")),
                    configsByGuild =
                        mapOf(
                            "g1" to
                                mutableListOf(
                                    config(
                                        id = "c1",
                                        guild = "g1",
                                        trigger = "stream.online",
                                        target = "111",
                                        message = "We are LIVE!",
                                        enabled = true,
                                    )
                                )
                        ),
                ),
            )

        controller.load()

        val state: DiscordState = controller.state.value
        assertTrue(state is DiscordState.Ready)
        val guilds: List<GuildNotifications> = (state as DiscordState.Ready).guilds
        assertEquals(1, guilds.size)
        val guild: GuildNotifications = guilds.first()
        assertEquals("g1", guild.connection.id)
        assertEquals("My Server", guild.connection.guildName)
        assertNull(guild.loadError)
        assertEquals(1, guild.configs.size)
        val rule: DiscordNotificationConfig = guild.configs.first()
        assertEquals("stream.online", rule.triggerType)
        assertEquals("111", rule.targetChannelId)
        assertEquals("We are LIVE!", rule.messageTemplate)
        assertEquals(true, rule.enabled)
    }

    @Test
    fun load_is_empty_when_no_guild_is_connected() = runTest {
        val controller =
            DiscordController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingDiscordApi(connections = emptyList()),
            )

        controller.load()

        // Not connected → the screen routes the operator to Integrations, not an error.
        assertTrue(controller.state.value is DiscordState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            DiscordController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                RecordingDiscordApi(connections = emptyList()),
            )

        controller.load()

        assertTrue(controller.state.value is DiscordState.Error)
    }

    @Test
    fun load_errors_when_the_connections_call_fails() = runTest {
        val controller =
            DiscordController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingDiscordApi(connectionsResult = ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is DiscordState.Error)
    }

    @Test
    fun a_per_guild_config_read_failure_surfaces_inline_without_failing_the_page() = runTest {
        val controller =
            DiscordController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                RecordingDiscordApi(
                    connections = listOf(guild("g1", "My Server")),
                    configsResult = ApiResult.Failure(ApiError(503, "DOWN", "configs unavailable")),
                ),
            )

        controller.load()

        val state: DiscordState = controller.state.value
        assertTrue(state is DiscordState.Ready)
        val guild: GuildNotifications = (state as DiscordState.Ready).guilds.first()
        assertEquals("configs unavailable", guild.loadError)
        assertTrue(guild.configs.isEmpty())
    }

    @Test
    fun create_persists_the_rule_then_reloads_with_the_new_row() = runTest {
        val api =
            RecordingDiscordApi(
                connections = listOf(guild("g1", "My Server")),
                configsByGuild = mapOf("g1" to mutableListOf()),
            )
        val controller = DiscordController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.createConfig(
            connectionId = "g1",
            triggerType = "channel.follow",
            targetChannelId = "222",
            messageTemplate = "New follow: {{user.name}}",
            enabled = true,
        )

        // The api recorded exactly the body the controller built.
        assertEquals(1, api.created.size)
        val created: Pair<String, CreateDiscordConfigBody> = api.created.first()
        assertEquals("g1", created.first)
        assertEquals("channel.follow", created.second.triggerType)
        assertEquals("222", created.second.targetChannelId)
        assertEquals("New follow: {{user.name}}", created.second.messageTemplate)
        assertEquals(true, created.second.enabled)

        // And the post-write reload surfaced the freshly-created rule from the store.
        val state: DiscordState = controller.state.value
        assertTrue(state is DiscordState.Ready)
        val configs: List<DiscordNotificationConfig> = (state as DiscordState.Ready).guilds.first().configs
        assertEquals(1, configs.size)
        assertEquals("channel.follow", configs.first().triggerType)
        assertNull(state.actionError)
    }

    @Test
    fun edit_replaces_the_channel_and_message_preserving_the_trigger_and_embed() = runTest {
        val api =
            RecordingDiscordApi(
                connections = listOf(guild("g1", "My Server")),
                configsByGuild =
                    mapOf(
                        "g1" to
                            mutableListOf(
                                config(
                                    id = "c1",
                                    guild = "g1",
                                    trigger = "stream.online",
                                    target = "111",
                                    message = "old",
                                    enabled = true,
                                    embed = DiscordEmbed(title = "Live"),
                                )
                            )
                    ),
            )
        val controller = DiscordController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.updateConfig(
            configId = "c1",
            targetChannelId = "999",
            messageTemplate = "We are LIVE now!",
            enabled = true,
        )

        // The whole-row PUT carried the new channel/message AND preserved the embed (never dropped on edit).
        assertEquals(1, api.updated.size)
        val body: UpdateDiscordConfigBody = api.updated.first().second
        assertEquals("999", body.targetChannelId)
        assertEquals("We are LIVE now!", body.messageTemplate)
        assertEquals(true, body.enabled)
        assertEquals("Live", body.embedConfig?.title)

        // The reload reflects the persisted edit; the trigger is unchanged (immutable on the row).
        val rule: DiscordNotificationConfig =
            (controller.state.value as DiscordState.Ready).guilds.first().configs.first()
        assertEquals("stream.online", rule.triggerType)
        assertEquals("999", rule.targetChannelId)
        assertEquals("We are LIVE now!", rule.messageTemplate)
    }

    @Test
    fun toggle_flips_only_enabled_preserving_the_rest_then_reloads() = runTest {
        val api =
            RecordingDiscordApi(
                connections = listOf(guild("g1", "My Server")),
                configsByGuild =
                    mapOf(
                        "g1" to
                            mutableListOf(
                                config(
                                    id = "c1",
                                    guild = "g1",
                                    trigger = "stream.online",
                                    target = "111",
                                    message = "We are LIVE!",
                                    enabled = true,
                                )
                            )
                    ),
            )
        val controller = DiscordController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.toggleConfig(configId = "c1", enabled = false)

        // The whole-row PUT flipped enabled but kept the channel + message.
        val body: UpdateDiscordConfigBody = api.updated.first().second
        assertEquals(false, body.enabled)
        assertEquals("111", body.targetChannelId)
        assertEquals("We are LIVE!", body.messageTemplate)

        // The reload reflects the persisted flip.
        val rule: DiscordNotificationConfig =
            (controller.state.value as DiscordState.Ready).guilds.first().configs.first()
        assertEquals(false, rule.enabled)
    }

    @Test
    fun delete_removes_the_rule_then_reloads_without_it() = runTest {
        val api =
            RecordingDiscordApi(
                connections = listOf(guild("g1", "My Server")),
                configsByGuild =
                    mapOf(
                        "g1" to
                            mutableListOf(
                                config(id = "c1", guild = "g1", trigger = "stream.online", target = "111"),
                                config(id = "c2", guild = "g1", trigger = "channel.follow", target = "222"),
                            )
                    ),
            )
        val controller = DiscordController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.deleteConfig(configId = "c1")

        assertEquals(listOf("c1"), api.deleted)
        val configs: List<DiscordNotificationConfig> =
            (controller.state.value as DiscordState.Ready).guilds.first().configs
        assertEquals(listOf("c2"), configs.map { it.id })
    }

    @Test
    fun a_failed_write_surfaces_the_error_over_the_kept_guilds() = runTest {
        val api =
            RecordingDiscordApi(
                connections = listOf(guild("g1", "My Server")),
                configsByGuild =
                    mapOf("g1" to mutableListOf(config(id = "c1", guild = "g1", trigger = "stream.online", target = "111"))),
                writeResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "no permission")),
            )
        val controller = DiscordController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.deleteConfig(configId = "c1")

        // The guilds are kept (not blown away) and the failure is surfaced on them.
        val state: DiscordState = controller.state.value
        assertTrue(state is DiscordState.Ready)
        assertEquals(1, (state as DiscordState.Ready).guilds.first().configs.size)
        assertEquals("no permission", state.actionError)
    }

    // ── fakes ──────────────────────────────────────────────────────────────────

    private fun guild(id: String, name: String): DiscordGuildConnection =
        DiscordGuildConnection(
            id = id,
            guildId = "snowflake-$id",
            guildName = name,
            botInstalled = true,
            serverConsentStatus = "approved",
            streamerEnabled = true,
            isLinkActive = true,
        )

    private fun config(
        id: String,
        guild: String,
        trigger: String,
        target: String,
        message: String? = null,
        enabled: Boolean = true,
        embed: DiscordEmbed? = null,
    ): DiscordNotificationConfig =
        DiscordNotificationConfig(
            id = id,
            guildConnectionId = guild,
            triggerType = trigger,
            enabled = enabled,
            targetChannelId = target,
            messageTemplate = message,
            embedConfig = embed,
        )
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

// A recording fake that behaves like the backend store: configs() returns the live per-guild store, and each
// successful create/update/delete mutates it so the controller's post-write reload observes the real
// consequence (a new rule, a flipped flag, an edited message, a removed rule) — not merely that a call
// happened. [writeResult] forces every write to fail (store untouched) to exercise the error path.
// [connectionsResult] / [configsResult] force the respective read to fail.
private class RecordingDiscordApi(
    connections: List<DiscordGuildConnection> = emptyList(),
    configsByGuild: Map<String, MutableList<DiscordNotificationConfig>> = emptyMap(),
    private val connectionsResult: ApiResult<List<DiscordGuildConnection>>? = null,
    private val configsResult: ApiResult<List<DiscordNotificationConfig>>? = null,
    private val writeResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : DiscordApi {
    private val connectionStore: List<DiscordGuildConnection> = connections
    private val configStore: MutableMap<String, MutableList<DiscordNotificationConfig>> =
        configsByGuild.toMutableMap()
    private var nextId: Int = 100

    val created: MutableList<Pair<String, CreateDiscordConfigBody>> = mutableListOf()
    val updated: MutableList<Pair<String, UpdateDiscordConfigBody>> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()

    override suspend fun connections(channelId: String): ApiResult<List<DiscordGuildConnection>> =
        connectionsResult ?: ApiResult.Ok(connectionStore)

    override suspend fun configs(
        channelId: String,
        connectionId: String,
    ): ApiResult<List<DiscordNotificationConfig>> =
        configsResult ?: ApiResult.Ok(configStore[connectionId]?.toList() ?: emptyList())

    override suspend fun createConfig(
        channelId: String,
        connectionId: String,
        body: CreateDiscordConfigBody,
    ): ApiResult<DiscordNotificationConfig> {
        created += connectionId to body
        if (writeResult is ApiResult.Failure) return ApiResult.Failure(writeResult.error)
        val row =
            DiscordNotificationConfig(
                id = "c${nextId++}",
                guildConnectionId = connectionId,
                triggerType = body.triggerType,
                enabled = body.enabled,
                targetChannelId = body.targetChannelId,
                pingRoleId = body.pingRoleId,
                messageTemplate = body.messageTemplate,
                embedConfig = body.embedConfig,
                milestoneType = body.milestoneType,
                milestoneThreshold = body.milestoneThreshold,
            )
        configStore.getOrPut(connectionId) { mutableListOf() } += row
        return ApiResult.Ok(row)
    }

    override suspend fun updateConfig(
        channelId: String,
        configId: String,
        body: UpdateDiscordConfigBody,
    ): ApiResult<DiscordNotificationConfig> {
        updated += configId to body
        if (writeResult is ApiResult.Failure) return ApiResult.Failure(writeResult.error)
        val list: MutableList<DiscordNotificationConfig> =
            configStore.values.firstOrNull { rows -> rows.any { it.id == configId } } ?: return notFound()
        val index: Int = list.indexOfFirst { it.id == configId }
        val updatedRow: DiscordNotificationConfig =
            list[index].copy(
                enabled = body.enabled,
                targetChannelId = body.targetChannelId,
                pingRoleId = body.pingRoleId,
                messageTemplate = body.messageTemplate,
                embedConfig = body.embedConfig,
                milestoneType = body.milestoneType,
                milestoneThreshold = body.milestoneThreshold,
            )
        list[index] = updatedRow
        return ApiResult.Ok(updatedRow)
    }

    override suspend fun deleteConfig(channelId: String, configId: String): ApiResult<Unit> {
        deleted += configId
        if (writeResult is ApiResult.Failure) return ApiResult.Failure(writeResult.error)
        configStore.values.forEach { rows -> rows.removeAll { it.id == configId } }
        return ApiResult.Ok(Unit)
    }

    private fun notFound(): ApiResult<DiscordNotificationConfig> =
        ApiResult.Failure(ApiError(404, "NOT_FOUND", "no such config"))

    override suspend fun previewConfig(channelId: String, configId: String): ApiResult<DiscordConfigPreview> =
        ApiResult.Ok(DiscordConfigPreview())

    override suspend fun roles(channelId: String, connectionId: String): ApiResult<List<DiscordNotificationRole>> =
        ApiResult.Ok(emptyList())

    override suspend fun createRole(
        channelId: String,
        connectionId: String,
        body: CreateDiscordRoleBody,
    ): ApiResult<DiscordNotificationRole> = ApiResult.Ok(DiscordNotificationRole())

    override suspend fun updateRole(
        channelId: String,
        roleId: String,
        body: UpdateDiscordRoleBody,
    ): ApiResult<DiscordNotificationRole> = ApiResult.Ok(DiscordNotificationRole())

    override suspend fun deleteRole(channelId: String, roleId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun postRoleButton(channelId: String, roleId: String, buttonChannelId: String): ApiResult<DiscordNotificationRole> =
        ApiResult.Ok(DiscordNotificationRole())

    override suspend fun approveServerConsent(
        channelId: String,
        connectionId: String,
        approvedByDiscordUserId: String,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun revokeServerConsent(channelId: String, connectionId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun dispatchLog(channelId: String, connectionId: String): ApiResult<List<DiscordDispatchLogEntry>> =
        ApiResult.Ok(emptyList())
}
