// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Default <see cref="IActAsMembershipOverlay"/>: the same rule the channel list applies to a user's own login
/// (an onboarded channel they moderate on Twitch earns the Moderator role), evaluated with the impersonated
/// user's own Twitch token and never written. Scoped: the Twitch moderated list is fetched at most once per
/// request, however many authorization checks ask.
/// </summary>
public sealed class ActAsMembershipOverlay(
    ICurrentUserService currentUser,
    IApplicationDbContext db,
    IChannelAccessService channelAccess,
    ITwitchModeratorsApi moderators
) : IActAsMembershipOverlay
{
    private HashSet<string>? _moderatedTwitchIds;

    public async Task<ManagementRole?> ResolveAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (currentUser.Impersonation?.SubjectUserId != userId)
            return null;

        string? twitchChannelId = await db
            .Channels.Where(c => c.Id == broadcasterId && c.IsOnboarded)
            .Select(c => c.TwitchChannelId)
            .FirstOrDefaultAsync(cancellationToken);
        if (twitchChannelId is null)
            return null;

        HashSet<string> moderated = await ModeratedTwitchIdsAsync(userId, cancellationToken);
        return moderated.Contains(twitchChannelId) ? ManagementRole.Moderator : null;
    }

    private async Task<HashSet<string>> ModeratedTwitchIdsAsync(Guid userId, CancellationToken ct)
    {
        if (_moderatedTwitchIds is not null)
            return _moderatedTwitchIds;

        HashSet<string> ids = new(StringComparer.Ordinal);
        Guid ownChannel = await channelAccess.ResolveOwnChannelAsync(userId.ToString(), ct);
        if (ownChannel != Guid.Empty)
        {
            Result<TwitchPage<TwitchModeratedChannel>> moderated =
                await moderators.GetModeratedChannelsAsync(ownChannel, new(), ct);
            if (moderated.IsSuccess)
                ids.UnionWith(moderated.Value.Items.Select(m => m.BroadcasterId));
        }

        _moderatedTwitchIds = ids;
        return ids;
    }
}
