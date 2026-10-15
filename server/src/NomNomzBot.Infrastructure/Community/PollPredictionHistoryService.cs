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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Domain.Community.Entities;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.EventStore.Entities;

namespace NomNomzBot.Infrastructure.Community;

/// <inheritdoc cref="IPollPredictionHistoryService"/>
/// <remarks>
/// Reads the journal rows of the four poll and prediction start/end events and pairs them by Twitch id. These
/// payloads carry only titles and counts (no personal data), so they are read as plain JSON. A start with no end
/// (the bot was offline when it ended) shows as <c>unfinished</c> with the choices as they were at the start.
/// </remarks>
public sealed class PollPredictionHistoryService(IApplicationDbContext db)
    : IPollPredictionHistoryService
{
    private const string PollKind = "poll";
    private const string PredictionKind = "prediction";
    private const string ChatPollKind = "chat-poll";
    private const string UnfinishedStatus = "unfinished";

    private static readonly string[] EventTypes =
    [
        nameof(PollBeganEvent),
        nameof(PollEndedEvent),
        nameof(PredictionBeganEvent),
        nameof(PredictionEndedEvent),
    ];

    public async Task<Result<IReadOnlyList<PollPredictionHistoryItemDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<EventJournal> rows = await db
            .EventJournals.AsNoTracking()
            .Where(r => r.BroadcasterId == broadcasterId && EventTypes.Contains(r.EventType))
            .OrderBy(r => r.StreamPosition)
            .ToListAsync(cancellationToken);

        Dictionary<string, PollPredictionHistoryItemDto> items = [];
        foreach (EventJournal row in rows)
        {
            PollPredictionHistoryItemDto? item = Fold(row, items);
            if (item is not null)
                items[$"{item.Kind}:{item.Id}"] = item;
        }

        List<PollPredictionHistoryItemDto> chatPolls = await ChatPollItemsAsync(
            broadcasterId,
            cancellationToken
        );

        IReadOnlyList<PollPredictionHistoryItemDto> newestFirst =
        [
            .. items.Values.Concat(chatPolls).OrderByDescending(i => i.StartedAt),
        ];
        return Result.Success(newestFirst);
    }

    /// <summary>The bot's own chat polls (!poll), with votes counted per option; a closed poll marks its single top option.</summary>
    private async Task<List<PollPredictionHistoryItemDto>> ChatPollItemsAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<ChatPoll> polls = await db
            .ChatPolls.AsNoTracking()
            .Where(p => p.BroadcasterId == broadcasterId)
            .ToListAsync(cancellationToken);

        List<ChatPollVote> votes = await db
            .ChatPollVotes.AsNoTracking()
            .Where(v => v.BroadcasterId == broadcasterId)
            .ToListAsync(cancellationToken);

        ILookup<Guid, ChatPollVote> votesByPoll = votes.ToLookup(v => v.PollId);
        return [.. polls.Select(p => ChatPollItem(p, votesByPoll[p.Id]))];
    }

    private static PollPredictionHistoryItemDto ChatPollItem(
        ChatPoll poll,
        IEnumerable<ChatPollVote> votes
    )
    {
        List<string> labels = JsonConvert.DeserializeObject<List<string>>(poll.OptionsJson) ?? [];
        Dictionary<int, int> counts = votes
            .GroupBy(v => v.OptionIndex)
            .ToDictionary(g => g.Key, g => g.Count());

        List<(string Id, string Title, int Votes, int Points)> options =
        [
            .. labels.Select(
                (label, i) => ((i + 1).ToString(), label, counts.GetValueOrDefault(i + 1), 0)
            ),
        ];

        string? winnerId = null;
        int top = options.Count == 0 ? 0 : options.Max(o => o.Votes);
        if (
            poll.Status == ChatPollStatus.Closed
            && top > 0
            && options.Count(o => o.Votes == top) == 1
        )
            winnerId = options.First(o => o.Votes == top).Id;

        DateTimeOffset? endedAt = poll.ClosedAt is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(poll.ClosedAt.Value, DateTimeKind.Utc));
        return Build(
            ChatPollKind,
            poll.Id.ToString(),
            poll.Question,
            poll.Status,
            new DateTimeOffset(DateTime.SpecifyKind(poll.OpenedAt, DateTimeKind.Utc)),
            endedAt,
            options,
            winnerId
        );
    }

    private static PollPredictionHistoryItemDto? Fold(
        EventJournal row,
        Dictionary<string, PollPredictionHistoryItemDto> items
    )
    {
        DateTimeOffset at = new(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc));

        switch (row.EventType)
        {
            case nameof(PollBeganEvent):
                PollBeganEvent pollBegan = Read<PollBeganEvent>(row);
                return items.ContainsKey($"{PollKind}:{pollBegan.PollId}")
                    ? null
                    : PollItem(
                        pollBegan.PollId,
                        pollBegan.Title,
                        UnfinishedStatus,
                        at,
                        null,
                        pollBegan.Choices,
                        null
                    );
            case nameof(PollEndedEvent):
                PollEndedEvent pollEnded = Read<PollEndedEvent>(row);
                DateTimeOffset pollStart = items.TryGetValue(
                    $"{PollKind}:{pollEnded.PollId}",
                    out PollPredictionHistoryItemDto? begun
                )
                    ? begun.StartedAt
                    : at;
                return PollItem(
                    pollEnded.PollId,
                    pollEnded.Title,
                    pollEnded.Status,
                    pollStart,
                    at,
                    pollEnded.Choices,
                    pollEnded.WinningChoiceId
                );
            case nameof(PredictionBeganEvent):
                PredictionBeganEvent predictionBegan = Read<PredictionBeganEvent>(row);
                return items.ContainsKey($"{PredictionKind}:{predictionBegan.PredictionId}")
                    ? null
                    : PredictionItem(
                        predictionBegan.PredictionId,
                        predictionBegan.Title,
                        UnfinishedStatus,
                        at,
                        null,
                        predictionBegan.Outcomes,
                        null
                    );
            default:
                PredictionEndedEvent predictionEnded = Read<PredictionEndedEvent>(row);
                DateTimeOffset predictionStart = items.TryGetValue(
                    $"{PredictionKind}:{predictionEnded.PredictionId}",
                    out PollPredictionHistoryItemDto? opened
                )
                    ? opened.StartedAt
                    : at;
                return PredictionItem(
                    predictionEnded.PredictionId,
                    predictionEnded.Title,
                    predictionEnded.Status,
                    predictionStart,
                    at,
                    predictionEnded.Outcomes,
                    predictionEnded.WinningOutcomeId
                );
        }
    }

    private static TEvent Read<TEvent>(EventJournal row) =>
        JsonConvert.DeserializeObject<TEvent>(row.Payload)!;

    private static PollPredictionHistoryItemDto PollItem(
        string id,
        string title,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        IReadOnlyList<PollChoice> choices,
        string? winnerId
    )
    {
        List<(string Id, string Title, int Votes, int Points)> options =
        [
            .. choices.Select(c => (c.Id, c.Title, c.Votes, c.ChannelPointsVotes)),
        ];
        return Build(PollKind, id, title, status, startedAt, endedAt, options, winnerId);
    }

    private static PollPredictionHistoryItemDto PredictionItem(
        string id,
        string title,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        IReadOnlyList<PredictionOutcome> outcomes,
        string? winnerId
    )
    {
        List<(string Id, string Title, int Votes, int Points)> options =
        [
            .. outcomes.Select(o => (o.Id, o.Title, o.Users, o.ChannelPoints)),
        ];
        return Build(PredictionKind, id, title, status, startedAt, endedAt, options, winnerId);
    }

    private static PollPredictionHistoryItemDto Build(
        string kind,
        string id,
        string title,
        string status,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        List<(string Id, string Title, int Votes, int Points)> options,
        string? winnerId
    )
    {
        int total = options.Sum(o => o.Votes);
        List<PollPredictionOptionDto> dtos =
        [
            .. options.Select(o => new PollPredictionOptionDto(
                o.Id,
                o.Title,
                o.Votes,
                o.Points,
                total == 0
                    ? 0
                    : (int)Math.Round(100.0 * o.Votes / total, MidpointRounding.AwayFromZero),
                winnerId is not null && o.Id == winnerId
            )),
        ];
        return new(id, kind, title, status, startedAt, endedAt, total, dtos);
    }
}
