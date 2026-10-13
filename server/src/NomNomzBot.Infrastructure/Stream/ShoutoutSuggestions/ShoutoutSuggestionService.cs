// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Stream.ShoutoutSuggestions;

/// <summary>
/// Builds the shoutout suggestions for the current stream from the stream's chatters, Twitch (Get Videos,
/// Get Channel Information) and this channel's own shoutout records.
/// </summary>
public sealed class ShoutoutSuggestionService(
    IChannelRegistry registry,
    IApplicationDbContext db,
    ITwitchVideosApi videos,
    ITwitchChannelsApi channels,
    ShoutoutSuggestionState state,
    TimeProvider clock
) : IShoutoutSuggestionService
{
    private const int WindowDays = 14;
    private const int MaxSuggestions = 10;
    private const int MaxVideoPages = 3;
    private const int ParallelLookups = 4;
    private const string ShoutoutSentEventType = "bot.shoutout.sent";

    public async Task<Result<IReadOnlyList<ShoutoutSuggestionDto>>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelContext? context = registry.Get(channelId);
        if (context is not { IsLive: true })
            return Result.Success<IReadOnlyList<ShoutoutSuggestionDto>>([]);

        DateTimeOffset now = clock.GetUtcNow();
        Dictionary<string, DateTimeOffset> shoutouts = await LastShoutoutsAsync(
            channelId,
            context,
            cancellationToken
        );
        List<string> candidates = await CandidatesAsync(
            channelId,
            context,
            shoutouts,
            cancellationToken
        );

        Dictionary<string, int> streams = await StreamsLast14DaysAsync(
            candidates,
            now,
            cancellationToken
        );
        List<string> top = candidates
            .Where(id => streams.GetValueOrDefault(id) > 0)
            .OrderByDescending(id => streams[id])
            .ThenBy(id => context.SessionChatters[id], StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)
            .ToList();
        if (top.Count == 0)
            return Result.Success<IReadOnlyList<ShoutoutSuggestionDto>>([]);

        Result<Dictionary<string, CachedChannelInfo>> infos = await ChannelInfosAsync(
            top,
            now,
            cancellationToken
        );
        if (infos.IsFailure)
            return Result.Failure<IReadOnlyList<ShoutoutSuggestionDto>>(
                infos.ErrorMessage,
                infos.ErrorCode
            );

        Dictionary<string, string> colors = await ColorsAsync(channelId, top, cancellationToken);
        List<ShoutoutSuggestionDto> suggestions = top.Where(infos.Value.ContainsKey)
            .Select(id => new ShoutoutSuggestionDto(
                id,
                infos.Value[id].Login,
                infos.Value[id].DisplayName,
                colors.GetValueOrDefault(id),
                infos.Value[id].Category,
                streams[id],
                shoutouts.TryGetValue(id, out DateTimeOffset at) ? at : null
            ))
            .ToList();
        return Result.Success<IReadOnlyList<ShoutoutSuggestionDto>>(suggestions);
    }

    public Result Dismiss(Guid channelId, string twitchUserId)
    {
        ChannelContext? context = registry.Get(channelId);
        if (context is null)
            return Result.Failure("The channel is not active.", "NOT_FOUND");

        state.Dismiss(channelId, twitchUserId, context.WentLiveAt);
        return Result.Success();
    }

    private async Task<List<string>> CandidatesAsync(
        Guid channelId,
        ChannelContext context,
        Dictionary<string, DateTimeOffset> shoutouts,
        CancellationToken cancellationToken
    )
    {
        List<string> bots = await db
            .BotAccounts.AsNoTracking()
            .Where(b => b.IsActive && b.Platform == "twitch")
            .Select(b => b.BotUserId)
            .ToListAsync(cancellationToken);

        List<string> candidates = context
            .SessionChatters.Keys.Where(id => id != context.TwitchChannelId)
            .Where(id => !bots.Contains(id))
            .Where(id => !ShoutedThisStream(id, context, shoutouts))
            .Where(id => !state.IsDismissed(channelId, id, context.WentLiveAt))
            .ToList();

        List<string> otherPlatform = await db
            .ChatMessages.AsNoTracking()
            .Where(m =>
                m.BroadcasterId == channelId
                && m.Provider != "twitch"
                && candidates.Contains(m.UserId)
            )
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return candidates.Where(id => !otherPlatform.Contains(id)).ToList();
    }

    private static bool ShoutedThisStream(
        string twitchUserId,
        ChannelContext context,
        Dictionary<string, DateTimeOffset> shoutouts
    ) =>
        shoutouts.TryGetValue(twitchUserId, out DateTimeOffset last)
        && context.WentLiveAt is { } wentLive
        && last >= wentLive;

    /// <summary>The last time this channel shouted each user out: the stored records and this stream's memory.</summary>
    private async Task<Dictionary<string, DateTimeOffset>> LastShoutoutsAsync(
        Guid channelId,
        ChannelContext context,
        CancellationToken cancellationToken
    )
    {
        var rows = await db
            .ChannelEvents.AsNoTracking()
            .Where(e => e.ChannelId == channelId && e.Type == ShoutoutSentEventType)
            .Select(e => new { e.Data, e.CreatedAt })
            .ToListAsync(cancellationToken);

        Dictionary<string, DateTimeOffset> last = [];
        foreach (var row in rows)
        {
            string? user = ReadTarget(row.Data);
            if (user is null)
                continue;
            DateTimeOffset at = new(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc));
            if (!last.TryGetValue(user, out DateTimeOffset seen) || at > seen)
                last[user] = at;
        }

        foreach (KeyValuePair<string, DateTimeOffset> memory in context.LastShoutoutPerUser)
            if (!last.TryGetValue(memory.Key, out DateTimeOffset seen) || memory.Value > seen)
                last[memory.Key] = memory.Value;
        return last;
    }

    private static string? ReadTarget(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return null;
        try
        {
            using JsonDocument json = JsonDocument.Parse(data);
            return json.RootElement.TryGetProperty("toUserId", out JsonElement id)
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<Dictionary<string, int>> StreamsLast14DaysAsync(
        List<string> candidates,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        ConcurrentDictionary<string, int> streams = new();
        List<string> missing = [];
        foreach (string id in candidates)
        {
            if (state.TryGetStreams(id, now, out int cached))
                streams[id] = cached;
            else
                missing.Add(id);
        }

        DateTimeOffset cutoff = now.AddDays(-WindowDays);
        await Parallel.ForEachAsync(
            missing,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = ParallelLookups,
                CancellationToken = cancellationToken,
            },
            async (id, ct) =>
            {
                int? count = await CountArchivesSinceAsync(id, cutoff, ct);
                if (count is null)
                    return;
                streams[id] = count.Value;
                state.SetStreams(id, count.Value, now);
            }
        );
        return streams.ToDictionary(p => p.Key, p => p.Value);
    }

    /// <summary>Archives created at or after <paramref name="cutoff"/>, newest first; null when Twitch fails.</summary>
    private async Task<int?> CountArchivesSinceAsync(
        string twitchUserId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken
    )
    {
        int count = 0;
        string? after = null;
        for (int page = 0; page < MaxVideoPages; page++)
        {
            Result<TwitchPage<TwitchVideo>> result = await videos.GetVideosByTwitchUserIdAsync(
                twitchUserId,
                "archive",
                null,
                "time",
                new(after),
                cancellationToken
            );
            if (result.IsFailure)
                return null;

            count += result.Value.Items.Count(v => v.CreatedAt >= cutoff);
            bool reachedOlder = result.Value.Items.Any(v => v.CreatedAt < cutoff);
            if (reachedOlder || result.Value.NextCursor is null)
                break;
            after = result.Value.NextCursor;
        }

        return count;
    }

    private async Task<Result<Dictionary<string, CachedChannelInfo>>> ChannelInfosAsync(
        List<string> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, CachedChannelInfo> infos = [];
        List<string> missing = [];
        foreach (string id in ids)
        {
            if (
                state.TryGetChannelInfo(id, now, out CachedChannelInfo? cached)
                && cached is not null
            )
                infos[id] = cached;
            else
                missing.Add(id);
        }

        if (missing.Count == 0)
            return Result.Success(infos);

        Result<IReadOnlyList<TwitchChannelInformation>> fetched =
            await channels.GetChannelInformationByTwitchIdsAsync(missing, cancellationToken);
        if (fetched.IsFailure)
            return Result.Failure<Dictionary<string, CachedChannelInfo>>(
                fetched.ErrorMessage,
                fetched.ErrorCode
            );

        foreach (TwitchChannelInformation info in fetched.Value)
        {
            CachedChannelInfo entry = new(
                info.BroadcasterLogin,
                info.BroadcasterName,
                string.IsNullOrEmpty(info.GameName) ? null : info.GameName
            );
            infos[info.BroadcasterId] = entry;
            state.SetChannelInfo(info.BroadcasterId, entry, now);
        }

        return Result.Success(infos);
    }

    /// <summary>The chat color from each user's latest chat line in this channel that carried one.</summary>
    private async Task<Dictionary<string, string>> ColorsAsync(
        Guid channelId,
        List<string> ids,
        CancellationToken cancellationToken
    )
    {
        var rows = await db
            .ChatMessages.AsNoTracking()
            .Where(m =>
                m.BroadcasterId == channelId
                && m.Provider == "twitch"
                && m.ColorHex != null
                && ids.Contains(m.UserId)
            )
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new { m.UserId, m.ColorHex })
            .Take(ids.Count * 20)
            .ToListAsync(cancellationToken);

        Dictionary<string, string> colors = [];
        foreach (var row in rows)
            colors.TryAdd(row.UserId, row.ColorHex!);
        return colors;
    }
}
