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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Content.Music;

/// <summary>
/// Boot-time backfill for the legacy <c>Service</c> token store the music providers read from. The connect
/// flow now mirrors a Spotify/YouTube grant into that store on every NEW connect (see
/// <see cref="IMusicProviderTokenMirror"/>), but accounts that connected BEFORE the mirror existed have a
/// vaulted token and no <c>Service</c> row, so playback/search read them back as disconnected until a manual
/// reconnect. This seeder closes that gap: for every music-provider <see cref="IntegrationConnection"/> whose
/// tokens are vaulted but whose <c>Service</c> row is missing or tokenless, it re-reads the vaulted grant and
/// runs the SAME <see cref="IMusicProviderTokenMirror.MirrorAsync"/> the connect path uses.
/// </summary>
/// <remarks>
/// Runs late (Order 910 — it reads runtime <c>IntegrationConnection</c> rows that arrive via connect, not via
/// seeding). Idempotent: an anti-join skips connections that already have a usable <c>Service</c> row, so a
/// warm database is a fast no-op, and the underlying mirror upserts by <c>(BroadcasterId, provider)</c> in any
/// case. Fail-safe: a per-connection guard logs and continues, so one undecryptable or misconfigured grant can
/// never abort startup.
/// </remarks>
public sealed class MusicProviderServiceBackfillSeeder : ISeeder
{
    // The providers whose tokens the Service store fronts. Kept as an array (not the mirror's HashSet) so the
    // membership test translates to a SQL `IN` in the candidate query below.
    private static readonly string[] MusicProviders =
    [
        AuthEnums.IntegrationProvider.Spotify,
        AuthEnums.IntegrationProvider.YouTube,
    ];

    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly IMusicProviderTokenMirror _mirror;
    private readonly ILogger<MusicProviderServiceBackfillSeeder> _logger;

    public MusicProviderServiceBackfillSeeder(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        ISystemCredentialsProvider credentials,
        IMusicProviderTokenMirror mirror,
        ILogger<MusicProviderServiceBackfillSeeder> logger
    )
    {
        _db = db;
        _vault = vault;
        _credentials = credentials;
        _mirror = mirror;
        _logger = logger;
    }

    public int Order => 910;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Live music connections keyed to a tenant (the mirror seals under the tenant subject, so a platform/global
        // connection — BroadcasterId null — has no Service row to write). IgnoreQueryFilters + explicit DeletedAt
        // matches how the vault reads these, independent of any ambient tenant.
        List<IntegrationConnection> connections = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.DeletedAt == null
                && c.BroadcasterId != null
                && MusicProviders.Contains(c.Provider)
            )
            .ToListAsync(ct);

        if (connections.Count == 0)
            return;

        // Anti-join: the (tenant, provider) pairs that already have a usable Service row (what the providers
        // require — enabled + an access token). Skipping these makes an already-backfilled boot a fast no-op.
        HashSet<string> alreadyMirrored = (
            await _db
                .Services.Where(s =>
                    s.Enabled
                    && s.AccessToken != null
                    && s.BroadcasterId != null
                    && MusicProviders.Contains(s.Name)
                )
                .Select(s => new { s.BroadcasterId, s.Name })
                .ToListAsync(ct)
        )
            .Select(s => Key(s.BroadcasterId!.Value, s.Name))
            .ToHashSet();

        int backfilled = 0;
        foreach (IntegrationConnection connection in connections)
        {
            Guid broadcasterId = connection.BroadcasterId!.Value;
            string provider = connection.Provider;

            if (alreadyMirrored.Contains(Key(broadcasterId, provider)))
                continue;

            try
            {
                if (await TryMirrorAsync(connection, broadcasterId, provider, ct))
                    backfilled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail-safe: one bad grant (crypto-shredded DEK, provider mid-migration, …) must not abort the
                // startup seed for every other connection.
                _logger.LogWarning(
                    ex,
                    "Skipped {Provider} Service backfill for broadcaster {BroadcasterId} — the connection could not be mirrored",
                    provider,
                    broadcasterId
                );
            }
        }

        if (backfilled > 0)
            _logger.LogInformation(
                "Backfilled {Count} music-provider Service row(s) from the token vault",
                backfilled
            );
    }

    /// <summary>
    /// Re-reads the vaulted grant for one connection and mirrors it into the Service store, reusing the connect
    /// path's <see cref="IMusicProviderTokenMirror"/>. Returns false (and logs) when the grant or its app
    /// credentials are unavailable, so the row is left for a reconnect rather than a broken mirror.
    /// </summary>
    private async Task<bool> TryMirrorAsync(
        IntegrationConnection connection,
        Guid broadcasterId,
        string provider,
        CancellationToken ct
    )
    {
        Result<DecryptedTokenDto> access = await _vault.GetAccessTokenAsync(connection.Id, ct);
        if (access.IsFailure)
        {
            _logger.LogDebug(
                "No vaulted access token for {Provider} broadcaster {BroadcasterId} ({Error}) — nothing to backfill",
                provider,
                broadcasterId,
                access.ErrorMessage
            );
            return false;
        }

        // The access token row carries the expiry; the refresh token is optional (some providers omit it).
        Result<DecryptedTokenDto> refresh = await _vault.GetRefreshTokenAsync(connection.Id, ct);
        string? refreshToken = refresh.IsSuccess ? refresh.Value.Value : null;

        SystemAppCredentials? app = await _credentials.GetAsync(provider, ct);
        if (app is null)
        {
            _logger.LogWarning(
                "Cannot backfill {Provider} Service row for broadcaster {BroadcasterId} — app credentials are not configured",
                provider,
                broadcasterId
            );
            return false;
        }

        await _mirror.MirrorAsync(
            broadcasterId,
            provider,
            access.Value.Value,
            refreshToken,
            access.Value.ExpiresAt,
            app.ClientId,
            app.ClientSecret,
            ct
        );
        return true;
    }

    /// <summary>The natural mirror key — tenant + lowercase provider — matching the Service row's (BroadcasterId, Name).</summary>
    private static string Key(Guid broadcasterId, string provider) =>
        $"{broadcasterId}:{provider.ToLowerInvariant()}";
}
