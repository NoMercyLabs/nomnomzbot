// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.SupportPersonHistoryEntry
import bot.nomnomz.dashboard.core.network.SupportPersonSearchResult
import bot.nomnomz.dashboard.core.network.SupportPersonView
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_support_activity_error
import nomnomzbot.composeapp.generated.resources.admin_support_activity_nothing_recorded
import nomnomzbot.composeapp.generated.resources.admin_support_activity_platform_global
import nomnomzbot.composeapp.generated.resources.admin_support_audited_notice
import nomnomzbot.composeapp.generated.resources.admin_support_back
import nomnomzbot.composeapp.generated.resources.admin_support_find
import nomnomzbot.composeapp.generated.resources.admin_support_grant_reason
import nomnomzbot.composeapp.generated.resources.admin_support_history_counts
import nomnomzbot.composeapp.generated.resources.admin_support_justification_label
import nomnomzbot.composeapp.generated.resources.admin_support_no_matches
import nomnomzbot.composeapp.generated.resources.admin_support_nothing_known
import nomnomzbot.composeapp.generated.resources.admin_support_open
import nomnomzbot.composeapp.generated.resources.admin_support_person_row_type
import nomnomzbot.composeapp.generated.resources.admin_support_role_scope_platform
import nomnomzbot.composeapp.generated.resources.admin_support_search_label
import nomnomzbot.composeapp.generated.resources.admin_support_section_activity
import nomnomzbot.composeapp.generated.resources.admin_support_section_community
import nomnomzbot.composeapp.generated.resources.admin_support_section_connections
import nomnomzbot.composeapp.generated.resources.admin_support_section_entitlements
import nomnomzbot.composeapp.generated.resources.admin_support_section_history
import nomnomzbot.composeapp.generated.resources.admin_support_section_iam
import nomnomzbot.composeapp.generated.resources.admin_support_section_identities
import nomnomzbot.composeapp.generated.resources.admin_support_section_moderation
import nomnomzbot.composeapp.generated.resources.admin_support_section_trust
import nomnomzbot.composeapp.generated.resources.admin_support_tenant_count
import nomnomzbot.composeapp.generated.resources.admin_support_tier
import nomnomzbot.composeapp.generated.resources.admin_support_trust_values
import org.jetbrains.compose.resources.stringResource

/**
 * The cross-tenant support desk (S-ADMIN-7a): find one PERSON anywhere on the platform, then read the state
 * the product actually holds for them — platform roles, community standing, moderation standing and history,
 * trust/heat, entitlements, platform connections — each fact labelled with the channel it belongs to.
 *
 * Two rules shape this screen. The lookup is AUDITED, so the reason field is mandatory and the notice above
 * the one primary action says plainly what gets recorded. And a fact the system does not hold is OMITTED
 * entirely: an absent trust score renders no block at all, never a "0.0" that would read as a real measured
 * value.
 */
@Composable
internal fun SupportTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    val canLook: Boolean =
        state.supportSearch.isNotBlank() && state.supportJustification.isNotBlank()

    Column(
        modifier = Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        AppTextField(
            value = state.supportSearch,
            onValueChange = { controller.setSupportSearch(it) },
            label = stringResource(Res.string.admin_support_search_label),
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = state.supportJustification,
            onValueChange = { controller.setSupportJustification(it) },
            label = stringResource(Res.string.admin_support_justification_label),
            modifier = Modifier.fillMaxWidth(),
        )
        Text(
            text = stringResource(Res.string.admin_support_audited_notice),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        // The one full-chroma action on this screen. Everything else is a neutral text action.
        Button(onClick = { scope.launch { controller.searchPeople() } }, enabled = canLook) {
            Text(text = stringResource(Res.string.admin_support_find))
        }

        state.supportError?.let { InlineError(message = it) }
        state.supportPersonError?.let { InlineError(message = it) }

        when {
            state.supportLoading || state.supportPersonLoading -> Spinner(color = tokens.primary)
            state.supportPerson != null -> PersonRecord(
                person = state.supportPerson,
                history = state.supportHistory,
                historyLoaded = state.supportHistoryLoaded,
                historyLoading = state.supportHistoryLoading,
                historyError = state.supportHistoryError,
                onBack = { controller.closeSupportPerson() },
            )
            state.supportResults.isNotEmpty() -> Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.supportResults.forEachIndexed { index, person ->
                        PersonResultRow(
                            person = person,
                            onOpen = { scope.launch { controller.openSupportPerson(person.userId) } },
                        )
                        if (index < state.supportResults.lastIndex) Separator()
                    }
                }
            }
            state.supportSearched -> EmptyLine(stringResource(Res.string.admin_support_no_matches))
        }
    }
}

@Composable
private fun PersonResultRow(person: SupportPersonSearchResult, onOpen: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(
            modifier = Modifier.fillMaxWidth(0.7f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = resolveRowLabel(
                    primary = person.displayName,
                    secondary = person.username,
                    typeLabel = stringResource(Res.string.admin_support_person_row_type),
                    discriminatorSource = person.userId,
                ),
                style = typography.sm,
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(
                    Res.string.admin_support_tenant_count,
                    person.tenantCount.toString(),
                ),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        TextButton(onClick = onOpen) {
            Text(text = stringResource(Res.string.admin_support_open))
        }
    }
}

/**
 * One person's whole record. Each block is rendered ONLY when the backend actually returned rows for it —
 * that is the "omit what we do not have" rule, enforced here rather than by a placeholder further down.
 */
@Composable
private fun PersonRecord(
    person: SupportPersonView,
    history: List<SupportPersonHistoryEntry>,
    historyLoaded: Boolean,
    historyLoading: Boolean,
    historyError: String?,
    onBack: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    val hasAnyFact: Boolean =
        person.identities.isNotEmpty() ||
            person.platformConnections.isNotEmpty() ||
            person.iamRoles.isNotEmpty() ||
            person.communityStandings.isNotEmpty() ||
            person.moderationStandings.isNotEmpty() ||
            person.moderationHistory.isNotEmpty() ||
            person.trustScores.isNotEmpty() ||
            person.entitlements.isNotEmpty()

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onBack) {
                Text(text = stringResource(Res.string.admin_support_back))
            }
        }

        Text(
            text = resolveRowLabel(
                primary = person.displayName,
                secondary = person.username,
                typeLabel = stringResource(Res.string.admin_support_person_row_type),
                discriminatorSource = person.userId,
            ),
            style = typography.base,
            color = tokens.foreground,
        )

        if (person.identities.isNotEmpty()) {
            FactBlock(stringResource(Res.string.admin_support_section_identities)) {
                person.identities.forEach { identity ->
                    FactRow(
                        primary = identity.provider,
                        detail = identity.providerUsername,
                        discriminator = identity.providerUserId,
                    )
                }
            }
        }
        if (person.iamRoles.isNotEmpty()) {
            val platformWide: String = stringResource(Res.string.admin_support_role_scope_platform)
            FactBlock(stringResource(Res.string.admin_support_section_iam)) {
                person.iamRoles.forEach { role ->
                    FactRow(
                        primary = role.roleName,
                        detail = role.scopeChannelName ?: platformWide,
                        discriminator = role.principalId,
                    )
                }
            }
        }
        if (person.communityStandings.isNotEmpty()) {
            FactBlock(stringResource(Res.string.admin_support_section_community)) {
                person.communityStandings.forEach { standing ->
                    FactRow(
                        primary = standing.channelName,
                        detail = standing.standing,
                        discriminator = standing.broadcasterId,
                    )
                }
            }
        }
        if (person.moderationStandings.isNotEmpty()) {
            FactBlock(stringResource(Res.string.admin_support_section_moderation)) {
                person.moderationStandings.forEach { standing ->
                    FactRow(
                        primary = standing.channelName,
                        detail = "${standing.standing} · ${standing.provider}",
                        extra = standing.reason,
                        discriminator = standing.broadcasterId,
                    )
                }
            }
        }
        if (person.moderationHistory.isNotEmpty()) {
            FactBlock(stringResource(Res.string.admin_support_section_history)) {
                person.moderationHistory.forEach { history ->
                    FactRow(
                        primary = history.channelName,
                        detail = stringResource(
                            Res.string.admin_support_history_counts,
                            history.timeoutCount.toString(),
                            history.banCount.toString(),
                            history.warningCount.toString(),
                            history.messagesDeletedCount.toString(),
                        ),
                        discriminator = history.broadcasterId,
                    )
                }
            }
        }
        if (person.trustScores.isNotEmpty()) {
            FactBlock(stringResource(Res.string.admin_support_section_trust)) {
                person.trustScores.forEach { score ->
                    FactRow(
                        primary = score.channelName,
                        detail = stringResource(
                            Res.string.admin_support_trust_values,
                            score.trustScore.toString(),
                            score.heatScore.toString(),
                        ),
                        discriminator = score.broadcasterId,
                    )
                }
            }
        }
        if (person.entitlements.isNotEmpty()) {
            FactBlock(stringResource(Res.string.admin_support_section_entitlements)) {
                person.entitlements.forEach { entitlement ->
                    FactRow(
                        primary = entitlement.channelName,
                        detail = stringResource(
                            Res.string.admin_support_tier,
                            entitlement.tierKey,
                        ),
                        extra = entitlement.grants.firstOrNull()?.let { grant ->
                            stringResource(
                                Res.string.admin_support_grant_reason,
                                grant.reason,
                                grant.expiresAt,
                            )
                        },
                        discriminator = entitlement.broadcasterId,
                    )
                }
            }
        }
        if (person.platformConnections.isNotEmpty()) {
            FactBlock(stringResource(Res.string.admin_support_section_connections)) {
                person.platformConnections.forEach { connection ->
                    FactRow(
                        primary = connection.channelName,
                        detail = "${connection.provider} · ${connection.displayName}",
                        discriminator = connection.broadcasterId,
                    )
                }
            }
        }

        if (!hasAnyFact) EmptyLine(stringResource(Res.string.admin_support_nothing_known))

        ActivitySection(history = history, loaded = historyLoaded, loading = historyLoading, error = historyError)
    }
}

/**
 * What actually happened to this person, replayed from the real event journal (S-ADMIN-7b) — one row per
 * event, newest first, each naming the tenant it happened in. [loaded] tells "fetched, genuinely nothing
 * recorded" apart from "not fetched yet", so the empty state never reads as an error.
 */
@Composable
private fun ActivitySection(
    history: List<SupportPersonHistoryEntry>,
    loaded: Boolean,
    loading: Boolean,
    error: String?,
) {
    val tokens = LocalTokens.current
    val platformWide: String = stringResource(Res.string.admin_support_activity_platform_global)

    Column {
        Text(
            text = stringResource(Res.string.admin_support_section_activity),
            style = LocalTypography.current.sm,
            color = tokens.mutedForeground,
        )
        when {
            loading -> Spinner(color = tokens.primary)
            error != null ->
                InlineError(
                    message = stringResource(Res.string.admin_support_activity_error, error),
                )
            history.isNotEmpty() -> Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    history.forEachIndexed { index, entry ->
                        FactRow(
                            primary = entry.eventType,
                            detail = entry.channelName ?: platformWide,
                            discriminator = entry.eventId,
                        )
                        if (index < history.lastIndex) Separator()
                    }
                }
            }
            loaded -> EmptyLine(stringResource(Res.string.admin_support_activity_nothing_recorded))
        }
    }
}

/** A titled group of real rows. Only ever called with a non-empty [rows]. */
@Composable
private fun FactBlock(title: String, rows: @Composable () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = title, style = typography.sm, color = tokens.mutedForeground)
        Card(modifier = Modifier.fillMaxWidth()) {
            Column { rows() }
        }
    }
}

@Composable
private fun FactRow(
    primary: String,
    detail: String,
    discriminator: String,
    extra: String? = null,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(
            text = resolveRowLabel(
                primary = primary,
                secondary = detail,
                typeLabel = stringResource(Res.string.admin_support_person_row_type),
                discriminatorSource = discriminator,
            ),
            style = typography.sm,
            color = tokens.cardForeground,
        )
        Text(text = detail, style = typography.xs, color = tokens.mutedForeground)
        if (!extra.isNullOrBlank()) {
            Text(text = extra, style = typography.xs, color = tokens.mutedForeground)
        }
    }
}
