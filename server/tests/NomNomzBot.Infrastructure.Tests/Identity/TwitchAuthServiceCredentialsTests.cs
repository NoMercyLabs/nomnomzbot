// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Configuration;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the onboarding keystone end to end on a real OAuth path: the Twitch app client id + secret a user
/// saved through the wizard (vaulted to the DB) are the credentials <see cref="TwitchAuthService.ExchangeCodeAsync"/>
/// actually sends to Twitch's token endpoint — closing the original bug where the code exchange read a static
/// startup <c>IOptions&lt;TwitchOptions&gt;</c> and ignored the saved creds. Also proves the clean
/// not-configured behavior: no credentials → no malformed request, a null result.
/// </summary>
public sealed class TwitchAuthServiceCredentialsTests
{
    private static (TwitchAuthService Service, RecordingTokenHandler Wire) Build(
        IConfiguration config
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(out _);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );
        RecordingTokenHandler wire = new();
        TwitchAuthService service = new(
            db,
            new ThrowingVault(),
            credentials,
            new SingleClientFactory(wire),
            NullLogger<TwitchAuthService>.Instance,
            TimeProvider.System
        );
        return (service, wire);
    }

    /// <summary>Saves an app secret the wizard way: id as a plain Value row, secret sealed under its AAD.</summary>
    private static async Task SeedTwitchAppAsync(
        AuthDbContext db,
        ITokenProtector protector,
        string clientId,
        string clientSecret
    )
    {
        db.Configurations.Add(
            new ConfigEntity
            {
                BroadcasterId = null,
                Key = "twitch.client_id",
                Value = clientId,
            }
        );
        db.Configurations.Add(
            new ConfigEntity
            {
                BroadcasterId = null,
                Key = "twitch.client_secret",
                SecureValue = await protector.ProtectAsync(
                    clientSecret,
                    SystemCredentialsProvider.ContextFor("twitch.client_secret")
                ),
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExchangeCodeAsync_UsesTheWizardSavedDbCredentials_NotStaticConfig()
    {
        // Config carries a DECOY app (the old static IOptions path). The DB carries the real wizard-saved app.
        // The exchange must send the DB app's id + secret, proving wizard creds now drive the OAuth.
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(out _);
        await SeedTwitchAppAsync(db, protector, "wizard-client-id", "wizard-secret");

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Twitch:ClientId"] = "decoy-config-id",
                    ["Twitch:ClientSecret"] = "decoy-config-secret",
                }
            )
            .Build();

        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );
        RecordingTokenHandler wire = new();
        TwitchAuthService service = new(
            db,
            new ThrowingVault(),
            credentials,
            new SingleClientFactory(wire),
            NullLogger<TwitchAuthService>.Instance,
            TimeProvider.System
        );

        TokenResult? result = await service.ExchangeCodeAsync(
            "the-auth-code",
            "https://api.example.test/api/v1/auth/twitch/callback"
        );

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("issued-access");

        // The wire carries the WIZARD-VAULTED credentials and the auth code — not the config decoy.
        wire.LastBody.Should().Contain("client_id=wizard-client-id");
        wire.LastBody.Should().Contain("client_secret=wizard-secret");
        wire.LastBody.Should().Contain("code=the-auth-code");
        wire.LastBody.Should().Contain("grant_type=authorization_code");
        wire.LastBody.Should().NotContain("decoy-config-id");
    }

    [Fact]
    public async Task ExchangeCodeAsync_WhenNotConfigured_ReturnsNull_WithoutCallingTwitch()
    {
        // No DB row, no config — wholly unconfigured. The exchange must short-circuit to a null result and
        // never issue a (malformed, secret-less) request to Twitch.
        (TwitchAuthService service, RecordingTokenHandler wire) = Build(
            new ConfigurationBuilder().Build()
        );

        TokenResult? result = await service.ExchangeCodeAsync(
            "code",
            "https://api.example.test/api/v1/auth/twitch/callback"
        );

        result.Should().BeNull();
        wire.CallCount.Should().Be(0);
    }

    // ── doubles ──────────────────────────────────────────────────────────────

    /// <summary>Records the token-endpoint request body and returns a canned successful token response.</summary>
    private sealed class RecordingTokenHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"issued-access","refresh_token":"issued-refresh","expires_in":3600,"scope":["user:read:chat"],"token_type":"bearer"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// The token vault at its seam: <see cref="TwitchAuthService.ExchangeCodeAsync"/> is pure HTTP and must not
    /// touch the vault (the caller vaults the result), so every member throws — a reach here would be a bug.
    /// </summary>
    private sealed class ThrowingVault : IIntegrationTokenVault
    {
        public Task<Result<IntegrationConnectionDto>> UpsertConnectionAsync(
            UpsertConnectionDto request,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("ExchangeCodeAsync must not vault.");

        public Task<Result> StoreTokensAsync(
            Guid connectionId,
            StoreTokensDto tokens,
            IReadOnlyList<string>? grantedScopes = null,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("ExchangeCodeAsync must not vault.");

        public Task<Result<DecryptedTokenDto>> GetAccessTokenAsync(
            Guid connectionId,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("ExchangeCodeAsync must not vault.");

        public Task<Result<DecryptedTokenDto>> GetRefreshTokenAsync(
            Guid connectionId,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("ExchangeCodeAsync must not vault.");

        public Task<Result> MarkRefreshFailureAsync(
            Guid connectionId,
            string error,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("ExchangeCodeAsync must not vault.");

        public Task<Result> RevokeConnectionAsync(
            Guid connectionId,
            string reason,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("ExchangeCodeAsync must not vault.");

        public Task<Result<IReadOnlyList<IntegrationConnectionDto>>> ListConnectionsAsync(
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("ExchangeCodeAsync must not vault.");
    }
}
