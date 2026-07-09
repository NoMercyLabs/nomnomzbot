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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Links every <see cref="ChannelEvent"/> to its actor's internal <see cref="User"/> in ONE set-based pass, run
/// after the channel-event-log projection rebuilds ([[viewer-identity-is-user]]). The fold leaves <c>UserId</c> null
/// and snapshots the actor's Twitch id into <c>Data.actorTwitchUserId</c>; this backfill reads those snapshots,
/// bulk get-or-creates the Users (one existing-lookup + one insert for the genuinely new ids — NO external Twitch
/// call), then issues one <c>ExecuteUpdate</c> per distinct actor (a few hundred, not ~41k) to set the FK. The
/// result: <c>ChannelEvents.UserId</c> is populated and the dashboard's distinct-viewer count is real. Idempotent —
/// re-running resolves to the same Users and re-sets the same FKs.
/// </summary>
public sealed class ChannelEventActorBackfill(IApplicationDbContext db)
{
    /// <summary>
    /// Backfills <c>UserId</c> for one tenant's channel events. Returns how many rows were linked.
    /// </summary>
    public async Task<Result<long>> BackfillAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Read the actor snapshot off every event row for the tenant. Done in C# (not a JSON SQL predicate) so the
        //    pass is provider-portable across SQLite (self-host) and Npgsql (SaaS).
        List<ChannelEventRow> rows = await db
            .ChannelEvents.Where(e => e.ChannelId == broadcasterId && e.Data != null)
            .Select(e => new ChannelEventRow(e.Id, e.Data!))
            .ToListAsync(cancellationToken);

        Dictionary<string, ActorSnapshot> actors = new(StringComparer.Ordinal);
        List<(string EventId, string TwitchUserId)> links = new(rows.Count);
        foreach (ChannelEventRow row in rows)
        {
            ActorSnapshot? actor = ParseActor(row.Data);
            if (actor is null)
                continue;
            actors.TryAdd(actor.Value.TwitchUserId, actor.Value);
            links.Add((row.Id, actor.Value.TwitchUserId));
        }

        if (links.Count == 0)
            return Result.Success(0L);

        // 2. Bulk get-or-create the Users for the distinct actor Twitch ids — one query for the ones that exist, one
        //    insert for the rest. No per-row round-trip, no external fetch.
        Dictionary<string, Guid> twitchToUserId = await ResolveUsersAsync(
            actors,
            cancellationToken
        );

        // 3. One UPDATE per distinct actor: set UserId on all of that actor's event rows. A few hundred statements
        //    for the whole history, versus a per-event lookup+save during the fold.
        long linked = 0;
        foreach (
            IGrouping<string, (string EventId, string TwitchUserId)> group in links.GroupBy(l =>
                l.TwitchUserId
            )
        )
        {
            if (!twitchToUserId.TryGetValue(group.Key, out Guid userId))
                continue;

            List<string> eventIds = group.Select(g => g.EventId).ToList();
            linked += await db
                .ChannelEvents.Where(e => eventIds.Contains(e.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.UserId, userId), cancellationToken);
        }

        return Result.Success(linked);
    }

    private async Task<Dictionary<string, Guid>> ResolveUsersAsync(
        IReadOnlyDictionary<string, ActorSnapshot> actors,
        CancellationToken cancellationToken
    )
    {
        List<string> twitchIds = actors.Keys.ToList();
        Dictionary<string, Guid> map = await db
            .Users.Where(u => twitchIds.Contains(u.TwitchUserId!))
            .Select(u => new { TwitchUserId = u.TwitchUserId!, u.Id })
            .ToDictionaryAsync(u => u.TwitchUserId, u => u.Id, cancellationToken);

        List<User> toCreate = [];
        foreach ((string twitchId, ActorSnapshot actor) in actors)
        {
            if (map.ContainsKey(twitchId))
                continue;
            User user = new()
            {
                TwitchUserId = twitchId,
                Username = actor.Login,
                UsernameNormalized = actor.Login.ToLowerInvariant(),
                DisplayName = actor.Display,
                Enabled = true,
            };
            toCreate.Add(user);
            map[twitchId] = user.Id;
        }

        if (toCreate.Count > 0)
        {
            await db.Users.AddRangeAsync(toCreate, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return map;
    }

    private static ActorSnapshot? ParseActor(string data)
    {
        JObject? json = TryParse(data);
        string? twitchId = json?["actorTwitchUserId"]?.Value<string>();
        if (string.IsNullOrEmpty(twitchId))
            return null;

        string display = json!["actorDisplay"]?.Value<string>() ?? twitchId;
        string login = json["actorLogin"]?.Value<string>() ?? display.ToLowerInvariant();
        return new ActorSnapshot(twitchId, login, display);
    }

    private static JObject? TryParse(string data)
    {
        try
        {
            return JObject.Parse(data);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    private readonly record struct ChannelEventRow(string Id, string Data);

    private readonly record struct ActorSnapshot(string TwitchUserId, string Login, string Display);
}
