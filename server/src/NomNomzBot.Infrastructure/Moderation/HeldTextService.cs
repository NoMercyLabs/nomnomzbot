// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Enums;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Reads the moderation queue (<c>ModerationQueueItem</c>) for a row that holds a viewer's text back. The
/// redemption event and the AutoMod hold event are separate EventSub events and either can arrive first, so
/// when no row exists yet the check polls for a bounded time before it decides the text was never held.
/// </summary>
public sealed partial class HeldTextService : IHeldTextService
{
    private static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public HeldTextService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<bool> IsTextHeldAsync(
        Guid broadcasterId,
        string twitchUserId,
        string text,
        CancellationToken ct = default,
        DateTimeOffset? occurredAt = null
    )
    {
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        DateTimeOffset matchAt = occurredAt ?? startedAt;
        DateTime from = (matchAt - MatchWindow).UtcDateTime;
        DateTime to = (matchAt + MatchWindow).UtcDateTime;
        string wanted = Normalize(text);

        while (true)
        {
            List<ModerationQueueStatus> matches = (
                await _db
                    .ModerationQueueItems.AsNoTracking()
                    .Where(i =>
                        i.BroadcasterId == broadcasterId
                        && i.TargetTwitchUserId == twitchUserId
                        && i.CreatedAt >= from
                        && i.CreatedAt <= to
                    )
                    .Select(i => new { i.Status, i.MessageContentSnapshot })
                    .ToListAsync(ct)
            )
                .Where(i => Normalize(i.MessageContentSnapshot) == wanted)
                .Select(i => i.Status)
                .ToList();

            if (matches.Count > 0)
                return matches.Any(status => status != ModerationQueueStatus.Approved);

            if (_timeProvider.GetUtcNow() - startedAt >= MaxWait)
                return false;

            await Task.Delay(PollInterval, _timeProvider, ct);
        }
    }

    private static string Normalize(string? text) =>
        WhitespaceRun().Replace(text ?? string.Empty, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
