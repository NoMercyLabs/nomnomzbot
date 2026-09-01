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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S-OWN01 — "the bot keeps asking for scope permissions the user already granted." Root cause: a plain
/// re-login (<see cref="AuthService.HandleTwitchCallbackAsync"/> reaching the private
/// <c>EstablishStreamerSessionAsync</c>) with no live web session cookie to resolve the channel up front
/// (desktop, a fresh browser, or an expired session) requests only the base minimal scope set — Twitch's
/// token response then carries only that narrower set. That response used to be vaulted verbatim, silently
/// SHRINKING the connection's <c>Scopes</c> back down on every such re-login and re-opening every
/// missing-scope gap the operator had already closed with an earlier additive re-grant. This proves the
/// fix: the scopes handed to <see cref="IIntegrationTokenVault.UpsertConnectionAsync"/> and
/// <see cref="IIntegrationTokenVault.StoreTokensAsync"/> are the union of what the connection already held
/// and what this login's token carries — never a downgrade.
/// </summary>
public sealed class AuthServiceReLoginUnionsScopesTests
{
    private const string TwitchUserId = "tw-100";

    [Fact]
    public async Task ReLogin_with_only_the_minimal_scope_set_does_not_drop_a_previously_granted_scope()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
        Guid channelId = Guid.Parse("0192a000-0000-7000-8000-00000000f002");

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
                Id = channelId,
                OwnerUserId = ownerId,
                TwitchChannelId = TwitchUserId,
                Name = "stoney",
                NameNormalized = "stoney",
                IsOnboarded = true,
            }
        );
        // An earlier additive re-grant already widened this connection well past the login minimum — the
        // OAuth response this re-login gets back (stubbed below) carries only "user:read:email".
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = channelId,
                Provider = AuthEnums.IntegrationProvider.Twitch,
                ProviderAccountId = TwitchUserId,
                Status = "connected",
                Scopes = ["user:read:email", "channel:manage:raids", "moderator:read:followers"],
            }
        );
        await db.SaveChangesAsync();

        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .UpsertConnectionAsync(Arg.Any<UpsertConnectionDto>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new IntegrationConnectionDto(
                        Guid.NewGuid(),
                        channelId,
                        "twitch",
                        TwitchUserId,
                        "stoney",
                        "connected",
                        ["user:read:email"],
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

        AuthService service = Build(db, vault);

        OAuthCallbackDto callback = new() { Code = "auth-code" };
        AuthContextDto context = new("web", "127.0.0.1", "test-agent");

        Result<AuthResultDto> result = await service.HandleTwitchCallbackAsync(callback, context);

        result.IsSuccess.Should().BeTrue();

        // Both vault calls must carry the UNION, not the OAuth response's narrower "user:read:email" alone —
        // the previously-granted scopes the operator already consented to must survive this re-login.
        await vault
            .Received(1)
            .UpsertConnectionAsync(
                Arg.Is<UpsertConnectionDto>(dto =>
                    dto.Scopes.Contains("user:read:email")
                    && dto.Scopes.Contains("channel:manage:raids")
                    && dto.Scopes.Contains("moderator:read:followers")
                ),
                Arg.Any<CancellationToken>()
            );
        await vault
            .Received(1)
            .StoreTokensAsync(
                Arg.Any<Guid>(),
                Arg.Any<StoreTokensDto>(),
                Arg.Is<IReadOnlyList<string>>(scopes =>
                    scopes.Contains("user:read:email")
                    && scopes.Contains("channel:manage:raids")
                    && scopes.Contains("moderator:read:followers")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── scaffolding (mirrors AuthServiceReAuthOnboardingRepublishTests.Build, but keeps the vault mock so
    // the test above can assert on the arguments it was called with) ──────────────────────────────────────

    private static AuthService Build(AuthDbContext db, IIntegrationTokenVault vault)
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
                    ["user:read:email"] // the login minimum this re-login's response carries back
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
