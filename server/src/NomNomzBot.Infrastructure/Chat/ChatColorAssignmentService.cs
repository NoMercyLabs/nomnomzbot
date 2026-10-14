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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// <see cref="IChatColorAssignmentService"/> over the <see cref="ChatterAssignedColor"/> table. The least-used
/// color is counted per palette color as distinct chatters whose messages carried exactly that color while
/// one of the channel's streams of the last 14 days was running. Ties go to the first color in the palette.
/// Singleton: it opens its own scope, so the translators and workers that call it need no database.
/// </summary>
public sealed class ChatColorAssignmentService : IChatColorAssignmentService
{
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(14);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<(Guid, string, string), string> _assigned = new();

    public ChatColorAssignmentService(IServiceScopeFactory scopeFactory, TimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    public async Task<string?> ResolveAsync(
        Guid broadcasterId,
        string provider,
        string userId,
        string? lineColor,
        CancellationToken ct = default
    )
    {
        if (!string.IsNullOrWhiteSpace(lineColor))
            return lineColor;

        (Guid, string, string) key = (broadcasterId, provider, userId);
        if (_assigned.TryGetValue(key, out string? cached))
            return cached;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        string? color = await FindStoredAsync(db, key, ct);
        if (color is null)
        {
            string leastUsed = await LeastUsedColorAsync(db, broadcasterId, ct);
            color = await StoreAsync(db, key, leastUsed, ct);
        }

        _assigned[key] = color;
        return color;
    }

    private static Task<string?> FindStoredAsync(
        IApplicationDbContext db,
        (Guid BroadcasterId, string Provider, string UserId) key,
        CancellationToken ct
    ) =>
        db
            .ChatterAssignedColors.Where(c =>
                c.BroadcasterId == key.BroadcasterId
                && c.Provider == key.Provider
                && c.UserId == key.UserId
            )
            .Select(c => c.ColorHex)
            .FirstOrDefaultAsync(ct);

    private static async Task<string> StoreAsync(
        IApplicationDbContext db,
        (Guid BroadcasterId, string Provider, string UserId) key,
        string color,
        CancellationToken ct
    )
    {
        ChatterAssignedColor row = new()
        {
            BroadcasterId = key.BroadcasterId,
            Provider = key.Provider,
            UserId = key.UserId,
            ColorHex = color,
        };
        await db.ChatterAssignedColors.AddAsync(row, ct);

        try
        {
            await db.SaveChangesAsync(ct);
            return color;
        }
        catch (DbUpdateException)
        {
            // A concurrent line for the same chatter stored first: that assignment is the one to keep.
            db.Entry(row).State = EntityState.Detached;
            return await FindStoredAsync(db, key, ct) ?? color;
        }
    }

    private async Task<string> LeastUsedColorAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset cutoff = now - Lookback;

        List<(DateTime Start, DateTime End)> windows = await ReadStreamWindowsAsync(
            db,
            broadcasterId,
            cutoff,
            now,
            ct
        );
        Dictionary<string, HashSet<(string, string)>> chattersByColor =
            windows.Count == 0
                ? []
                : await CountChattersByColorAsync(db, broadcasterId, windows, ct);

        return ChatColorPalette.Colors.OrderBy(c => CountOf(chattersByColor, c)).First();
    }

    private static int CountOf(
        Dictionary<string, HashSet<(string, string)>> chattersByColor,
        string color
    ) =>
        chattersByColor.TryGetValue(color, out HashSet<(string, string)>? chatters)
            ? chatters.Count
            : 0;

    private static async Task<List<(DateTime Start, DateTime End)>> ReadStreamWindowsAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTimeOffset cutoff,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var streams = await db
            .Streams.Where(s =>
                s.ChannelId == broadcasterId
                && s.StartedAt != null
                && (s.EndedAt == null || s.EndedAt >= cutoff)
            )
            .Select(s => new { s.StartedAt, s.EndedAt })
            .ToListAsync(ct);

        return
        [
            .. streams.Select(s =>
                (s.StartedAt!.Value.UtcDateTime, (s.EndedAt ?? now).UtcDateTime)
            ),
        ];
    }

    private static async Task<
        Dictionary<string, HashSet<(string, string)>>
    > CountChattersByColorAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        List<(DateTime Start, DateTime End)> windows,
        CancellationToken ct
    )
    {
        DateTime earliest = windows.Min(w => w.Start);
        var lines = await db
            .ChatMessages.Where(m =>
                m.BroadcasterId == broadcasterId && m.ColorHex != null && m.CreatedAt >= earliest
            )
            .Select(m => new
            {
                m.Provider,
                m.UserId,
                m.ColorHex,
                m.CreatedAt,
            })
            .ToListAsync(ct);

        Dictionary<string, HashSet<(string, string)>> chattersByColor = [];
        foreach (var line in lines)
        {
            if (!windows.Any(w => line.CreatedAt >= w.Start && line.CreatedAt <= w.End))
                continue;

            string color = line.ColorHex!.ToUpperInvariant();
            if (!chattersByColor.TryGetValue(color, out HashSet<(string, string)>? chatters))
                chattersByColor[color] = chatters = [];
            chatters.Add((line.Provider, line.UserId));
        }

        return chattersByColor;
    }
}
