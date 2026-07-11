// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Reads a channel's moderators (badge-sourced) and channel editors (Helix editors) from Twitch and projects
/// them to a management snapshot, resolving each Twitch user to a local <c>User</c> via get-or-create. Reports
/// which sources it could read so the reconciler prunes only what it trusts (roles-permissions §4). Shared by the
/// onboarding seed and the periodic reconcile so their sourcing never drifts.
/// </summary>
public sealed class TwitchManagementSnapshotBuilder(
    IUserService users,
    ITwitchModeratorsApi moderators,
    ITwitchChannelsApi channels,
    ILogger<TwitchManagementSnapshotBuilder> logger
) : ITwitchManagementSnapshotBuilder
{
    public async Task<ManagementSnapshot> BuildAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        // A member that resolves to both a moderator and an editor is recorded once at the higher (editor) role —
        // the map is keyed by user, and the editor pass overwrites the moderator entry.
        Dictionary<Guid, TwitchManagementMember> byUser = [];
        HashSet<MembershipSource> authoritative = [];

        // ── Moderators (TwitchBadge) ──────────────────────────────────────────
        // A source is authoritative FOR PRUNING only when its snapshot is PROVABLY COMPLETE: every page read
        // AND every member resolved to a local user. A partial snapshot — a failed page, a channel with more
        // than one page (>100) of moderators, or a transient get-or-create miss — must NOT prune, or the
        // reconcile silently strips a real moderator's role (they lose every mod tool; every
        // `[RequireAction("moderation:*")]` then 403s and their dashboard goes blank). Better to keep a stale
        // grant one extra cycle than to wrongly revoke a live one.
        bool modsComplete = true;
        string? cursor = null;
        int pageGuard = 0;
        do
        {
            Result<TwitchPage<TwitchModerator>> page = await moderators.GetModeratorsAsync(
                broadcasterId,
                new TwitchPageRequest(After: cursor),
                ct
            );
            if (page.IsFailure)
            {
                logger.LogWarning(
                    "Management snapshot: reading moderators for {BroadcasterId} failed: {Error} ({Code}) — moderator roles left intact (not pruned this run)",
                    broadcasterId,
                    page.ErrorMessage,
                    page.ErrorCode
                );
                modsComplete = false;
                break;
            }

            foreach (TwitchModerator mod in page.Value.Items)
            {
                Guid? userId = await ResolveUserIdAsync(
                    mod.UserId,
                    mod.UserLogin,
                    mod.UserName ?? mod.UserLogin,
                    ct
                );
                if (userId is Guid id)
                    byUser[id] = new TwitchManagementMember(
                        id,
                        mod.UserId,
                        ManagementRole.Moderator,
                        MembershipSource.TwitchBadge
                    );
                else
                    // An unresolved member means the snapshot doesn't fully represent the channel's mods;
                    // do not treat it as authoritative for pruning this run.
                    modsComplete = false;
            }

            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor) && ++pageGuard < 100);

        if (modsComplete)
            authoritative.Add(MembershipSource.TwitchBadge);
        else
            logger.LogWarning(
                "Management snapshot: moderator snapshot for {BroadcasterId} was incomplete (a failed page or unresolved member) — moderator roles left intact (not pruned this run)",
                broadcasterId
            );

        // ── Channel editors (HelixEditors) ────────────────────────────────────
        Result<IReadOnlyList<TwitchChannelEditor>> editorsResult =
            await channels.GetChannelEditorsAsync(broadcasterId, ct);
        if (editorsResult.IsSuccess)
        {
            bool editorsComplete = true;
            foreach (TwitchChannelEditor editor in editorsResult.Value)
            {
                // Editors expose only the Twitch user id + display name (no login); fall back to the display
                // name as the username so get-or-create can still mint the row.
                Guid? userId = await ResolveUserIdAsync(
                    editor.UserId,
                    editor.UserName,
                    editor.UserName,
                    ct
                );
                if (userId is Guid id)
                    byUser[id] = new TwitchManagementMember(
                        id,
                        editor.UserId,
                        ManagementRole.Editor,
                        MembershipSource.HelixEditors
                    );
                else
                    editorsComplete = false;
            }

            if (editorsComplete)
                authoritative.Add(MembershipSource.HelixEditors);
            else
                logger.LogWarning(
                    "Management snapshot: editor snapshot for {BroadcasterId} was incomplete (an unresolved editor) — editor roles left intact (not pruned this run)",
                    broadcasterId
                );
        }
        else
        {
            logger.LogWarning(
                "Management snapshot: reading channel editors for {BroadcasterId} failed: {Error} ({Code}) — editor roles left intact (not pruned this run)",
                broadcasterId,
                editorsResult.ErrorMessage,
                editorsResult.ErrorCode
            );
        }

        return new ManagementSnapshot([.. byUser.Values], authoritative);
    }

    private async Task<Guid?> ResolveUserIdAsync(
        string twitchUserId,
        string username,
        string displayName,
        CancellationToken ct
    )
    {
        Result<UserDto> user = await users.GetOrCreateAsync(
            twitchUserId,
            username,
            displayName,
            cancellationToken: ct
        );
        if (user.IsFailure)
        {
            logger.LogWarning(
                "Management snapshot: could not resolve Twitch user {TwitchUserId}: {Error}",
                twitchUserId,
                user.ErrorMessage
            );
            return null;
        }

        return Guid.TryParse(user.Value.Id, out Guid id) ? id : null;
    }
}
