// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.attention.ui

import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.core.time.ClockTime
import bot.nomnomz.dashboard.feature.integrations.ui.providerDisplayName
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.attention_audio_source_missing_live_message
import nomnomzbot.composeapp.generated.resources.attention_audio_source_missing_live_title
import nomnomzbot.composeapp.generated.resources.attention_audio_source_missing_message
import nomnomzbot.composeapp.generated.resources.attention_audio_source_missing_title
import nomnomzbot.composeapp.generated.resources.attention_automod_delete_failed_message
import nomnomzbot.composeapp.generated.resources.attention_automod_delete_failed_title
import nomnomzbot.composeapp.generated.resources.attention_bot_not_moderator_message
import nomnomzbot.composeapp.generated.resources.attention_bot_not_moderator_title
import nomnomzbot.composeapp.generated.resources.attention_eventsub_unauthorized_message
import nomnomzbot.composeapp.generated.resources.attention_eventsub_unauthorized_title
import nomnomzbot.composeapp.generated.resources.attention_held_category_unknown
import nomnomzbot.composeapp.generated.resources.attention_held_many_from_user_message
import nomnomzbot.composeapp.generated.resources.attention_held_many_message
import nomnomzbot.composeapp.generated.resources.attention_held_single_from_user_message
import nomnomzbot.composeapp.generated.resources.attention_held_single_message
import nomnomzbot.composeapp.generated.resources.attention_held_title_many
import nomnomzbot.composeapp.generated.resources.attention_held_title_one
import nomnomzbot.composeapp.generated.resources.attention_integration_expired_message
import nomnomzbot.composeapp.generated.resources.attention_integration_reauth_title
import nomnomzbot.composeapp.generated.resources.attention_integration_decrypt_failed_message
import nomnomzbot.composeapp.generated.resources.attention_integration_refresh_failed_message
import nomnomzbot.composeapp.generated.resources.attention_integration_unusable_message
import nomnomzbot.composeapp.generated.resources.attention_heat_timeout_failed_message
import nomnomzbot.composeapp.generated.resources.attention_heat_timeout_failed_title
import nomnomzbot.composeapp.generated.resources.attention_report_message
import nomnomzbot.composeapp.generated.resources.attention_report_title
import nomnomzbot.composeapp.generated.resources.attention_unban_request_message
import nomnomzbot.composeapp.generated.resources.attention_unban_request_title
import nomnomzbot.composeapp.generated.resources.attention_scope_missing_message
import nomnomzbot.composeapp.generated.resources.attention_scope_missing_title
import nomnomzbot.composeapp.generated.resources.attention_scope_missing_topics_message
import nomnomzbot.composeapp.generated.resources.attention_song_lost_message
import nomnomzbot.composeapp.generated.resources.attention_security_access_granted_message
import nomnomzbot.composeapp.generated.resources.attention_security_access_granted_title
import nomnomzbot.composeapp.generated.resources.attention_security_impersonation_ended_message
import nomnomzbot.composeapp.generated.resources.attention_security_impersonation_ended_title
import nomnomzbot.composeapp.generated.resources.attention_security_impersonation_started_message
import nomnomzbot.composeapp.generated.resources.attention_security_impersonation_started_title
import nomnomzbot.composeapp.generated.resources.attention_song_lost_title_many
import nomnomzbot.composeapp.generated.resources.attention_song_lost_title_one
import nomnomzbot.composeapp.generated.resources.attention_song_lost_unnamed_message
import nomnomzbot.composeapp.generated.resources.attention_source_name_unban_requests
import nomnomzbot.composeapp.generated.resources.attention_source_name_unknown
import nomnomzbot.composeapp.generated.resources.attention_source_unavailable_message
import nomnomzbot.composeapp.generated.resources.attention_source_unavailable_title
import nomnomzbot.composeapp.generated.resources.attention_spotify_blocked_message
import nomnomzbot.composeapp.generated.resources.attention_spotify_blocked_title
import nomnomzbot.composeapp.generated.resources.attention_unknown_title
import nomnomzbot.composeapp.generated.resources.attention_unmanaged_rewards_message
import nomnomzbot.composeapp.generated.resources.attention_unmanaged_rewards_pending_message
import nomnomzbot.composeapp.generated.resources.attention_unmanaged_rewards_title_many
import nomnomzbot.composeapp.generated.resources.attention_unmanaged_rewards_title_one
import nomnomzbot.composeapp.generated.resources.attention_webhook_disabled_message
import nomnomzbot.composeapp.generated.resources.attention_webhook_disabled_title
import nomnomzbot.composeapp.generated.resources.attention_webhook_failing_message
import nomnomzbot.composeapp.generated.resources.attention_webhook_failing_title
import nomnomzbot.composeapp.generated.resources.attention_widget_build_failed_message
import nomnomzbot.composeapp.generated.resources.attention_widget_build_failed_nothing_live_message
import nomnomzbot.composeapp.generated.resources.attention_widget_build_failed_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The backend sends an action-required item as resource KEYS plus named parameters, never prose (translations
// never live in the backend). This file is the one place those keys become text: [attentionTitleOf] and
// [attentionMessageOf] pick the string resource and its arguments (pure, so the mapping is unit-tested), and
// [rememberText] renders it in the active locale.

/** One argument of a localized attention string: a literal value, or another resource rendered first. */
sealed interface AttentionArg {
    data class Literal(val value: String) : AttentionArg

    data class Resource(val resource: StringResource) : AttentionArg
}

/** A string resource and the arguments it formats in. */
data class AttentionText(val resource: StringResource, val args: List<AttentionArg> = emptyList())

/** The title for [item]. An unknown key still renders an honest generic title, never an empty row. */
fun attentionTitleOf(item: ActionRequiredItem, now: Instant = Clock.System.now()): AttentionText {
    val many: Boolean = item.count > 1
    val count: AttentionArg = literal(item.count.toString())
    return when (item.titleKey) {
        "attention_integration_reauth_title" ->
            AttentionText(
                Res.string.attention_integration_reauth_title,
                listOf(literal(providerDisplayName(item.param("provider")))),
            )
        "attention_held_title" ->
            if (many) {
                AttentionText(Res.string.attention_held_title_many, listOf(count))
            } else {
                AttentionText(Res.string.attention_held_title_one)
            }
        "attention_unmanaged_rewards_title" ->
            if (many) {
                AttentionText(Res.string.attention_unmanaged_rewards_title_many, listOf(count))
            } else {
                AttentionText(Res.string.attention_unmanaged_rewards_title_one)
            }
        "attention_report_title" ->
            AttentionText(Res.string.attention_report_title, listOf(literal(item.param("username"))))
        "attention_unban_request_title" ->
            AttentionText(Res.string.attention_unban_request_title, listOf(literal(item.param("username"))))
        "attention_scope_missing_title" ->
            AttentionText(Res.string.attention_scope_missing_title, listOf(literal(item.param("scope"))))
        "attention_eventsub_unauthorized_title" -> AttentionText(Res.string.attention_eventsub_unauthorized_title)
        "attention_audio_source_missing_title" -> AttentionText(Res.string.attention_audio_source_missing_title)
        "attention_audio_source_missing_live_title" -> AttentionText(Res.string.attention_audio_source_missing_live_title)
        "attention_widget_build_failed_title" ->
            AttentionText(Res.string.attention_widget_build_failed_title, listOf(literal(item.param("widgetName"))))
        "attention_webhook_disabled_title" ->
            AttentionText(Res.string.attention_webhook_disabled_title, listOf(literal(item.param("endpointName"))))
        "attention_webhook_failing_title" ->
            AttentionText(Res.string.attention_webhook_failing_title, listOf(literal(item.param("endpointName"))))
        "attention_security_impersonation_started_title" ->
            AttentionText(
                Res.string.attention_security_impersonation_started_title,
                listOf(literal(item.param("targetName"))),
            )
        "attention_security_impersonation_ended_title" ->
            AttentionText(
                Res.string.attention_security_impersonation_ended_title,
                listOf(literal(item.param("targetName"))),
            )
        "attention_security_access_granted_title" -> AttentionText(Res.string.attention_security_access_granted_title)
        "attention_song_lost_title" ->
            if (many) {
                AttentionText(Res.string.attention_song_lost_title_many, listOf(count))
            } else {
                AttentionText(Res.string.attention_song_lost_title_one)
            }
        "attention_heat_timeout_failed_title" ->
            AttentionText(Res.string.attention_heat_timeout_failed_title, listOf(literal(item.param("username"))))
        "attention_automod_delete_failed_title" -> AttentionText(Res.string.attention_automod_delete_failed_title)
        "attention_bot_not_moderator_title" ->
            AttentionText(Res.string.attention_bot_not_moderator_title, listOf(literal(item.param("botName"))))
        "attention_spotify_blocked_title" ->
            AttentionText(
                Res.string.attention_spotify_blocked_title,
                listOf(literal(ClockTime.of(item.param("until"), now).orEmpty())),
            )
        "attention_source_unavailable_title" ->
            AttentionText(
                Res.string.attention_source_unavailable_title,
                listOf(AttentionArg.Resource(sourceNameOf(item.param("source")))),
            )
        else -> AttentionText(Res.string.attention_unknown_title)
    }
}

/** The explanatory line for [item], or null for an unknown key (the title alone is then shown). */
fun attentionMessageOf(item: ActionRequiredItem): AttentionText? =
    when (item.messageKey) {
        "attention_integration_expired_message" -> AttentionText(Res.string.attention_integration_expired_message)
        "attention_integration_refresh_failed_message" ->
            AttentionText(
                Res.string.attention_integration_refresh_failed_message,
                listOf(literal(item.param("failureCount"))),
            )
        "attention_integration_unusable_message" -> AttentionText(Res.string.attention_integration_unusable_message)
        "attention_integration_decrypt_failed_message" ->
            AttentionText(Res.string.attention_integration_decrypt_failed_message)
        "attention_security_impersonation_started_message" ->
            AttentionText(
                Res.string.attention_security_impersonation_started_message,
                listOf(literal(item.param("operatorName")), literal(item.param("reason"))),
            )
        "attention_security_impersonation_ended_message" ->
            AttentionText(
                Res.string.attention_security_impersonation_ended_message,
                listOf(literal(item.param("operatorName")), literal(item.param("reason"))),
            )
        "attention_security_access_granted_message" ->
            AttentionText(
                Res.string.attention_security_access_granted_message,
                listOf(literal(item.param("operatorName"))),
            )
        "attention_held_single_from_user_message" ->
            AttentionText(
                Res.string.attention_held_single_from_user_message,
                listOf(literal(item.param("username")), heldCategory(item)),
            )
        "attention_held_single_message" ->
            AttentionText(Res.string.attention_held_single_message, listOf(heldCategory(item)))
        "attention_held_many_from_user_message" ->
            AttentionText(
                Res.string.attention_held_many_from_user_message,
                listOf(literal(item.param("count")), literal(item.param("username"))),
            )
        "attention_held_many_message" ->
            AttentionText(Res.string.attention_held_many_message, listOf(literal(item.param("count"))))
        "attention_report_message" ->
            AttentionText(Res.string.attention_report_message, listOf(literal(item.param("reason"))))
        "attention_unban_request_message" ->
            AttentionText(Res.string.attention_unban_request_message, listOf(literal(item.param("text"))))
        "attention_unmanaged_rewards_message" -> AttentionText(Res.string.attention_unmanaged_rewards_message)
        "attention_unmanaged_rewards_pending_message" ->
            AttentionText(
                Res.string.attention_unmanaged_rewards_pending_message,
                listOf(literal(item.param("pendingCount"))),
            )
        "attention_scope_missing_topics_message" ->
            AttentionText(Res.string.attention_scope_missing_topics_message, listOf(literal(item.param("topics"))))
        "attention_scope_missing_message" -> AttentionText(Res.string.attention_scope_missing_message)
        "attention_eventsub_unauthorized_message" ->
            AttentionText(Res.string.attention_eventsub_unauthorized_message, listOf(literal(item.param("topics"))))
        "attention_audio_source_missing_message" -> AttentionText(Res.string.attention_audio_source_missing_message)
        "attention_audio_source_missing_live_message" -> AttentionText(Res.string.attention_audio_source_missing_live_message)
        "attention_widget_build_failed_message" ->
            AttentionText(Res.string.attention_widget_build_failed_message, listOf(literal(item.param("version"))))
        "attention_widget_build_failed_nothing_live_message" ->
            AttentionText(
                Res.string.attention_widget_build_failed_nothing_live_message,
                listOf(literal(item.param("version"))),
            )
        "attention_webhook_disabled_message" ->
            AttentionText(Res.string.attention_webhook_disabled_message, listOf(literal(item.param("failureCount"))))
        "attention_webhook_failing_message" ->
            AttentionText(Res.string.attention_webhook_failing_message, listOf(literal(item.param("failureCount"))))
        "attention_song_lost_message" ->
            AttentionText(
                Res.string.attention_song_lost_message,
                listOf(literal(item.param("trackName")), literal(item.param("requestedBy"))),
            )
        "attention_song_lost_unnamed_message" -> AttentionText(Res.string.attention_song_lost_unnamed_message)
        "attention_heat_timeout_failed_message" ->
            AttentionText(Res.string.attention_heat_timeout_failed_message, listOf(literal(item.param("error"))))
        "attention_automod_delete_failed_message" ->
            AttentionText(
                Res.string.attention_automod_delete_failed_message,
                listOf(literal(item.param("ruleName")), literal(item.param("userName")), literal(item.param("count"))),
            )
        "attention_bot_not_moderator_message" ->
            AttentionText(Res.string.attention_bot_not_moderator_message, listOf(literal(item.param("botName"))))
        "attention_spotify_blocked_message" -> AttentionText(Res.string.attention_spotify_blocked_message)
        "attention_source_unavailable_message" ->
            AttentionText(Res.string.attention_source_unavailable_message, listOf(literal(item.param("reason"))))
        else -> null
    }

/** Renders this text in the active locale, resolving any nested resource arguments first. */
@Composable
fun AttentionText.rememberText(): String {
    val resolved: List<String> =
        args.map { arg ->
            when (arg) {
                is AttentionArg.Literal -> arg.value
                is AttentionArg.Resource -> stringResource(arg.resource)
            }
        }
    return stringResource(resource, *resolved.toTypedArray())
}

private fun literal(value: String): AttentionArg = AttentionArg.Literal(value)

// A readable name for the check that failed. A source key with no name here still renders honestly.
private fun sourceNameOf(sourceKey: String): StringResource =
    when (sourceKey) {
        "unban_requests" -> Res.string.attention_source_name_unban_requests
        else -> Res.string.attention_source_name_unknown
    }

private fun heldCategory(item: ActionRequiredItem): AttentionArg =
    item.parameters["category"]?.takeIf { it.isNotBlank() }?.let(::literal)
        ?: AttentionArg.Resource(Res.string.attention_held_category_unknown)

private fun ActionRequiredItem.param(name: String): String = parameters[name].orEmpty()
