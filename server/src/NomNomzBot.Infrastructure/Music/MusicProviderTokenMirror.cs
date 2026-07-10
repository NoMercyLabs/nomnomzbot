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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Mirrors a connected music-provider OAuth grant into the legacy <see cref="Service"/> token store that
/// <c>SpotifyMusicProvider</c> / <c>YouTubeMusicProvider</c> (and <c>IntegrationsController.ListIntegrations</c>)
/// read from. It seals the tokens + client credentials under the exact
/// <see cref="TokenProtectionContext"/> those providers unseal — <c>(broadcasterId, provider, field)</c> for
/// fields <c>access</c> / <c>refresh</c> / <c>client_id</c> / <c>client_secret</c> — so once the row exists the
/// providers' own refresh-on-demand + rotation path takes over. See <see cref="IMusicProviderTokenMirror"/>
/// for why this bridge exists (canonical store is the crypto vault; this is a mirror until the providers read
/// the vault directly).
/// </summary>
public sealed class MusicProviderTokenMirror : IMusicProviderTokenMirror
{
    // The providers whose tokens live in the Service store (the generic OAuth registry's music providers).
    // A non-music connect (nothing here reads the Service row for it) is a no-op.
    private static readonly HashSet<string> MusicProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        AuthEnums.IntegrationProvider.Spotify,
        AuthEnums.IntegrationProvider.YouTube,
    };

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly ILogger<MusicProviderTokenMirror> _logger;

    public MusicProviderTokenMirror(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        ILogger<MusicProviderTokenMirror> logger
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _logger = logger;
    }

    public async Task MirrorAsync(
        Guid broadcasterId,
        string provider,
        string accessToken,
        string? refreshToken,
        DateTime? tokenExpiry,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default
    )
    {
        if (!MusicProviders.Contains(provider))
            return;

        string name = provider.ToLowerInvariant();
        string subjectId = broadcasterId.ToString();

        Service? service = await _db.Services.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Name == name,
            cancellationToken
        );

        bool isNew = service is null;
        service ??= new Service { Name = name, BroadcasterId = broadcasterId };

        service.Enabled = true;
        service.TokenExpiry = tokenExpiry;
        service.AccessToken = await _tokenProtector.ProtectAsync(
            accessToken,
            new TokenProtectionContext(subjectId, name, "access"),
            cancellationToken
        );
        service.RefreshToken = refreshToken is not null
            ? await _tokenProtector.ProtectAsync(
                refreshToken,
                new TokenProtectionContext(subjectId, name, "refresh"),
                cancellationToken
            )
            : null;
        service.ClientId = await _tokenProtector.ProtectAsync(
            clientId,
            new TokenProtectionContext(subjectId, name, "client_id"),
            cancellationToken
        );
        service.ClientSecret = await _tokenProtector.ProtectAsync(
            clientSecret,
            new TokenProtectionContext(subjectId, name, "client_secret"),
            cancellationToken
        );

        if (isNew)
            _db.Services.Add(service);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Mirrored {Provider} OAuth tokens into the Service store for broadcaster {BroadcasterId} ({Action})",
            name,
            broadcasterId,
            isNew ? "created" : "updated"
        );
    }
}
