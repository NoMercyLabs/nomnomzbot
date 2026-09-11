// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.component.PickerRef
import bot.nomnomz.dashboard.core.designsystem.component.SearchPickerField
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityTrustLevel
import bot.nomnomz.dashboard.feature.community.state.CommunityController
import bot.nomnomz.dashboard.feature.community.state.CommunityRole
import bot.nomnomz.dashboard.feature.community.state.CommunityState
import bot.nomnomz.dashboard.feature.community.state.ViewerProfileController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.community_banned
import nomnomzbot.composeapp.generated.resources.community_directory_search_hint
import nomnomzbot.composeapp.generated.resources.community_directory_subtitle
import nomnomzbot.composeapp.generated.resources.community_directory_title
import nomnomzbot.composeapp.generated.resources.community_error
import nomnomzbot.composeapp.generated.resources.community_loading
import nomnomzbot.composeapp.generated.resources.community_no_internal_id
import nomnomzbot.composeapp.generated.resources.community_no_members_in_role
import nomnomzbot.composeapp.generated.resources.community_open_profile
import nomnomzbot.composeapp.generated.resources.community_page_indicator
import nomnomzbot.composeapp.generated.resources.community_page_indicator_total
import nomnomzbot.composeapp.generated.resources.community_pager_next
import nomnomzbot.composeapp.generated.resources.community_pager_prev
import nomnomzbot.composeapp.generated.resources.community_retry
import nomnomzbot.composeapp.generated.resources.community_role_all
import nomnomzbot.composeapp.generated.resources.community_role_follower
import nomnomzbot.composeapp.generated.resources.community_role_moderator
import nomnomzbot.composeapp.generated.resources.community_role_vip
import nomnomzbot.composeapp.generated.resources.community_row_description
import nomnomzbot.composeapp.generated.resources.community_search_label
import nomnomzbot.composeapp.generated.resources.community_search_placeholder
import nomnomzbot.composeapp.generated.resources.community_trust_moderator
import nomnomzbot.composeapp.generated.resources.community_trust_subscriber
import nomnomzbot.composeapp.generated.resources.community_trust_viewer
import nomnomzbot.composeapp.generated.resources.community_trust_vip
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Community page (frontend-ia.md §3, owner punch list 2026-09-08 §3): two jobs, two views, exactly the
// split the owner asked for (mirroring how Moderation is already split by job instead of topic) — a search-
// first DIRECTORY for finding someone fast, and a full single-person PROFILE opened from a Directory row. This
// replaces the old page's role-filter-tabs-on-one-flat-list structure: switching a filter used to just reorder
// the same rows with nothing about the page's STRUCTURE changing — now the Directory's job is finding someone,
// full stop, and every management action on that person (trust, ban/VIP, TTS voice, overrides, permits, GDPR)
// lives on their Profile, never duplicated on both views. [openProfileUserId] is this composable's own local
// nav state (no ShellNav route exists for a per-person drill-down); it is the internal `User.Id` Guid a
// Directory row already resolved.
@Composable
fun CommunityScreen(
    controller: CommunityController,
    profileController: ViewerProfileController,
    role: ManagementRole?,
) {
    var openProfileUserId: String? by remember { mutableStateOf(null) }

    val target: String? = openProfileUserId
    if (target != null) {
        ViewerProfileScreen(
            controller = profileController,
            userId = target,
            role = role,
            onBack = { openProfileUserId = null },
        )
    } else {
        DirectoryScreen(
            controller = controller,
            onOpenProfile = { userId -> openProfileUserId = userId },
        )
    }
}

// Directory is deliberately read-only (owner punch list §3A: "a browse/search list for finding someone
// fast") — every management action moved to the person's Profile, so there is no [ManagementRole] write floor
// to gate here.
@Composable
private fun DirectoryScreen(
    controller: CommunityController,
    onOpenProfile: (userId: String) -> Unit,
) {
    val state: CommunityState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()

    // A viewer found via the "reach a person beyond this page" search, resolved to their real member state so
    // the row can tell whether they even have a Profile to open (a local User row / internalUserId).
    var pickedViewer: PickerRef? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: CommunityState = state) {
            is CommunityState.Loading -> CenteredMessage(stringResource(Res.string.community_loading))
            is CommunityState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            // A channel with zero known members today still gets the full Directory shell — search
            // reaches ANY known viewer, not just the current page (owner punch list §3A), so hiding it
            // behind a bare "empty" message would defeat the one thing this state should never block.
            is CommunityState.Empty ->
                DirectoryList(
                    members = emptyList(),
                    role = CommunityRole.All,
                    page = 1,
                    hasPrev = false,
                    hasMore = false,
                    total = 0,
                    pickedViewer = pickedViewer,
                    onSelectRole = { newRole -> scope.launch { controller.selectRole(newRole) } },
                    onPrevPage = { scope.launch { controller.prevPage() } },
                    onNextPage = { scope.launch { controller.nextPage() } },
                    onSearchViewers = { query -> controller.searchViewers(query) },
                    onPickViewer = { picked -> pickedViewer = picked },
                    onClearPicked = { pickedViewer = null },
                    onFetchMember = { twitchId -> controller.memberDetail(twitchId) },
                    onOpenProfile = onOpenProfile,
                )
            is CommunityState.Ready ->
                DirectoryList(
                    members = current.members,
                    role = current.role,
                    page = current.page,
                    hasPrev = current.hasPrev,
                    hasMore = current.hasMore,
                    total = current.total,
                    pickedViewer = pickedViewer,
                    onSelectRole = { newRole -> scope.launch { controller.selectRole(newRole) } },
                    onPrevPage = { scope.launch { controller.prevPage() } },
                    onNextPage = { scope.launch { controller.nextPage() } },
                    onSearchViewers = { query -> controller.searchViewers(query) },
                    onPickViewer = { picked -> pickedViewer = picked },
                    onClearPicked = { pickedViewer = null },
                    onFetchMember = { twitchId -> controller.memberDetail(twitchId) },
                    onOpenProfile = onOpenProfile,
                )
        }
    }
}

@Composable
private fun DirectoryList(
    members: List<CommunityMember>,
    role: String,
    page: Int,
    hasPrev: Boolean,
    hasMore: Boolean,
    total: Int?,
    pickedViewer: PickerRef?,
    onSelectRole: (String) -> Unit,
    onPrevPage: () -> Unit,
    onNextPage: () -> Unit,
    onSearchViewers: suspend (String) -> List<PickerOption>,
    onPickViewer: (PickerRef) -> Unit,
    onClearPicked: () -> Unit,
    onFetchMember: suspend (twitchId: String) -> CommunityMember?,
    onOpenProfile: (userId: String) -> Unit,
) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        item(key = "page-header") {
            PageHeader(
                title = stringResource(Res.string.community_directory_title),
                subtitle = stringResource(Res.string.community_directory_subtitle),
            )
        }
        // Search is the PROMINENT, first-class tool (owner punch list §3A: "a prominent search box") — it
        // reaches any known viewer, not just the current page, and is the primary way to find someone fast.
        item(key = "search") {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                SearchPickerField(
                    search = onSearchViewers,
                    selected = pickedViewer,
                    onSelect = onPickViewer,
                    onClear = onClearPicked,
                    label = stringResource(Res.string.community_search_label),
                    placeholder = stringResource(Res.string.community_search_placeholder),
                )
                Text(
                    text = stringResource(Res.string.community_directory_search_hint),
                    style = LocalTypography.current.xs,
                    color = LocalTokens.current.mutedForeground,
                )
                pickedViewer?.let { picked ->
                    Card(modifier = Modifier.fillMaxWidth()) {
                        SearchedPersonRow(
                            twitchId = picked.id,
                            fallbackName = picked.name,
                            onFetchMember = onFetchMember,
                            onOpenProfile = onOpenProfile,
                        )
                    }
                }
            }
        }
        // A compact SECONDARY refinement, not the page's primary structure (the mess the owner flagged: role
        // tabs used to BE the whole page). Default stays "all", sorted most-recently-active first.
        item(key = "role-filter") { RoleFilter(role = role, onSelectRole = onSelectRole) }
        item(key = "members-card") {
            Card(modifier = Modifier.fillMaxWidth()) {
                if (members.isEmpty()) {
                    Text(
                        text = stringResource(Res.string.community_no_members_in_role),
                        style = LocalTypography.current.sm,
                        color = LocalTokens.current.mutedForeground,
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = spacing.s4, vertical = spacing.s4),
                    )
                } else {
                    members.forEachIndexed { index, member ->
                        DirectoryRow(member = member, onOpenProfile = onOpenProfile)
                        if (index < members.lastIndex) Separator()
                    }
                }
            }
        }
        item(key = "pager") {
            Pager(page = page, total = total, hasPrev = hasPrev, hasMore = hasMore, onPrev = onPrevPage, onNext = onNextPage)
        }
    }
}

// A row found via search. Search only returns a Twitch id, but a Profile is addressed by the internal `User.Id`
// Guid — so this row resolves the person's real member state first (same [DirectoryRow.canOpen] rule: no local
// User row yet means no Profile to open yet) rather than guessing at a Guid or opening the wrong resource.
@Composable
private fun SearchedPersonRow(
    twitchId: String,
    fallbackName: String,
    onFetchMember: suspend (twitchId: String) -> CommunityMember?,
    onOpenProfile: (userId: String) -> Unit,
) {
    var resolved: CommunityMember? by remember(twitchId) { mutableStateOf(null) }
    LaunchedEffect(twitchId) { resolved = onFetchMember(twitchId) }

    val member: CommunityMember = resolved ?: CommunityMember(id = twitchId, displayName = fallbackName)
    val name: String = memberName(member)
    val canOpen: Boolean = member.internalUserId != null

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = LocalSpacing.current.s4, vertical = LocalSpacing.current.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = name, style = LocalTypography.current.base, color = LocalTokens.current.cardForeground, maxLines = 2, overflow = TextOverflow.Ellipsis)
        if (canOpen) {
            TextButton(onClick = { onOpenProfile(member.internalUserId!!) }) {
                Text(text = stringResource(Res.string.community_open_profile), color = LocalTokens.current.primary, maxLines = 1)
            }
        } else {
            Text(
                text = stringResource(Res.string.community_no_internal_id),
                style = LocalTypography.current.xs,
                color = LocalTokens.current.mutedForeground,
            )
        }
    }
}

// The role filter: all / followers / VIPs / moderators. Kept as a compact secondary control (not the page's
// primary organizing structure the owner flagged as the problem).
@Composable
private fun RoleFilter(role: String, onSelectRole: (String) -> Unit) {
    val typography = LocalTypography.current
    TabsList(modifier = Modifier.fillMaxWidth()) {
        CommunityRole.tabs.forEach { tab ->
            TabsTrigger(
                selected = tab == role,
                onClick = { if (tab != role) onSelectRole(tab) },
                modifier = Modifier.weight(1f),
            ) {
                Text(text = stringResource(roleTabLabel(tab)), style = typography.sm, maxLines = 1)
            }
        }
    }
}

@Composable
private fun DirectoryRow(member: CommunityMember, onOpenProfile: (userId: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = memberName(member)
    val standingLabel: String = stringResource(trustLabel(member.trustLevel))
    val rowDescription: String = stringResource(Res.string.community_row_description, name, standingLabel)
    val canOpen: Boolean = member.internalUserId != null

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = standingLabel,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        if (member.isBanned) {
            DirectoryBadge(
                label = stringResource(Res.string.community_banned),
                background = tokens.destructive,
                foreground = tokens.destructiveForeground,
            )
        }
        if (canOpen) {
            TextButton(
                onClick = { onOpenProfile(member.internalUserId!!) },
                modifier = Modifier.semantics {
                    role = Role.Button
                    contentDescription = rowDescription
                },
            ) {
                Text(text = stringResource(Res.string.community_open_profile), color = tokens.primary, maxLines = 1)
            }
        } else {
            Text(
                text = stringResource(Res.string.community_no_internal_id),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
    }
}

@Composable
private fun DirectoryBadge(label: String, background: Color, foreground: Color) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(background)
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
    ) {
        Text(text = label, style = typography.xs, color = foreground, maxLines = 1)
    }
}

@Composable
private fun Pager(page: Int, total: Int?, hasPrev: Boolean, hasMore: Boolean, onPrev: () -> Unit, onNext: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s2),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        TextButton(onClick = onPrev, enabled = hasPrev) {
            Text(text = stringResource(Res.string.community_pager_prev), color = if (hasPrev) tokens.primary else tokens.mutedForeground, maxLines = 1)
        }
        Text(
            text = if (total != null) stringResource(Res.string.community_page_indicator_total, page, total) else stringResource(Res.string.community_page_indicator, page),
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.weight(1f),
            textAlign = TextAlign.Center,
        )
        TextButton(onClick = onNext, enabled = hasMore) {
            Text(text = stringResource(Res.string.community_pager_next), color = if (hasMore) tokens.primary else tokens.mutedForeground, maxLines = 1)
        }
    }
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = stringResource(Res.string.community_error, detail), style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.community_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

/** The member's best display name: display name, then login, then the raw id. */
private fun memberName(member: CommunityMember): String =
    member.displayName.takeIf { it.isNotBlank() }
        ?: member.username.takeIf { it.isNotBlank() }
        ?: member.id

/** Map a backend `trustLevel` to its localized badge label, falling back to the viewer label. */
private fun trustLabel(trustLevel: String): StringResource =
    when (trustLevel.lowercase()) {
        CommunityTrustLevel.Moderator -> Res.string.community_trust_moderator
        CommunityTrustLevel.Vip -> Res.string.community_trust_vip
        CommunityTrustLevel.Subscriber -> Res.string.community_trust_subscriber
        else -> Res.string.community_trust_viewer
    }

/** Map a role filter key to its localized label. */
private fun roleTabLabel(role: String): StringResource =
    when (role) {
        CommunityRole.Follower -> Res.string.community_role_follower
        CommunityRole.Vip -> Res.string.community_role_vip
        CommunityRole.Moderator -> Res.string.community_role_moderator
        else -> Res.string.community_role_all
    }
