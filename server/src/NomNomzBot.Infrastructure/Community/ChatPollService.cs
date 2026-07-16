// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Community.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Community.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Community;

/// <inheritdoc cref="IChatPollService"/>
public sealed class ChatPollService : IChatPollService
{
    private const int MaxDurationSeconds = 86_400;
    private const int HistoryTake = 20;

    private readonly IApplicationDbContext _db;
    private readonly IChannelRegistry _registry;
    private readonly IChatProvider _chat;
    private readonly TimeProvider _clock;
    private readonly ILogger<ChatPollService> _logger;

    public ChatPollService(
        IApplicationDbContext db,
        IChannelRegistry registry,
        IChatProvider chat,
        TimeProvider clock,
        ILogger<ChatPollService> logger
    )
    {
        _db = db;
        _registry = registry;
        _chat = chat;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<ChatPollDto>> OpenAsync(
        string broadcasterId,
        OpenChatPollRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<ChatPollDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        List<string> options = [.. request.Options.Select(o => o.Trim()).Where(o => o.Length > 0)];
        if (options.Count < 2 || options.Count > 10)
            return Result.Failure<ChatPollDto>(
                "A poll needs 2–10 non-empty options.",
                "VALIDATION_FAILED"
            );

        bool alreadyOpen = await _db.ChatPolls.AnyAsync(
            p => p.BroadcasterId == broadcaster && p.Status == ChatPollStatus.Open,
            cancellationToken
        );
        if (alreadyOpen)
            return Result.Failure<ChatPollDto>(
                "A poll is already open — close it first.",
                "CONFLICT"
            );

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        ChatPoll poll = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcaster,
            Question = request.Question.Trim(),
            OptionsJson = JsonSerializer.Serialize(options),
            Status = ChatPollStatus.Open,
            OpenedAt = now,
            ClosesAt = request.DurationSeconds is int seconds and > 0
                ? now.AddSeconds(Math.Min(seconds, MaxDurationSeconds))
                : null,
        };
        _db.ChatPolls.Add(poll);
        await _db.SaveChangesAsync(cancellationToken);

        SetHotPathPoll(broadcaster, poll, options.Count);

        if (request.Announce)
        {
            string lines = string.Join(" | ", options.Select((label, i) => $"{i + 1}: {label}"));
            await SendChatSafeAsync(
                broadcaster,
                $"POLL: {poll.Question} — {lines} — vote by typing the number!",
                cancellationToken
            );
        }

        return Result.Success(ToDto(poll, options, votes: []));
    }

    public async Task<Result<IReadOnlyList<ChatPollDto>>> ListAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<IReadOnlyList<ChatPollDto>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        await AutoCloseExpiredAsync(broadcaster, cancellationToken);

        List<ChatPoll> polls = await _db
            .ChatPolls.Where(p => p.BroadcasterId == broadcaster)
            .OrderBy(p => p.Status) // "closed" < "open" alphabetically — flip below
            .ToListAsync(cancellationToken);
        List<ChatPoll> ordered =
        [
            .. polls.Where(p => p.Status == ChatPollStatus.Open),
            .. polls
                .Where(p => p.Status != ChatPollStatus.Open)
                .OrderByDescending(p => p.OpenedAt)
                .Take(HistoryTake),
        ];

        List<ChatPollDto> items = [];
        foreach (ChatPoll poll in ordered)
            items.Add(await ProjectAsync(poll, cancellationToken));
        return Result.Success<IReadOnlyList<ChatPollDto>>(items);
    }

    public async Task<Result<ChatPollDto>> GetAsync(
        string broadcasterId,
        Guid pollId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<ChatPollDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        await AutoCloseExpiredAsync(broadcaster, cancellationToken);

        ChatPoll? poll = await _db.ChatPolls.FirstOrDefaultAsync(
            p => p.Id == pollId && p.BroadcasterId == broadcaster,
            cancellationToken
        );
        if (poll is null)
            return Errors.NotFound<ChatPollDto>("ChatPoll", pollId.ToString());

        return Result.Success(await ProjectAsync(poll, cancellationToken));
    }

    public async Task<Result<ChatPollDto>> CloseAsync(
        string broadcasterId,
        Guid pollId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<ChatPollDto>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        ChatPoll? poll = await _db.ChatPolls.FirstOrDefaultAsync(
            p => p.Id == pollId && p.BroadcasterId == broadcaster,
            cancellationToken
        );
        if (poll is null)
            return Errors.NotFound<ChatPollDto>("ChatPoll", pollId.ToString());
        if (poll.Status != ChatPollStatus.Open)
            return Result.Failure<ChatPollDto>("The poll is already closed.", "CONFLICT");

        ChatPollDto dto = await CloseCoreAsync(poll, announce: true, cancellationToken);
        return Result.Success(dto);
    }

    public async Task RecordVoteAsync(
        Guid broadcasterId,
        Guid pollId,
        string voterProvider,
        string voterUserId,
        int optionIndex,
        CancellationToken cancellationToken = default
    )
    {
        ChatPoll? poll = await _db.ChatPolls.FirstOrDefaultAsync(
            p =>
                p.Id == pollId
                && p.BroadcasterId == broadcasterId
                && p.Status == ChatPollStatus.Open,
            cancellationToken
        );
        if (poll is null)
        {
            // The cache said open but the row isn't — heal the hot path.
            ClearHotPathPoll(broadcasterId);
            return;
        }

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        if (poll.ClosesAt is DateTime closesAt && now >= closesAt)
        {
            await CloseCoreAsync(poll, announce: true, cancellationToken);
            return;
        }

        if (optionIndex < 1 || optionIndex > ParseOptions(poll.OptionsJson).Count)
            return;

        ChatPollVote? existing = await _db.ChatPollVotes.FirstOrDefaultAsync(
            v =>
                v.PollId == pollId
                && v.VoterProvider == voterProvider
                && v.VoterUserId == voterUserId,
            cancellationToken
        );
        if (existing is null)
        {
            _db.ChatPollVotes.Add(
                new ChatPollVote
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = broadcasterId,
                    PollId = pollId,
                    VoterProvider = voterProvider,
                    VoterUserId = voterUserId,
                    OptionIndex = optionIndex,
                    VotedAt = now,
                }
            );
        }
        else
        {
            existing.OptionIndex = optionIndex; // last vote wins
            existing.VotedAt = now;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Closes any open-but-expired poll for the channel (the lazy auto-close touchpoint).</summary>
    private async Task AutoCloseExpiredAsync(Guid broadcaster, CancellationToken ct)
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        ChatPoll? expired = await _db.ChatPolls.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == broadcaster
                && p.Status == ChatPollStatus.Open
                && p.ClosesAt != null
                && p.ClosesAt <= now,
            ct
        );
        if (expired is not null)
            await CloseCoreAsync(expired, announce: true, ct);
    }

    private async Task<ChatPollDto> CloseCoreAsync(
        ChatPoll poll,
        bool announce,
        CancellationToken ct
    )
    {
        poll.Status = ChatPollStatus.Closed;
        poll.ClosedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);
        ClearHotPathPoll(poll.BroadcasterId);

        ChatPollDto dto = await ProjectAsync(poll, ct);
        if (announce)
        {
            ChatPollOptionDto? winner = dto
                .Options.OrderByDescending(o => o.Votes)
                .FirstOrDefault();
            string result =
                dto.TotalVotes == 0 || winner is null
                    ? "no votes were cast."
                    : $"\"{winner.Label}\" wins with {winner.Votes}/{dto.TotalVotes} votes!";
            await SendChatSafeAsync(
                poll.BroadcasterId,
                $"POLL CLOSED: {poll.Question} — {result}",
                ct
            );
        }
        return dto;
    }

    private async Task<ChatPollDto> ProjectAsync(ChatPoll poll, CancellationToken ct)
    {
        List<string> options = ParseOptions(poll.OptionsJson);
        var tallies = await _db
            .ChatPollVotes.Where(v => v.PollId == poll.Id)
            .GroupBy(v => v.OptionIndex)
            .Select(g => new { Index = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        Dictionary<int, int> byIndex = tallies.ToDictionary(t => t.Index, t => t.Count);
        return ToDto(poll, options, byIndex);
    }

    private static ChatPollDto ToDto(
        ChatPoll poll,
        List<string> options,
        Dictionary<int, int> votes
    )
    {
        IReadOnlyList<ChatPollOptionDto> optionDtos =
        [
            .. options.Select(
                (label, i) => new ChatPollOptionDto(i + 1, label, votes.GetValueOrDefault(i + 1))
            ),
        ];
        return new ChatPollDto(
            poll.Id,
            poll.Question,
            optionDtos,
            poll.Status,
            optionDtos.Sum(o => o.Votes),
            poll.OpenedAt,
            poll.ClosesAt,
            poll.ClosedAt
        );
    }

    private static List<string> ParseOptions(string optionsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(optionsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SetHotPathPoll(Guid broadcaster, ChatPoll poll, int optionCount)
    {
        ChannelContext? ctx = _registry.Get(broadcaster);
        if (ctx is not null)
            ctx.ActiveChatPoll = new CachedChatPoll { Id = poll.Id, OptionCount = optionCount };
    }

    private void ClearHotPathPoll(Guid broadcaster)
    {
        ChannelContext? ctx = _registry.Get(broadcaster);
        if (ctx is not null)
            ctx.ActiveChatPoll = null;
    }

    /// <summary>Chat announcements are best-effort — a send failure must never fail the poll operation.</summary>
    private async Task SendChatSafeAsync(Guid broadcaster, string message, CancellationToken ct)
    {
        try
        {
            await _chat.SendMessageAsync(broadcaster, message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat poll announcement failed for {Channel}", broadcaster);
        }
    }
}
