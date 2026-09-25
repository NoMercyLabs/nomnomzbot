// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelBasics
import bot.nomnomz.dashboard.core.network.ChannelSettingsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.UpdateBasicsBody
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.setup_finish_pending_error

/**
 * Resolve the signed-in streamer's primary channel and PUT the review step's onboarding basics. Shared by
 * [SetupController]'s in-process finish path (desktop, or a web launcher that happens to return) AND
 * [resumePendingSetupFinish] (the post-redirect web path) — one definition, two callers, never duplicated.
 * A blank prefix falls back to the conventional "!" so onboarding never persists an empty (match-everything)
 * prefix; blank locale/timezone are sent as null (leave unchanged). [platformBotConnected] decides whether
 * [botLinePrefix] still applies (D5): once a dedicated bot account is connected it types as itself and the
 * marker is meaningless, so null (leave unchanged) is sent instead of the possibly-stale typed value.
 */
suspend fun applySetupBasics(
    channelsApi: ChannelsApi,
    channelSettingsApi: ChannelSettingsApi,
    prefix: String,
    locale: String,
    timezone: String,
    botLinePrefix: String,
    platformBotConnected: Boolean,
): ApiResult<Unit> {
    val channel: ChannelSummary =
        when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
            is ApiResult.Failure -> return ApiResult.Failure(result.error)
            is ApiResult.Ok -> result.value
        }

    val resolvedPrefix: String = prefix.trim().ifEmpty { "!" }
    val result: ApiResult<ChannelBasics> =
        channelSettingsApi.updateBasics(
            channel.id,
            UpdateBasicsBody(
                prefix = resolvedPrefix,
                locale = locale.trim().ifEmpty { null },
                timezone = timezone.trim().ifEmpty { null },
                botLinePrefix = if (platformBotConnected) null else botLinePrefix.trim(),
            ),
        )
    return when (result) {
        is ApiResult.Failure -> ApiResult.Failure(result.error)
        is ApiResult.Ok -> ApiResult.Ok(Unit)
    }
}

/**
 * Resolve a [SetupFinishPending] record left behind by [SetupController.finish] when the streamer OAuth
 * redirect tore the page down before it could run completeSetup()/applyBasics() itself (the web bug this
 * closes: the whole page navigates away for the redirect, so nothing after that call ever ran). Call once at
 * app boot, AFTER the session is confirmed established — completeSetup() and the channel basics write both
 * need an authenticated caller, so calling this any earlier would only fail.
 *
 * A no-op when no record is pending (the common case — desktop, or a web finish that already resolved the
 * record in-process). Never silently swallows a failure (S070): completeSetup() or the basics write failing
 * surfaces the real backend error via [feedback] on the landing surface (the shell-level FeedbackHost renders
 * it on whatever page is mounted), and the record is LEFT IN PLACE so the next app start retries
 * automatically. The record is cleared only once BOTH steps succeed.
 */
suspend fun resumePendingSetupFinish(
    pendingStore: SetupFinishStore,
    systemApi: SystemApi,
    channelsApi: ChannelsApi,
    channelSettingsApi: ChannelSettingsApi,
    feedback: Feedback,
) {
    val pending: SetupFinishPending = pendingStore.read() ?: return

    when (val completeResult: ApiResult<Unit> = systemApi.completeSetup()) {
        is ApiResult.Failure -> {
            feedback.error(Res.string.setup_finish_pending_error, completeResult.error.message)
            return
        }
        is ApiResult.Ok -> Unit
    }

    val basicsResult: ApiResult<Unit> =
        applySetupBasics(
            channelsApi = channelsApi,
            channelSettingsApi = channelSettingsApi,
            prefix = pending.prefix,
            locale = pending.locale,
            timezone = pending.timezone,
            botLinePrefix = pending.botLinePrefix,
            platformBotConnected = pending.platformBotConnected,
        )
    when (basicsResult) {
        is ApiResult.Failure -> feedback.error(Res.string.setup_finish_pending_error, basicsResult.error.message)
        is ApiResult.Ok -> pendingStore.write(null)
    }
}
