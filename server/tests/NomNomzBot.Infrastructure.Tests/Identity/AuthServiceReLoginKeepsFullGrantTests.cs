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
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// "The bot keeps asking for scope permissions the user already granted" (seven reports). A plain re-login
/// asks Twitch for the login minimum only, so its token holds only those scopes. Saving that token over the
/// channel's full broadcaster token, even with the stored scope LIST unioned, left every Helix call on the
/// narrow token; the next refresh read the real scopes off it, shrank the list, and every job re-raised its
/// missing-scope row. A login whose token lacks a scope the healthy stored grant holds therefore keeps the
/// stored token; any other login stores its token with the scopes it really carries.
/// </summary>
public sealed class AuthServiceReLoginKeepsFullGrantTests
{
    private const string TwitchUserId = "tw-100";
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000f002");

    [Fact]
    public async Task A_relogin_with_a_narrower_token_keeps_the_stored_full_grant_and_its_token()
    {
        AuthDbContext db = await SeedAsync(
            AuthEnums.IntegrationStatus.Connected,
            ["user:read:email", "channel:manage:raids", "moderator:read:followers"]
        );
        IIntegrationTokenVault vault = NewVault();
        AuthService service = Build(db, vault, ["user:read:email"]);

        Result<AuthResultDto> result = await service.HandleTwitchCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent")
        );

        result.IsSuccess.Should().BeTrue();
        await vault
            .DidNotReceive()
            .StoreTokensAsync(
                Arg.Any<Guid>(),
                Arg.Any<StoreTokensDto>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
        await vault
            .DidNotReceive()
            .UpsertConnectionAsync(Arg.Any<UpsertConnectionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_relogin_over_a_revoked_grant_stores_the_new_token_with_only_the_scopes_it_carries()
    {
        AuthDbContext db = await SeedAsync(
            AuthEnums.IntegrationStatus.Revoked,
            ["user:read:email", "channel:manage:raids"]
        );
        IIntegrationTokenVault vault = NewVault();
        AuthService service = Build(db, vault, ["user:read:email"]);

        Result<AuthResultDto> result = await service.HandleTwitchCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent")
        );

        result.IsSuccess.Should().BeTrue();
        await vault
            .Received(1)
            .StoreTokensAsync(
                Arg.Any<Guid>(),
                Arg.Is<StoreTokensDto>(t => t.AccessToken == "access-token"),
                Arg.Is<IReadOnlyList<string>>(scopes =>
                    scopes.Count == 1 && scopes.Contains("user:read:email")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_login_whose_token_holds_every_stored_scope_replaces_the_stored_token()
    {
        AuthDbContext db = await SeedAsync(
            AuthEnums.IntegrationStatus.Connected,
            ["user:read:email", "channel:manage:raids"]
        );
        IIntegrationTokenVault vault = NewVault();
        AuthService service = Build(
            db,
            vault,
            ["user:read:email", "channel:manage:raids", "channel:read:vips"]
        );

        Result<AuthResultDto> result = await service.HandleTwitchCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent")
        );

        result.IsSuccess.Should().BeTrue();
        await vault
            .Received(1)
            .StoreTokensAsync(
                Arg.Any<Guid>(),
                Arg.Is<StoreTokensDto>(t => t.AccessToken == "access-token"),
                Arg.Is<IReadOnlyList<string>>(scopes =>
                    scopes.Count == 3 && scopes.Contains("channel:read:vips")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── scaffolding ──────────────────────────────────────────────────────────────────────────────────────

    private static async Task<AuthDbContext> SeedAsync(string status, List<string> storedScopes)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = Guid.Parse("0192a000-0000-7000-8000-00000000f001");

        db.Users.Add(
            new()
            {
                Id = ownerId,
                TwitchUserId = TwitchUserId,
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                OwnerUserId = ownerId,
                TwitchChannelId = TwitchUserId,
                Name = "stoney",
                NameNormalized = "stoney",
                IsOnboarded = true,
            }
        );
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = ChannelId,
                Provider = AuthEnums.IntegrationProvider.Twitch,
                ProviderAccountId = TwitchUserId,
                Status = status,
                Scopes = storedScopes,
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static IIntegrationTokenVault NewVault()
    {
        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .UpsertConnectionAsync(Arg.Any<UpsertConnectionDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result.Success(
                    new IntegrationConnectionDto(
                        Guid.NewGuid(),
                        ChannelId,
                        "twitch",
                        TwitchUserId,
                        "stoney",
                        "connected",
                        call.Arg<UpsertConnectionDto>().Scopes,
                        false,
                        DateTime.UtcNow,
                        null,
                        0
                    )
                )
            );
        vault
            .StoreTokensAsync(
                Arg.Any<Guid>(),
                Arg.Any<StoreTokensDto>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        return vault;
    }

    private static AuthService Build(
        AuthDbContext db,
        IIntegrationTokenVault vault,
        string[] tokenScopes
    )
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync("twitch", Arg.Any<CancellationToken>())
            .Returns("public-client-id");

        ITwitchAuthService twitchAuth = Substitute.For<ITwitchAuthService>();
        twitchAuth
            .ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new TokenResult(
                    "access-token",
                    "refresh-token",
                    DateTime.UtcNow.AddHours(4),
                    tokenScopes
                )
            );

        ISessionService sessions = Substitute.For<ISessionService>();
        sessions
            .CreateSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new SessionTokensDto(
                        "session-jwt",
                        "raw-refresh-token",
                        DateTime.UtcNow.AddHours(1),
                        DateTime.UtcNow.AddDays(30),
                        Guid.NewGuid()
                    )
                )
            );

        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory
            .CreateClient(Arg.Any<string>())
            .Returns(_ => new(new FakeTwitchHelixHandler()));

        IConfiguration config = new ConfigurationBuilder().Build();

        return new(
            db,
            twitchAuth,
            Substitute.For<ITwitchDeviceCodeService>(),
            vault,
            sessions,
            Substitute.For<ISessionRevocationService>(),
            new RecordingEventBus(),
            credentials,
            httpClientFactory,
            config,
            new(DeploymentMode.SelfHostFull),
            TimeProvider.System,
            new(),
            Substitute.For<IPlatformOwnerPrincipalMinter>(),
            Substitute.For<IBillingTierService>(),
            NullLogger<AuthService>.Instance
        );
    }

    /// <summary>Answers the two Helix reads <c>EstablishStreamerSessionAsync</c> makes: the user lookup (used
    /// to resolve the logged-in Twitch identity) and the best-effort chat-color fetch (empty — ignored).</summary>
    private sealed class FakeTwitchHelixHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri?.AbsolutePath ?? "";

            if (path == "/helix/users")
            {
                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(
                        new
                        {
                            data = new[]
                            {
                                new
                                {
                                    id = TwitchUserId,
                                    login = "stoney",
                                    display_name = "Stoney",
                                    profile_image_url = (string?)null,
                                    broadcaster_type = "affiliate",
                                    type = "",
                                    created_at = new DateTime(
                                        2020,
                                        1,
                                        1,
                                        0,
                                        0,
                                        0,
                                        DateTimeKind.Utc
                                    ),
                                },
                            },
                        }
                    ),
                };
                return Task.FromResult(response);
            }

            if (path == "/helix/chat/color")
            {
                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { data = Array.Empty<object>() }),
                };
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
