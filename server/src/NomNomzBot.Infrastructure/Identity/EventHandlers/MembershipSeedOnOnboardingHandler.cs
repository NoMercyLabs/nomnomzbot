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
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity.EventHandlers;

/// <summary>
/// Onboarding seed job (Identity / roles-permissions domain): when a channel finishes onboarding, build the
/// Plane-B management snapshot from Twitch — moderators (badge-sourced) + channel editors (Helix editors) —
/// and reconcile the channel's <c>ChannelMemberships</c> so the dashboard's roles screen is populated. Each
/// member is resolved to a local <c>User</c> via get-or-create (a viewer is a non-setup User), then handed to
/// <see cref="IMembershipService.SyncManagementFromTwitchAsync"/>, which idempotently upserts + prunes only the
/// synced rows (Owner / bot-grant rows untouched). Independently resilient — caught + logged, never propagated,
/// so it cannot affect the other onboarding seed jobs. Safe to run on every onboarding + backfill.
/// </summary>
public sealed class MembershipSeedOnOnboardingHandler(
    IMembershipService membership,
    IUserService users,
    ITwitchModeratorsApi moderators,
    ITwitchChannelsApi channels,
    ILogger<MembershipSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (memberships): syncing management roles from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            List<TwitchManagementMember> snapshot = await BuildSnapshotAsync(
                @event.BroadcasterId,
                ct
            );

            Result result = await membership.SyncManagementFromTwitchAsync(
                @event.BroadcasterId,
                snapshot,
                ct
            );

            if (result.IsSuccess)
                logger.LogInformation(
                    "Onboarding seed (memberships): completed for {BroadcasterId} ({Count} management member(s))",
                    @event.BroadcasterId,
                    snapshot.Count
                );
            else
                logger.LogWarning(
                    "Onboarding seed (memberships): sync returned a failure for {BroadcasterId}: {Error} ({Code})",
                    @event.BroadcasterId,
                    result.ErrorMessage,
                    result.ErrorCode
                );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (memberships): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }

    /// <summary>
    /// Pulls moderators + editors from Helix and projects them to the management snapshot, resolving each
    /// Twitch user to a local <c>User</c> Guid via get-or-create. A member that resolves to both a moderator
    /// and an editor is recorded once at the higher (editor) role — the snapshot is keyed by user, and a later
    /// duplicate would otherwise overwrite the row in the reconciler.
    /// </summary>
    private async Task<List<TwitchManagementMember>> BuildSnapshotAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        Dictionary<Guid, TwitchManagementMember> byUser = [];

        Result<TwitchPage<TwitchModerator>> modsResult = await moderators.GetModeratorsAsync(
            broadcasterId,
            new TwitchPageRequest(),
            ct
        );
        if (modsResult.IsSuccess)
            foreach (TwitchModerator mod in modsResult.Value.Items)
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
            }

        Result<IReadOnlyList<TwitchChannelEditor>> editorsResult =
            await channels.GetChannelEditorsAsync(broadcasterId, ct);
        if (editorsResult.IsSuccess)
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
            }

        return [.. byUser.Values];
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
            ct
        );
        if (user.IsFailure)
        {
            logger.LogWarning(
                "Onboarding seed (memberships): could not resolve Twitch user {TwitchUserId}: {Error}",
                twitchUserId,
                user.ErrorMessage
            );
            return null;
        }

        return Guid.TryParse(user.Value.Id, out Guid id) ? id : null;
    }
}
