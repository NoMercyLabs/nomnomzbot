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

/// <summary>The music volume the owner last chose, kept so a restart never resets it to 100.</summary>
public interface IOwnerVolumeStore
{
    Task SaveAsync(Guid broadcasterId, int volume, CancellationToken ct = default);

    /// <summary>The saved volume, or null when the owner never set one.</summary>
    Task<int?> LoadAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <inheritdoc cref="IOwnerVolumeStore"/>
public sealed class OwnerVolumeStore(IServiceScopeFactory scopes, ILogger<OwnerVolumeStore> logger)
    : IOwnerVolumeStore
{
    private const string ConfigKey = "music.owner_volume";

    public async Task SaveAsync(Guid broadcasterId, int volume, CancellationToken ct = default)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            string value = volume.ToString(CultureInfo.InvariantCulture);
            ChannelConfiguration? row = await db.Configurations.FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId && c.Key == ConfigKey,
                ct
            );
            if (row is not null)
                row.Value = value;
            else
                db.Configurations.Add(
                    new()
                    {
                        BroadcasterId = broadcasterId,
                        Key = ConfigKey,
                        Value = value,
                    }
                );
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // A database fault must not stop the music.
            logger.LogWarning(ex, "Could not save the music volume of {C}", broadcasterId);
        }
    }

    public async Task<int?> LoadAsync(Guid broadcasterId, CancellationToken ct = default)
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
            return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int v
            )
                ? Math.Clamp(v, 0, 100)
                : null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not read the music volume of {C}", broadcasterId);
            return null;
        }
    }
}
