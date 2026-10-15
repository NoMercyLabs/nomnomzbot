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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Stream;

namespace NomNomzBot.Infrastructure.Dashboard.Replay;

/// <summary>
/// Replays a <c>bot.shoutout.sent</c> activity row: the chat announcement, its speech and an overlay alert,
/// from the text the shoutout stored. It never calls the Twitch shoutout API, so no shoutout is sent twice
/// and no cooldown or counter moves. A row from before the text was kept replays nothing.
/// </summary>
public sealed class ShoutoutReplayPresenter
{
    public const string ActivityType = "bot.shoutout.sent";

    private readonly IApplicationDbContext _db;
    private readonly ITwitchChatApi _chat;
    private readonly ITtsDispatchService _tts;
    private readonly IEventResponseOverlayNotifier _overlay;
    private readonly ILogger<ShoutoutReplayPresenter> _logger;

    public ShoutoutReplayPresenter(
        IApplicationDbContext db,
        ITwitchChatApi chat,
        ITtsDispatchService tts,
        IEventResponseOverlayNotifier overlay,
        ILogger<ShoutoutReplayPresenter> logger
    )
    {
        _db = db;
        _chat = chat;
        _tts = tts;
        _overlay = overlay;
        _logger = logger;
    }

    /// <summary>Null when the row is not a shoutout activity row, so the caller tries its other sources.</summary>
    public async Task<EventResponseOutcome?> ReplayAsync(
        Guid broadcasterId,
        string channelEventId,
        CancellationToken ct
    )
    {
        ChannelEvent? row = await _db
            .ChannelEvents.AsNoTracking()
            .FirstOrDefaultAsync(
                e =>
                    e.Id == channelEventId
                    && e.ChannelId == broadcasterId
                    && e.Type == ActivityType,
                ct
            );
        if (row is null)
            return null;

        ShoutoutSentRecord? stored = JsonSerializer.Deserialize<ShoutoutSentRecord>(
            row.Data ?? "{}"
        );
        if (stored is null || string.IsNullOrWhiteSpace(stored.Announcement))
            return EventResponseOutcome.None;

        int chat = await SendChatAsync(broadcasterId, stored.Announcement, ct);
        int voice = stored.Spoke
            ? await SpeakAsync(broadcasterId, stored, stored.Announcement, ct)
            : 0;
        int overlays = await ShowOverlayAsync(broadcasterId, stored, stored.Announcement, ct);

        return new(chat, voice, overlays);
    }

    private async Task<int> SendChatAsync(
        Guid broadcasterId,
        string announcement,
        CancellationToken ct
    )
    {
        Result sent = await _chat.SendAnnouncementAsync(broadcasterId, announcement, null, ct);
        if (sent.IsFailure)
            _logger.LogWarning("Shoutout replay announcement failed: {Error}", sent.ErrorMessage);

        return sent.IsSuccess ? 1 : 0;
    }

    private async Task<int> SpeakAsync(
        Guid broadcasterId,
        ShoutoutSentRecord stored,
        string announcement,
        CancellationToken ct
    )
    {
        Guid? ownerId = await _db
            .Channels.AsNoTracking()
            .Where(c => c.Id == broadcasterId)
            .Select(c => (Guid?)c.OwnerUserId)
            .FirstOrDefaultAsync(ct);
        if (ownerId is null)
            return 0;

        string name = stored.ToDisplayName ?? stored.ToUserId;
        Result<TtsDispatchOutcome> spoken = await _tts.RequestSpeakAsync(
            ShoutoutSender.SpeakRequest(
                broadcasterId,
                ownerId.Value,
                stored.ToUserId,
                name,
                stored.ToLogin ?? name,
                announcement
            ),
            ct
        );
        if (spoken.IsFailure)
            _logger.LogWarning("Shoutout replay speech failed: {Error}", spoken.ErrorMessage);

        return spoken.IsSuccess ? 1 : 0;
    }

    private async Task<int> ShowOverlayAsync(
        Guid broadcasterId,
        ShoutoutSentRecord stored,
        string announcement,
        CancellationToken ct
    )
    {
        Dictionary<string, string> metadata = new()
        {
            ["toUserId"] = stored.ToUserId,
            ["toDisplayName"] = stored.ToDisplayName ?? stored.ToUserId,
        };
        await _overlay.NotifyAsync(broadcasterId, ActivityType, announcement, metadata, ct);

        return 1;
    }
}
