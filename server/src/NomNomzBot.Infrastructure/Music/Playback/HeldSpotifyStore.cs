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

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Infrastructure.Platform.Persistence;
using ChannelConfiguration = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// The durable fact "the director paused this channel's playing Spotify for a video". A row exists only for
/// that pause, so the owner's own pause is never undone after a restart.
/// </summary>
public interface IHeldSpotifyStore
{
    Task SaveAsync(Guid broadcasterId, DateTimeOffset heldSince, CancellationToken ct = default);

    /// <summary>When the pause began, or null when Spotify is not held.</summary>
    Task<DateTimeOffset?> HeldSinceAsync(Guid broadcasterId, CancellationToken ct = default);

    Task ClearAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <inheritdoc cref="IHeldSpotifyStore"/>
public sealed class HeldSpotifyStore(IServiceScopeFactory scopes, ILogger<HeldSpotifyStore> logger)
    : IHeldSpotifyStore
{
    private const string ConfigKey = "music.spotify_held_for_youtube";

    public Task SaveAsync(
        Guid broadcasterId,
        DateTimeOffset heldSince,
        CancellationToken ct = default
    ) =>
        WriteAsync(
            broadcasterId,
            async (db, row) =>
            {
                string value = heldSince.ToString("O", CultureInfo.InvariantCulture);
                if (row is not null)
                {
                    row.Value = value;
                    return;
                }

                db.Configurations.Add(
                    new()
                    {
                        BroadcasterId = broadcasterId,
                        Key = ConfigKey,
                        Value = value,
                    }
                );
                await Task.CompletedTask;
            },
            ct
        );

    public async Task<DateTimeOffset?> HeldSinceAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            string? value = await db
                .Configurations.AsNoTracking()
                .Where(c => c.BroadcasterId == broadcasterId && c.Key == ConfigKey)
                .Select(c => c.Value)
                .FirstOrDefaultAsync(ct);

            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset since
            )
                ? since
                : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not read the held Spotify fact of {C}", broadcasterId);
            return null;
        }
    }

    public Task ClearAsync(Guid broadcasterId, CancellationToken ct = default) =>
        WriteAsync(
            broadcasterId,
            (db, row) =>
            {
                if (row is not null)
                    db.Configurations.Remove(row);

                return Task.CompletedTask;
            },
            ct
        );

    /// <summary>A database fault must not stop the music, so it is logged and playback goes on.</summary>
    private async Task WriteAsync(
        Guid broadcasterId,
        Func<AppDbContext, ChannelConfiguration?, Task> change,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ChannelConfiguration? row = await db.Configurations.FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId && c.Key == ConfigKey,
                ct
            );
            await change(db, row);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not write the held Spotify fact of {C}", broadcasterId);
        }
    }
}
