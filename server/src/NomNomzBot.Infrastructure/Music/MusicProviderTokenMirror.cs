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
/// <c>SpotifyMusicProvider</c> (and <c>IntegrationsController.ListIntegrations</c>'s Spotify auth-status
/// read) still reads from. It seals the tokens + client credentials under the exact
/// <see cref="TokenProtectionContext"/> that provider unseals — <c>(broadcasterId, provider, field)</c> for
/// fields <c>access</c> / <c>refresh</c> / <c>client_id</c> / <c>client_secret</c> — so once the row exists the
/// provider's own refresh-on-demand + rotation path takes over. See <see cref="IMusicProviderTokenMirror"/>
/// for why this bridge exists (canonical store is the crypto vault; this is a mirror until Spotify reads
/// the vault directly).
///
/// <para>
/// <b>S036c-b — YouTube migrated OFF this mirror.</b> Every YouTube token reader now resolves through
/// <see cref="NomNomzBot.Application.Identity.Services.IIntegrationTokenVault"/> directly
/// (<c>YouTubeAccessTokenProvider</c>), so mirroring YouTube's OAuth grant into the legacy <c>Service</c>
/// row would only create a second, silently-drifting copy of a token nothing reads any more — YouTube is
/// deliberately excluded from <see cref="MusicProviders"/>.
/// </para>
/// </summary>
public sealed class MusicProviderTokenMirror : IMusicProviderTokenMirror
{
    // The providers whose tokens live in the Service store. YouTube is deliberately absent (S036c-b) —
    // it reads the vault directly and nothing consumes a mirrored Service row for it any more.
    private static readonly HashSet<string> MusicProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        AuthEnums.IntegrationProvider.Spotify,
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
        IReadOnlyList<string>? grantedScopes = null,
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
        service ??= new() { Name = name, BroadcasterId = broadcasterId };

        service.Enabled = true;
        service.TokenExpiry = tokenExpiry;
        service.AccessToken = await _tokenProtector.ProtectAsync(
            accessToken,
            new(subjectId, name, "access"),
            cancellationToken
        );
        service.RefreshToken = refreshToken is not null
            ? await _tokenProtector.ProtectAsync(
                refreshToken,
                new(subjectId, name, "refresh"),
                cancellationToken
            )
            : null;
        service.ClientId = await _tokenProtector.ProtectAsync(
            clientId,
            new(subjectId, name, "client_id"),
            cancellationToken
        );
        service.ClientSecret = await _tokenProtector.ProtectAsync(
            clientSecret,
            new(subjectId, name, "client_secret"),
            cancellationToken
        );
        if (grantedScopes is not null)
            service.Scopes = [.. grantedScopes];

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
