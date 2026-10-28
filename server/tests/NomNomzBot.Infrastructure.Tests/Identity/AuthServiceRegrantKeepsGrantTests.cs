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

using System.Collections.Specialized;
using System.Web;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A moderator or viewer who signs in again with Twitch keeps what they had: the scopes they granted before
/// (the authorize request carries them, so Twitch does not narrow the stored grant) and the channel their
/// session was on (the callback hands it to the new session, but only for the same person).
/// </summary>
public sealed class AuthServiceRegrantKeepsGrantTests
{
    // The account the fake Helix answers for (AuthServiceReAuthOnboardingRepublishTests.FakeTwitchHelixHandler).
    private const string TwitchUserId = "tw-100";

    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");

    private static readonly string[] ModeratorScopes =
    [
        "user:read:chat",
        "user:read:moderated_channels",
        "user:write:chat",
        "user:read:emotes",
        "moderator:manage:banned_users",
        "moderator:read:unban_requests",
        "moderator:manage:unban_requests",
        "moderator:read:blocked_terms",
        "moderator:manage:blocked_terms",
        "moderator:manage:chat_messages",
        "moderator:read:shield_mode",
        "moderator:manage:shield_mode",
        "moderator:manage:warnings",
        "moderator:manage:suspicious_users",
    ];

    private static readonly string[] HeldGrant = [.. ModeratorScopes, "user:read:email"];

    // ─── the scopes the authorize request asks for ─────────────────────────────

    [Fact]
    public async Task Moderator_start_asks_the_role_scopes_plus_every_scope_the_person_granted_before()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        await AddPersonalConnectionAsync(db, person, HeldGrant);

        string[] requested = await RequestedScopesAsync(db, TwitchLoginRole.Moderator, person.Id);

        requested.Should().BeEquivalentTo(HeldGrant);
    }

    [Fact]
    public async Task Viewer_start_keeps_the_moderator_scopes_the_person_granted_before()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        await AddPersonalConnectionAsync(db, person, HeldGrant);

        string[] requested = await RequestedScopesAsync(db, TwitchLoginRole.Viewer, person.Id);

        requested.Should().BeEquivalentTo(HeldGrant);
    }

    [Fact]
    public async Task A_start_with_no_signed_in_person_asks_the_role_scopes_only()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        await AddPersonalConnectionAsync(db, person, HeldGrant);

        string[] requested = await RequestedScopesAsync(db, TwitchLoginRole.Moderator, null);

        requested.Should().BeEquivalentTo(ModeratorScopes);
    }

    [Fact]
    public async Task A_start_never_borrows_the_grant_of_another_person_or_a_deleted_connection()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        User other = await AddUserAsync(db, "tw-200");
        await AddPersonalConnectionAsync(db, other, ["user:read:email", "user:read:follows"]);
        IntegrationConnection deleted = await AddPersonalConnectionAsync(
            db,
            person,
            ["user:read:subscriptions"]
        );
        deleted.DeletedAt = DateTime.UtcNow;
        // The platform bot's own connection, which this person connected as an admin.
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = null,
                Provider = "twitch",
                ProviderAccountId = "bot-id",
                Status = "connected",
                Scopes = ["user:bot", "user:manage:whispers"],
                ConnectedByUserId = person.Id,
            }
        );
        await db.SaveChangesAsync();

        string[] requested = await RequestedScopesAsync(db, TwitchLoginRole.Moderator, person.Id);

        requested.Should().BeEquivalentTo(ModeratorScopes);
    }

    [Fact]
    public async Task A_start_never_asks_again_for_the_scopes_of_the_persons_channel_connection()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        await AddPersonalConnectionAsync(db, person, ["user:read:email"]);
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = Channel,
                Provider = "twitch",
                Status = "connected",
                Scopes = ["user:bot", "channel:manage:raids"],
                ConnectedByUserId = person.Id,
            }
        );
        await db.SaveChangesAsync();

        string[] requested = await RequestedScopesAsync(db, TwitchLoginRole.Moderator, person.Id);

        requested.Should().BeEquivalentTo([.. ModeratorScopes, "user:read:email"]);
    }

    // ─── the scopes kept on the personal connection after the callback ─────────

    [Fact]
    public async Task Signing_in_again_leaves_the_personal_connection_holding_every_scope_granted_before()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        await AddPersonalConnectionAsync(db, person, HeldGrant);
        ITwitchAuthService twitch = Substitute.For<ITwitchAuthService>();
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(
            db,
            new RecordingEventBus(),
            NewRealVault(db),
            twitchAuthOverride: twitch
        );
        string[] asked = await AskedScopesAsync(service, person.Id);
        // Twitch issues a token with exactly the scopes the authorize request asked for.
        twitch
            .ExchangeCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new TokenResult("access", "refresh", DateTime.UtcNow.AddHours(4), asked));

        Result<AuthResultDto> result = await service.HandleTwitchViewerCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent")
        );

        result.IsSuccess.Should().BeTrue();
        List<IntegrationConnection> personal = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.BroadcasterId == null && c.Provider == "twitch")
            .ToListAsync();
        personal.Should().ContainSingle();
        personal[0].Scopes.Should().BeEquivalentTo(HeldGrant);
    }

    // ─── the channel the new session is created with ───────────────────────────

    [Fact]
    public async Task Viewer_callback_keeps_the_channel_when_the_arriving_person_is_the_one_who_signed_in()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        ISessionService sessions = NewSessions();
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(
            db,
            new RecordingEventBus(),
            sessionsOverride: sessions
        );

        Result<AuthResultDto> result = await service.HandleTwitchViewerCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent"),
            arrivingUserId: person.Id,
            arrivingChannelId: Channel
        );

        result.IsSuccess.Should().BeTrue();
        await sessions
            .Received(1)
            .CreateSessionAsync(
                Arg.Is<Guid>(id => id == person.Id),
                Arg.Is<Guid?>(channel => channel == Channel),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Viewer_callback_drops_the_channel_when_the_arriving_person_is_someone_else()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        ISessionService sessions = NewSessions();
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(
            db,
            new RecordingEventBus(),
            sessionsOverride: sessions
        );

        Result<AuthResultDto> result = await service.HandleTwitchViewerCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent"),
            arrivingUserId: Guid.NewGuid(),
            arrivingChannelId: Channel
        );

        result.IsSuccess.Should().BeTrue();
        await sessions
            .Received(1)
            .CreateSessionAsync(
                Arg.Is<Guid>(id => id == person.Id),
                Arg.Is<Guid?>(channel => channel == null),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Viewer_callback_without_an_arriving_person_opens_a_session_on_no_channel()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        User person = await AddUserAsync(db);
        ISessionService sessions = NewSessions();
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(
            db,
            new RecordingEventBus(),
            sessionsOverride: sessions
        );

        Result<AuthResultDto> result = await service.HandleTwitchViewerCallbackAsync(
            new() { Code = "auth-code" },
            new("web", "127.0.0.1", "test-agent"),
            arrivingChannelId: Channel
        );

        result.IsSuccess.Should().BeTrue();
        await sessions
            .Received(1)
            .CreateSessionAsync(
                Arg.Is<Guid>(id => id == person.Id),
                Arg.Is<Guid?>(channel => channel == null),
                Arg.Any<AuthContextDto>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static async Task<string[]> RequestedScopesAsync(
        AuthDbContext db,
        TwitchLoginRole role,
        Guid? userHint
    )
    {
        AuthService service = AuthServiceReAuthOnboardingRepublishTests.Build(
            db,
            new RecordingEventBus()
        );

        return await AskedScopesAsync(service, userHint, role);
    }

    private static async Task<string[]> AskedScopesAsync(
        AuthService service,
        Guid? userHint,
        TwitchLoginRole role = TwitchLoginRole.Moderator
    )
    {
        Result<string> url = await service.GetTwitchOAuthUrl(
            role,
            "state-1",
            "https://x.test",
            userHint: userHint
        );

        url.IsSuccess.Should().BeTrue();
        NameValueCollection query = HttpUtility.ParseQueryString(new Uri(url.Value).Query);
        return query["scope"]!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<User> AddUserAsync(
        AuthDbContext db,
        string twitchUserId = TwitchUserId
    )
    {
        User user = new()
        {
            TwitchUserId = twitchUserId,
            Platform = AuthEnums.Platform.Twitch,
            Username = twitchUserId,
            UsernameNormalized = twitchUserId,
            DisplayName = twitchUserId,
            Enabled = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<IntegrationConnection> AddPersonalConnectionAsync(
        AuthDbContext db,
        User person,
        IEnumerable<string> scopes
    )
    {
        IntegrationConnection connection = new()
        {
            BroadcasterId = null,
            Provider = "twitch",
            ProviderAccountId = person.TwitchUserId,
            ProviderAccountName = person.Username,
            Status = "connected",
            Scopes = [.. scopes],
            ConnectedByUserId = person.Id,
        };
        db.IntegrationConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection;
    }

    private static ISessionService NewSessions()
    {
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
        return sessions;
    }

    private static IIntegrationTokenVault NewRealVault(AuthDbContext db)
    {
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        return new IntegrationTokenVault(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
    }

    private sealed class PassthroughScopeGrant : IScopeGrantService
    {
        public IReadOnlyList<string> RequiredScopesFor(string featureKey) => [];

        public Task<Result<ScopeGrantState>> EnsureFeatureScopesAsync(
            Guid broadcasterId,
            string featureKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(new ScopeGrantState(true, null, [])));

        public Task<Result<IReadOnlyList<string>>> ReconcileGrantedScopesAsync(
            Guid connectionId,
            IReadOnlyList<string> actualScopes,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(actualScopes));
    }
}
