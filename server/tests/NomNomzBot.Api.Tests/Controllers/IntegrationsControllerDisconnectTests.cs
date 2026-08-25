// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S036c-c — proves the two disconnect paths <c>IntegrationsController.Disconnect</c> now serves. YouTube
/// custody moved off the legacy <c>Service</c> row into <see cref="IIntegrationTokenVault"/> (S036c); before
/// this slice the generic <c>DELETE /integrations/{id}</c> fell back to a <c>Service</c>-row lookup for any
/// provider not special-cased, which was a silent no-op/404 for YouTube post-migration since no YouTube
/// <c>Service</c> row is ever created any more. Spotify still mirrors into the legacy <c>Service</c> row
/// (identity-auth §3.4 NOTE), so it is the regression case proving the generic Service-row fallback still
/// works unchanged for the providers it already handled.
/// </summary>
public sealed class IntegrationsControllerDisconnectTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static IntegrationsController Build(
        IntegrationsControllerDisconnectTestDbContext db,
        IIntegrationTokenVault vault
    ) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            new NoopDiscordGuildService(),
            new NoopIntegrationStatusService(),
            new NoopChannelSpotifyCredentialsService(),
            vault
        );

    [Fact]
    public async Task Disconnect_youtube_revokes_the_vaulted_connection_and_it_stops_reading_back_as_connected()
    {
        IntegrationsControllerDisconnectTestDbContext db =
            IntegrationsControllerDisconnectTestDbContext.New();
        RecordingIntegrationTokenVault vault = new(db);
        IntegrationsController controller = Build(db, vault);

        Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
            new(
                BroadcasterId: Tenant,
                Provider: AuthEnums.IntegrationProvider.YouTube,
                ProviderAccountId: "yt-external-1",
                ProviderAccountName: "yt-streamer",
                Scopes: ["https://www.googleapis.com/auth/youtube.readonly"],
                ClientId: null,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );
        upsert.IsSuccess.Should().BeTrue();
        Guid connectionId = upsert.Value.Id;

        IActionResult result = await controller.Disconnect(
            Tenant.ToString(),
            "youtube",
            CancellationToken.None
        );

        result.Should().BeOfType<NoContentResult>();

        IntegrationConnection? afterDisconnect = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == connectionId);
        afterDisconnect.Should().NotBeNull();
        afterDisconnect.Status.Should().Be(AuthEnums.IntegrationStatus.Revoked);

        // A subsequent resolve (the same query YouTubeAccessTokenProvider/IntegrationStatusService run)
        // no longer finds a non-revoked connection — YouTube reads back as disconnected.
        bool stillResolvesAsConnected = await db.IntegrationConnections.AnyAsync(c =>
            c.BroadcasterId == Tenant
            && c.Provider == AuthEnums.IntegrationProvider.YouTube
            && c.Status != AuthEnums.IntegrationStatus.Revoked
        );
        stillResolvesAsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task Disconnect_youtube_with_no_connection_returns_not_found_without_touching_the_vault()
    {
        IntegrationsControllerDisconnectTestDbContext db =
            IntegrationsControllerDisconnectTestDbContext.New();
        RecordingIntegrationTokenVault vault = new(db);
        IntegrationsController controller = Build(db, vault);

        IActionResult result = await controller.Disconnect(
            Tenant.ToString(),
            "youtube",
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundObjectResult>();
        vault.RevokeCalls.Should().BeEmpty();
    }

    /// <summary>Regression: the generic Service-row fallback still disconnects a provider that was never
    /// migrated off it (Spotify mirrors its OAuth token into a <c>Service</c> row alongside the vault).</summary>
    [Fact]
    public async Task Disconnect_spotify_still_removes_the_legacy_service_row_unchanged()
    {
        IntegrationsControllerDisconnectTestDbContext db =
            IntegrationsControllerDisconnectTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = Tenant,
                AccessToken = "spotify-access",
                RefreshToken = "spotify-refresh",
            }
        );
        await db.SaveChangesAsync();

        RecordingIntegrationTokenVault vault = new(db);
        IntegrationsController controller = Build(db, vault);

        IActionResult result = await controller.Disconnect(
            Tenant.ToString(),
            "spotify",
            CancellationToken.None
        );

        result.Should().BeOfType<NoContentResult>();

        bool stillPresent = await db.Services.AnyAsync(s =>
            s.BroadcasterId == Tenant && s.Name == "spotify"
        );
        stillPresent.Should().BeFalse();
        vault.RevokeCalls.Should().BeEmpty("Spotify disconnect never touches the vault path");
    }

    /// <summary>Records every call so a test can assert exactly which path the controller took.</summary>
    private sealed class RecordingIntegrationTokenVault(
        IntegrationsControllerDisconnectTestDbContext db
    ) : IIntegrationTokenVault
    {
        public List<Guid> RevokeCalls { get; } = [];

        public async Task<Result<IntegrationConnectionDto>> UpsertConnectionAsync(
            UpsertConnectionDto request,
            CancellationToken cancellationToken = default
        )
        {
            IntegrationConnection connection = new()
            {
                BroadcasterId = request.BroadcasterId,
                Provider = request.Provider,
                ProviderAccountId = request.ProviderAccountId,
                ProviderAccountName = request.ProviderAccountName,
                Status = AuthEnums.IntegrationStatus.Connected,
                Scopes = [.. request.Scopes],
                ClientId = request.ClientId,
                IsByok = request.IsByok,
                ConnectedByUserId = request.ConnectedByUserId,
            };
            db.IntegrationConnections.Add(connection);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(
                new IntegrationConnectionDto(
                    connection.Id,
                    connection.BroadcasterId,
                    connection.Provider,
                    connection.ProviderAccountId,
                    connection.ProviderAccountName,
                    connection.Status,
                    connection.Scopes,
                    connection.IsByok,
                    connection.ConnectedAt,
                    connection.LastRefreshedAt,
                    connection.ConsecutiveFailureCount
                )
            );
        }

        public Task<Result> StoreTokensAsync(
            Guid connectionId,
            StoreTokensDto tokens,
            IReadOnlyList<string>? grantedScopes = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<DecryptedTokenDto>> GetAccessTokenAsync(
            Guid connectionId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<DecryptedTokenDto>> GetRefreshTokenAsync(
            Guid connectionId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> MarkRefreshFailureAsync(
            Guid connectionId,
            string error,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public async Task<Result> RevokeConnectionAsync(
            Guid connectionId,
            string reason,
            CancellationToken cancellationToken = default
        )
        {
            RevokeCalls.Add(connectionId);
            IntegrationConnection? connection = await db
                .IntegrationConnections.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
            if (connection is null)
                return Result.Success();

            connection.Status = AuthEnums.IntegrationStatus.Revoked;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        public Task<Result<IReadOnlyList<IntegrationConnectionDto>>> ListConnectionsAsync(
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class NoopDiscordGuildService : IDiscordGuildService
    {
        public Task<Result<IReadOnlyList<DiscordGuildConnectionDto>>> GetConnectionsAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<DiscordGuildConnectionDto>> GetConnectionAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<DiscordGuildConnectionDto>> UpsertFromOAuthAsync(
            Guid broadcasterId,
            DiscordGuildOAuthResult oauth,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> ApproveServerConsentAsync(
            Guid broadcasterId,
            Guid connectionId,
            string approvedByDiscordUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> RevokeServerConsentAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> SetStreamerEnabledAsync(
            Guid broadcasterId,
            Guid connectionId,
            bool enabled,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> DisconnectAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<bool>> IsLinkActiveAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class NoopIntegrationStatusService : IIntegrationStatusService
    {
        public Task<Result<List<ChannelIntegrationDto>>> GetStatusesAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class NoopChannelSpotifyCredentialsService : IChannelSpotifyCredentialsService
    {
        public Task<Result<ChannelSpotifyCredentialsDto>> GetAsync(
            Guid channelId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<ChannelSpotifyCredentialsDto>> SetAsync(
            Guid channelId,
            SetChannelSpotifyCredentialsDto request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<ChannelSpotifyCredentialsDto>> ClearAsync(
            Guid channelId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
