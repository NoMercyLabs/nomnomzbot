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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds the journal into the per-viewer-per-channel profile (analytics.md §3.1, schema M.1 — the anonymization
/// anchor). A viewer IS a non-setup User ([[viewer-identity-is-user]]): the projection get-or-creates the Users row
/// via <see cref="IUserService.GetOrCreateAsync"/> so a rebuild re-materializes viewer identities (replay-safe),
/// then upserts the profile keyed on (broadcaster, viewer). PII snapshots come from the journal payload, so a
/// GDPR-scrubbed payload re-projects anonymized. Folds chat today; extends to the other activity events next.
/// </summary>
public sealed class ViewerProfileProjection(IApplicationDbContext db, IUserService userService)
    : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
    };

    public string Name => "viewer-profile";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId is not Guid broadcasterId)
            return Result.Success();

        JObject payload;
        try
        {
            payload = JObject.Parse(@event.PayloadJson);
        }
        catch (JsonException)
        {
            return Result.Success();
        }

        string? twitchUserId = payload["UserId"]?.Value<string>();
        if (string.IsNullOrEmpty(twitchUserId))
            return Result.Success();
        string login = payload["UserLogin"]?.Value<string>() ?? twitchUserId;
        string display = payload["UserDisplayName"]?.Value<string>() ?? login;
        bool isSubscriber = payload["IsSubscriber"]?.Value<bool?>() ?? false;

        Result<UserDto> user = await userService.GetOrCreateAsync(
            twitchUserId,
            login,
            display,
            cancellationToken
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid viewerUserId))
            return Result.Success();

        ViewerProfile profile = await GetOrCreateAsync(
            broadcasterId,
            viewerUserId,
            twitchUserId,
            cancellationToken
        );
        profile.UsernameSnapshot = login;
        profile.DisplayNameSnapshot = display;
        profile.FirstSeenAt ??= @event.OccurredAt;
        profile.LastSeenAt = @event.OccurredAt;
        profile.TotalMessages++;
        profile.IsSubscriber = isSubscriber;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ViewerProfile> rows = await (
            broadcasterId is Guid id
                ? db.ViewerProfiles.Where(p => p.BroadcasterId == id)
                : db.ViewerProfiles
        ).ToListAsync(cancellationToken);

        // Zero the folded aggregates in place (the row is the soft-delete anchor — never hard-removed on rebuild).
        foreach (ViewerProfile profile in rows)
        {
            profile.TotalWatchSeconds = 0;
            profile.TotalMessages = 0;
            profile.TotalCommandsUsed = 0;
            profile.TotalRedemptions = 0;
            profile.TotalSongRequests = 0;
            profile.FirstSeenAt = null;
            profile.LastSeenAt = null;
            profile.IsFollower = false;
            profile.IsSubscriber = false;
            profile.SubTier = null;
        }
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<ViewerProfile> GetOrCreateAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        string twitchUserId,
        CancellationToken ct
    )
    {
        ViewerProfile? profile = await db.ViewerProfiles.FirstOrDefaultAsync(
            p => p.BroadcasterId == broadcasterId && p.ViewerUserId == viewerUserId,
            ct
        );
        if (profile is null)
        {
            profile = new ViewerProfile
            {
                BroadcasterId = broadcasterId,
                ViewerUserId = viewerUserId,
                ViewerTwitchUserId = twitchUserId,
            };
            db.ViewerProfiles.Add(profile);
        }
        return profile;
    }
}
