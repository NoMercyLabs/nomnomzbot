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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Content.Identity;

/// <summary>
/// S036c-a — step 1 of 3. YouTube custody still lives on the legacy flat <c>Service</c> table
/// (<see cref="NomNomzBot.Infrastructure.Integrations.YouTube.YouTubeAccessTokenProvider"/>'s doc comment
/// explains why), while <c>IntegrationOAuthService</c>'s connect flow ALREADY dual-writes every NEW YouTube
/// connect into both the <c>Service</c> row and the vaulted <c>IntegrationConnection</c>/<c>IntegrationToken</c>
/// rows (the "token-store bridge" note there). Accounts that connected BEFORE that dual-write existed have a
/// <c>Service</c> row and NO vault row, so <see cref="IIntegrationTokenVault"/> reads them back as
/// disconnected. This seeder closes that gap for every EXISTING YouTube <c>Service</c> row: it re-encrypts the
/// SAME plaintext tokens into the vault via <see cref="IIntegrationTokenVault.UpsertConnectionAsync"/> +
/// <see cref="IIntegrationTokenVault.StoreTokensAsync"/> — the exact calls the connect flow makes — so it hand-rolls
/// no new crypto path.
/// </summary>
/// <remarks>
/// This step is DELIBERATELY additive only: it changes no reader. The <c>Service</c> table stays authoritative
/// and untouched (no delete, no mutation) until a later slice cuts consumers over to the vault. Runs late
/// (Order 920, after the music-provider Service backfill at 910 — order between the two does not matter, they
/// touch disjoint stores, but this keeps the two YouTube-adjacent backfills adjacent in the seeder list).
/// Idempotent: an anti-join against LIVE <c>IntegrationConnection</c> rows for (BroadcasterId, "youtube") skips
/// anything already vaulted, so a warm database is a fast no-op and a re-run never inserts a duplicate row —
/// the vault's own <c>UpsertConnectionAsync</c> also upserts by (BroadcasterId, Provider), so even a race
/// converges on one row. Fail-safe: a per-row guard logs and continues, so one undecryptable or malformed
/// legacy row can never abort startup.
/// </remarks>
public sealed class YouTubeServiceConnectionBackfillSeeder : ISeeder
{
    private const string Provider = AuthEnums.IntegrationProvider.YouTube;
    private const string PlatformSubject = "_platform";

    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly ITokenProtector _tokenProtector;
    private readonly ILogger<YouTubeServiceConnectionBackfillSeeder> _logger;

    public YouTubeServiceConnectionBackfillSeeder(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        ITokenProtector tokenProtector,
        ILogger<YouTubeServiceConnectionBackfillSeeder> logger
    )
    {
        _db = db;
        _vault = vault;
        _tokenProtector = tokenProtector;
        _logger = logger;
    }

    public int Order => 920;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        List<Service> legacyServices = await _db
            .Services.Where(s => s.Name == Provider && s.AccessToken != null)
            .ToListAsync(ct);

        if (legacyServices.Count == 0)
            return;

        // Anti-join: the BroadcasterId values that already have a LIVE vault connection for YouTube, matching
        // how the vault itself reads (IgnoreQueryFilters + explicit DeletedAt, independent of ambient tenant).
        HashSet<Guid?> alreadyVaulted =
        [
            .. await _db
                .IntegrationConnections.IgnoreQueryFilters()
                .Where(c => c.Provider == Provider && c.DeletedAt == null)
                .Select(c => c.BroadcasterId)
                .ToListAsync(ct),
        ];

        int backfilled = 0;
        foreach (Service service in legacyServices)
        {
            if (alreadyVaulted.Contains(service.BroadcasterId))
                continue;

            try
            {
                if (await TryBackfillAsync(service, ct))
                    backfilled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail-safe: one malformed/undecryptable legacy row must not abort startup for every other
                // channel's backfill.
                _logger.LogWarning(
                    ex,
                    "Skipped YouTube vault backfill for broadcaster {BroadcasterId} — the legacy Service row could not be migrated",
                    service.BroadcasterId
                );
            }
        }

        if (backfilled > 0)
            _logger.LogInformation(
                "Backfilled {Count} YouTube Service row(s) into the token vault",
                backfilled
            );
    }

    /// <summary>
    /// Decrypts the legacy <c>Service</c> row's tokens under the SAME AAD context
    /// <c>YouTubeAccessTokenProvider</c> uses to read them, then re-protects them into the vault via the
    /// normal connect-flow calls. Returns false (and logs) when the access token cannot be decrypted, so the
    /// row is left for a reconnect rather than a broken/empty vault connection.
    /// </summary>
    private async Task<bool> TryBackfillAsync(Service service, CancellationToken ct)
    {
        string subject = service.BroadcasterId?.ToString() ?? PlatformSubject;

        string? accessToken = await _tokenProtector.TryUnprotectAsync(
            service.AccessToken,
            new(subject, Provider, "access"),
            ct
        );
        if (accessToken is null)
        {
            _logger.LogWarning(
                "Cannot backfill YouTube vault connection for broadcaster {BroadcasterId} — the legacy access token could not be decrypted",
                service.BroadcasterId
            );
            return false;
        }

        string? refreshToken = service.RefreshToken is null
            ? null
            : await _tokenProtector.TryUnprotectAsync(
                service.RefreshToken,
                new(subject, Provider, "refresh"),
                ct
            );

        string? clientId = service.ClientId is null
            ? null
            : await _tokenProtector.TryUnprotectAsync(
                service.ClientId,
                new(subject, Provider, "client_id"),
                ct
            );

        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new(
                service.BroadcasterId,
                Provider,
                service.UserId,
                service.UserName,
                service.Scopes,
                clientId,
                IsByok: clientId is not null,
                ConnectedByUserId: null,
                SettingsJson: null
            ),
            ct
        );
        if (connection.IsFailure)
        {
            _logger.LogWarning(
                "Cannot backfill YouTube vault connection for broadcaster {BroadcasterId} — {Error}",
                service.BroadcasterId,
                connection.ErrorMessage
            );
            return false;
        }

        Result store = await _vault.StoreTokensAsync(
            connection.Value.Id,
            new(accessToken, refreshToken, AppToken: null, service.TokenExpiry),
            grantedScopes: null,
            ct
        );
        return store.IsSuccess;
    }
}
