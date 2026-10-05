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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation;

/// <inheritdoc />
public sealed class OperatorModeratedChannelResolver : IOperatorModeratedChannelResolver
{
    private readonly IChannelAccessService _channelAccess;
    private readonly ITwitchModeratorsApi _moderators;
    private readonly IApplicationDbContext _db;

    public OperatorModeratedChannelResolver(
        IChannelAccessService channelAccess,
        ITwitchModeratorsApi moderators,
        IApplicationDbContext db
    )
    {
        _channelAccess = channelAccess;
        _moderators = moderators;
        _db = db;
    }

    public async Task<Result<IReadOnlyList<TwitchModeratedChannel>>> ResolveAsync(
        Guid operatorUserId,
        CancellationToken ct = default
    )
    {
        Guid operatorChannelId = await _channelAccess.ResolveOwnChannelAsync(
            operatorUserId.ToString(),
            ct
        );
        if (operatorChannelId == Guid.Empty)
            return Result.Success<IReadOnlyList<TwitchModeratedChannel>>([]);

        Result<IReadOnlyList<TwitchModeratedChannel>> moderated =
            await ResolveModeratedChannelsAsync(operatorChannelId, ct);
        if (moderated.IsFailure)
            return moderated;

        // Get Moderated Channels never lists the operator's own channel, so it is added first from the local
        // channel row (skipped while that row has no Twitch id yet).
        TwitchModeratedChannel? own = await _db
            .Channels.AsNoTracking()
            .Where(c => c.Id == operatorChannelId && c.TwitchChannelId != null)
            .Select(c => new TwitchModeratedChannel(c.TwitchChannelId!, c.Name, c.Name))
            .FirstOrDefaultAsync(ct);
        if (own is null || moderated.Value.Any(c => c.BroadcasterId == own.BroadcasterId))
            return moderated;

        return Result.Success<IReadOnlyList<TwitchModeratedChannel>>([own, .. moderated.Value]);
    }

    // Pages through every channel Twitch says the operator moderates. A first-page failure surfaces (e.g. the operator
    // token is missing user:read:moderated_channels); a later-page failure keeps what was already gathered.
    private async Task<Result<IReadOnlyList<TwitchModeratedChannel>>> ResolveModeratedChannelsAsync(
        Guid operatorChannelId,
        CancellationToken ct
    )
    {
        List<TwitchModeratedChannel> channels = [];
        string? cursor = null;
        do
        {
            Result<TwitchPage<TwitchModeratedChannel>> page =
                await _moderators.GetModeratedChannelsAsync(
                    operatorChannelId,
                    new(After: cursor),
                    ct
                );
            if (page.IsFailure)
            {
                if (channels.Count == 0)
                    return page.WithValue<IReadOnlyList<TwitchModeratedChannel>>(default!);
                break;
            }

            channels.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return Result.Success<IReadOnlyList<TwitchModeratedChannel>>(channels);
    }
}
