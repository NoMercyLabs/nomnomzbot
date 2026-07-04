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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the audited onboarding-repair fix in <see cref="AuthService.HandleTwitchCallbackAsync"/>: a streamer
/// re-authenticating against an EXISTING (already-onboarded) channel now re-publishes
/// <see cref="ChannelOnboardedEvent"/> on every successful login — not just the very first one. Before this
/// fix, the event was gated behind an <c>isNewChannel</c> flag that never re-fired for a returning streamer, so
/// their onboarding seed handlers (rewards, mods, subs, bans, event responses, …) could only repair via a full
/// server restart (<c>OnboardedChannelSeedBackfillService</c>). The handlers are documented idempotent, so
/// re-firing on every login is a safe repair path, proven here by driving the real callback twice and checking
/// no duplicate <c>Channel</c> row is ever created alongside the repeated publish.
/// </summary>
public sealed class AuthServiceReAuthOnboardingRepublishTests
{
    private const string TwitchUserId = "tw-100";

    [Fact]
    public async Task ReAuth_of_an_existing_channel_republishes_ChannelOnboardedEvent()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
        Guid channelId = Guid.Parse("0192a000-0000-7000-8000-00000000e002");

        db.Users.Add(
            new User
            {
                Id = ownerId,
                TwitchUserId = TwitchUserId,
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
            }
        );
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = ownerId,
                TwitchChannelId = TwitchUserId,
                Name = "stoney",
                NameNormalized = "stoney",
                IsOnboarded = true,
            }
        );
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        AuthService service = Build(db, bus);
        OAuthCallbackDto callback = new() { Code = "auth-code" };
        AuthContextDto context = new("web", "127.0.0.1", "test-agent");

        // First re-auth of the already-onboarded channel.
        Result<AuthResultDto> first = await service.HandleTwitchCallbackAsync(callback, context);
        first.IsSuccess.Should().BeTrue();

        // Second, independent re-auth (e.g. the streamer logs in again later).
        Result<AuthResultDto> second = await service.HandleTwitchCallbackAsync(callback, context);
        second.IsSuccess.Should().BeTrue();

        List<ChannelOnboardedEvent> published = bus
            .Published.OfType<ChannelOnboardedEvent>()
            .ToList();

        // The repair guarantee: BOTH logins re-published the event for the SAME existing channel — not just
        // the first (which the old isNewChannel-gated code would have skipped entirely, since the channel
        // already existed before either call).
        published.Should().HaveCount(2);
        published.Should().OnlyContain(e => e.BroadcasterId == channelId);
        published.Should().OnlyContain(e => e.TwitchChannelId == TwitchUserId);

        // Re-auth is a repair, never a duplicate-creation path — still exactly one Channel row.
        (await db.Channels.CountAsync())
            .Should()
            .Be(1);
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static AuthService Build(AuthDbContext db, RecordingEventBus bus)
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
                    ["user:read:email"]
                )
            );

        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .UpsertConnectionAsync(Arg.Any<UpsertConnectionDto>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new IntegrationConnectionDto(
                        Guid.NewGuid(),
                        null,
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
            .Returns(_ => new HttpClient(new FakeTwitchHelixHandler()));

        IConfiguration config = new ConfigurationBuilder().Build();

        return new AuthService(
            db,
            twitchAuth,
            Substitute.For<ITwitchDeviceCodeService>(),
            vault,
            sessions,
            bus,
            credentials,
            httpClientFactory,
            config,
            new DeploymentContext(DeploymentMode.SelfHostFull),
            TimeProvider.System,
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
