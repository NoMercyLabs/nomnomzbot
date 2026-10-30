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

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Automation.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Obs.Entities;
using NomNomzBot.Infrastructure.Admin.Diagnostics;
using NomNomzBot.Infrastructure.Admin.Diagnostics.Connections;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Admin;

/// <summary>
/// S-ADMIN-CONNECTIONS: the <c>connections</c> diagnostics section reports the health of every platform and
/// integration connection one channel has. Runs the REAL section, every real source, the REAL token vault over
/// the real envelope crypto stack and the REAL scope-notification service against a SQLite database, and proves
/// the returned states, the decrypt check, the isolation between channels and that no secret leaves the section.
/// </summary>
public sealed class ChannelConnectionsDiagnosticSectionTests
{
    private static readonly Guid Tenant = Guid.Parse("0199a000-0000-7000-8000-000000000e10");
    private static readonly Guid OtherTenant = Guid.Parse("0199a000-0000-7000-8000-000000000e11");
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] Secrets =
    [
        "SECRET-ACCESS-TOKEN",
        "SECRET-REFRESH-TOKEN",
        "SECRET-OBS-PASSWORD",
        "SECRET-OBS-BRIDGE",
        "SECRET-VTS-PLUGIN",
        "SECRET-VTS-BRIDGE",
        "SECRET-AUTOMATION-HASH",
        "SECRET-SUPPORTER-AUTH",
        "SECRET-OUTBOUND-SIGNING",
        "SECRET-OUTBOUND-SECOND",
        "SECRET-INBOUND-TOKEN",
        "SECRET-INBOUND-VERIFY",
        "SECRET-SERVICE-CLIENT",
        "SECRET-SERVICE-ACCESS",
        "SECRET-SERVICE-REFRESH",
    ];

    private sealed record Harness(
        AuthDbContext Db,
        IntegrationTokenVault Vault,
        ChannelConnectionsDiagnosticSection Section
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new NoopScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        ScopeNotificationService scopes = new(
            db,
            new ServiceCollection().BuildServiceProvider(),
            new(),
            TimeProvider.System,
            NullLogger<ScopeNotificationService>.Instance
        );
        ChannelConnectionsDiagnosticSection section = new([
            new PlatformConnectionSource(db),
            new IntegrationConnectionSource(db, vault, scopes),
            new DiscordGuildConnectionSource(db),
            new ObsConnectionSource(db),
            new VtsConnectionSource(db),
            new AutomationTokenConnectionSource(db, new FakeTimeProvider(Now)),
            new SupporterConnectionSource(db),
            new WebhookConnectionSource(db),
            new ServiceRowConnectionSource(db),
        ]);
        return new(db, vault, section);
    }

    private static async Task<ChannelDiagnosticsDto> ReadAsync(Harness harness, Guid broadcasterId)
    {
        ChannelDiagnosticsDto diagnostics = new() { BroadcasterId = broadcasterId };
        Result result = await harness.Section.ReadIntoAsync(
            new(broadcasterId, new(), false, null),
            diagnostics
        );
        result.IsSuccess.Should().BeTrue();
        return diagnostics;
    }

    private static async Task<Guid> ConnectAsync(
        Harness harness,
        Guid broadcasterId,
        string provider,
        string accountId,
        string accountName,
        string[] scopes,
        DateTime expiresAt
    )
    {
        Result<IntegrationConnectionDto> connection = await harness.Vault.UpsertConnectionAsync(
            new(broadcasterId, provider, accountId, accountName, scopes, null, false, null, null)
        );
        Result stored = await harness.Vault.StoreTokensAsync(
            connection.Value.Id,
            new("SECRET-ACCESS-TOKEN", "SECRET-REFRESH-TOKEN", null, expiresAt)
        );
        stored.IsSuccess.Should().BeTrue();
        return connection.Value.Id;
    }

    [Fact]
    public async Task A_healthy_twitch_link_a_spotify_token_nobody_can_open_and_a_discord_server_give_three_rows()
    {
        Harness harness = Build();
        DateTime expires = Now.AddHours(1);
        await ConnectAsync(
            harness,
            Tenant,
            AuthEnums.IntegrationProvider.Twitch,
            "tw-1",
            "made_up_streamer",
            ["channel:read:subscriptions", "chat:read"],
            expires
        );
        Guid spotifyId = await ConnectAsync(
            harness,
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "sp-1",
            "made_up_listener",
            ["user-read-playback-state"],
            expires
        );
        await harness
            .Db.IntegrationTokens.Where(t => t.ConnectionId == spotifyId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.CipherText, "v1|corrupted"));
        harness.Db.DiscordGuildConnections.AddRange(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Tenant,
                GuildId = "guild-1",
                GuildName = "Made Up Guild",
                BotInstalled = true,
                ServerConsentStatus = "approved",
                ApprovedAt = Now.AddDays(-3),
            },
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Tenant,
                GuildId = "guild-gone",
                GuildName = "Unlinked Guild",
                BotInstalled = true,
                DeletedAt = Now.AddDays(-1),
            }
        );
        await ConnectAsync(
            harness,
            OtherTenant,
            AuthEnums.IntegrationProvider.Spotify,
            "sp-other",
            "someone_else",
            [],
            expires
        );
        await harness.Db.SaveChangesAsync();

        ChannelDiagnosticsDto body = await ReadAsync(harness, Tenant);

        IReadOnlyList<ChannelDiagnosticConnectionDto> rows = body.Connections!;
        rows.Select(r => $"{r.Kind}/{r.Provider}")
            .Should()
            .Equal("discordGuild/discord", "integration/spotify", "integration/twitch");

        ChannelDiagnosticConnectionDto twitch = rows.Single(r => r.Provider == "twitch");
        twitch.Id.Should().NotBeNullOrEmpty();
        twitch.AccountName.Should().Be("made_up_streamer");
        twitch.AccountId.Should().Be("tw-1");
        twitch.Connected.Should().BeTrue();
        twitch.Status.Should().Be("connected");
        twitch.NeedsReauth.Should().BeFalse();
        twitch.DecryptFailed.Should().BeFalse();
        twitch.GrantedScopes.Should().BeEquivalentTo("channel:read:subscriptions", "chat:read");
        twitch.TokenExpiresAt.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
        twitch.LastError.Should().BeNull();
        twitch.LastErrorAt.Should().BeNull();

        ChannelDiagnosticConnectionDto spotify = rows.Single(r => r.Provider == "spotify");
        spotify.AccountName.Should().Be("made_up_listener");
        spotify.DecryptFailed.Should().BeTrue();
        spotify.Connected.Should().BeFalse();
        spotify.Status.Should().Be("decrypt_failed");
        spotify.NeedsReauth.Should().BeFalse();
        spotify.TokenExpiresAt.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
        spotify.MissingScopes.Should().BeEmpty();

        ChannelDiagnosticConnectionDto discord = rows.Single(r => r.Provider == "discord");
        discord.Kind.Should().Be(ChannelConnectionKinds.DiscordGuild);
        discord.AccountName.Should().Be("Made Up Guild");
        discord.AccountId.Should().Be("guild-1");
        discord.Connected.Should().BeTrue();
        discord.Status.Should().Be("approved");
        discord.NeedsReauth.Should().BeFalse();
        discord.DecryptFailed.Should().BeFalse();

        IntegrationConnectionStatusOf(harness.Db, spotifyId)
            .Should()
            .Be(
                AuthEnums.IntegrationStatus.DecryptFailed,
                "the vault marks a token it cannot open"
            );
    }

    private static string IntegrationConnectionStatusOf(AuthDbContext db, Guid connectionId) =>
        db.IntegrationConnections.AsNoTracking().Single(c => c.Id == connectionId).Status;

    [Fact]
    public async Task A_refused_refresh_shows_as_needs_reauth_with_its_failure_count_and_time()
    {
        Harness harness = Build();
        Guid connectionId = await ConnectAsync(
            harness,
            Tenant,
            AuthEnums.IntegrationProvider.YouTube,
            "yt-1",
            "made_up_channel",
            [],
            Now.AddHours(-1)
        );
        await harness.Vault.MarkRefreshRefusedAsync(connectionId, "invalid_grant");

        ChannelDiagnosticsDto body = await ReadAsync(harness, Tenant);

        ChannelDiagnosticConnectionDto youtube = body.Connections!.Single();
        youtube.Provider.Should().Be("youtube");
        youtube.NeedsReauth.Should().BeTrue();
        youtube.Connected.Should().BeFalse();
        youtube.Status.Should().Be("needs_reauth");
        youtube.DecryptFailed.Should().BeFalse();
        youtube.ConsecutiveFailures.Should().Be(1);
        youtube.LastErrorAt.Should().NotBeNull();
        youtube.TokenExpiresAt.Should().BeCloseTo(Now.AddHours(-1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_twitch_link_that_lacks_one_scope_lists_exactly_that_scope()
    {
        Harness harness = Build();
        string[] everyScope =
        [
            .. FeatureScopeMap
                .Features.SelectMany(f => f.Value)
                .Concat(new TwitchScopeRegistry().FullCatalogue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(s => s != TwitchScopes.ModeratorManageShoutouts),
        ];
        await ConnectAsync(
            harness,
            Tenant,
            AuthEnums.IntegrationProvider.Twitch,
            "tw-1",
            "made_up_streamer",
            everyScope,
            Now.AddHours(1)
        );

        ChannelDiagnosticsDto body = await ReadAsync(harness, Tenant);

        ChannelDiagnosticConnectionDto twitch = body.Connections!.Single();
        twitch.MissingScopes.Should().Equal(TwitchScopes.ModeratorManageShoutouts);
        twitch.GrantedScopes.Should().NotContain(TwitchScopes.ModeratorManageShoutouts);
        twitch.Connected.Should().BeTrue();
    }

    [Fact]
    public async Task A_channel_with_no_connections_gets_an_empty_list_not_a_missing_section()
    {
        Harness harness = Build();

        ChannelDiagnosticsDto body = await ReadAsync(harness, Tenant);

        body.Connections.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Every_store_the_code_base_keeps_shows_its_own_states()
    {
        Harness harness = Build();
        await SeedEveryStoreAsync(harness.Db);

        ChannelDiagnosticsDto body = await ReadAsync(harness, Tenant);

        IReadOnlyList<ChannelDiagnosticConnectionDto> rows = body.Connections!;
        rows.Select(r => r.Kind)
            .Should()
            .Equal(
                "automationToken",
                "automationToken",
                "automationToken",
                "obs",
                "platform",
                "service",
                "supporter",
                "vts",
                "webhook",
                "webhook"
            );

        ChannelDiagnosticConnectionDto kick = rows.Single(r => r.Kind == "platform");
        kick.Provider.Should().Be("kick");
        kick.AccountName.Should().Be("made_up_kick");
        kick.AccountId.Should().Be("kick-77");
        kick.Connected.Should().BeTrue();

        ChannelDiagnosticConnectionDto obs = rows.Single(r => r.Kind == "obs");
        obs.Provider.Should().Be("obs");
        obs.AccountName.Should().Be("192.0.2.10:4455");
        obs.Connected.Should().BeFalse();
        obs.Status.Should().Be("enabled");
        obs.LastError.Should().Be("Connection refused");
        obs.LastActivityAt.Should().Be(Now.AddHours(-1));

        ChannelDiagnosticConnectionDto vts = rows.Single(r => r.Kind == "vts");
        vts.AccountName.Should().Be("ws://127.0.0.1:8001");
        vts.Connected.Should().BeTrue();
        vts.Status.Should().Be("connected");
        vts.LastActivityAt.Should().Be(Now.AddMinutes(-5));

        List<ChannelDiagnosticConnectionDto> tokens =
        [
            .. rows.Where(r => r.Kind == "automationToken"),
        ];
        ChannelDiagnosticConnectionDto active = tokens.Single(t => t.AccountName == "stream deck");
        active.Connected.Should().BeTrue();
        active.Status.Should().Be("active");
        active.TokenExpiresAt.Should().Be(Now.AddDays(30));
        active.GrantedScopes.Should().Equal("pipelines:run");
        active.LastActivityAt.Should().Be(Now.AddMinutes(-2));
        ChannelDiagnosticConnectionDto expired = tokens.Single(t => t.AccountName == "old script");
        expired.Connected.Should().BeFalse();
        expired.Status.Should().Be("expired");
        ChannelDiagnosticConnectionDto revoked = tokens.Single(t => t.AccountName == "lost laptop");
        revoked.Connected.Should().BeFalse();
        revoked.Status.Should().Be("revoked");

        ChannelDiagnosticConnectionDto supporter = rows.Single(r => r.Kind == "supporter");
        supporter.Provider.Should().Be("kofi");
        supporter.Connected.Should().BeFalse();
        supporter.Status.Should().Be("error");
        supporter.LastActivityAt.Should().Be(Now.AddDays(-1));

        ChannelDiagnosticConnectionDto outbound = rows.Single(r =>
            r.AccountName == "discord relay"
        );
        outbound.Provider.Should().Be("webhook_outbound");
        outbound.AccountId.Should().Be("hooks.example.test");
        outbound.Connected.Should().BeFalse();
        outbound.Status.Should().Be("disabled");
        outbound.LastError.Should().Be("10 consecutive failures");
        outbound.LastErrorAt.Should().Be(Now.AddHours(-2));
        outbound.ConsecutiveFailures.Should().Be(10);
        outbound.LastActivityAt.Should().Be(Now.AddHours(-3));

        ChannelDiagnosticConnectionDto inbound = rows.Single(r => r.AccountName == "kofi in");
        inbound.Provider.Should().Be("webhook_inbound");
        inbound.Connected.Should().BeTrue();
        inbound.Status.Should().Be("enabled");
        inbound.LastActivityAt.Should().Be(Now.AddMinutes(-30));

        ChannelDiagnosticConnectionDto service = rows.Single(r => r.Kind == "service");
        service.Provider.Should().Be("spotify");
        service.AccountName.Should().Be("made_up_listener");
        service.Connected.Should().BeTrue();
        service.GrantedScopes.Should().Equal("user-read-playback-state");
        service.TokenExpiresAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public async Task No_token_secret_or_url_token_appears_anywhere_in_the_response()
    {
        Harness harness = Build();
        await SeedEveryStoreAsync(harness.Db);
        await ConnectAsync(
            harness,
            Tenant,
            AuthEnums.IntegrationProvider.Twitch,
            "tw-1",
            "made_up_streamer",
            ["chat:read"],
            Now.AddHours(1)
        );
        Guid brokenId = await ConnectAsync(
            harness,
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "sp-1",
            "made_up_listener",
            [],
            Now.AddHours(1)
        );
        await harness
            .Db.IntegrationTokens.Where(t => t.ConnectionId == brokenId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.CipherText, "v1|corrupted"));

        ChannelDiagnosticsDto body = await ReadAsync(harness, Tenant);

        string json = JsonSerializer.Serialize(body);
        body.Connections!.Should().HaveCount(12);
        foreach (string secret in Secrets)
            json.Should().NotContain(secret);
        json.Should().NotContain("v1|", "no sealed envelope leaves the vault either");
        json.Should().NotContain("nnz_ab12", "not even the visible prefix of an automation token");
    }

    private static async Task SeedEveryStoreAsync(AuthDbContext db)
    {
        db.PlatformConnections.Add(
            new()
            {
                ChannelId = Tenant,
                Provider = AuthEnums.Platform.Kick,
                ExternalChannelId = "kick-77",
                DisplayName = "made_up_kick",
            }
        );
        db.ObsConnections.AddRange(
            new ObsConnection
            {
                BroadcasterId = Tenant,
                Mode = "direct",
                Host = "192.0.2.10",
                Port = 4455,
                PasswordCipher = "SECRET-OBS-PASSWORD",
                BridgeToken = "SECRET-OBS-BRIDGE",
                IsEnabled = true,
                LastConnectedAt = Now.AddHours(-1),
                LastError = "Connection refused",
            },
            new ObsConnection
            {
                BroadcasterId = OtherTenant,
                Host = "198.51.100.1",
                IsEnabled = true,
            }
        );
        db.VtsConnections.Add(
            new()
            {
                BroadcasterId = Tenant,
                Endpoint = "ws://127.0.0.1:8001",
                PluginTokenCipher = "SECRET-VTS-PLUGIN",
                BridgeToken = "SECRET-VTS-BRIDGE",
                IsEnabled = true,
                Status = "connected",
                LastConnectedAt = Now.AddMinutes(-5),
            }
        );
        db.AutomationApiTokens.AddRange(
            AutomationToken("stream deck", Now.AddDays(30), null, Now.AddMinutes(-2)),
            AutomationToken("old script", Now.AddDays(-1), null, null),
            AutomationToken("lost laptop", null, Now.AddDays(-4), null)
        );
        db.SupporterConnections.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Tenant,
                SourceKey = "kofi",
                ConnectionMode = "webhook",
                AuthSecretCipher = "SECRET-SUPPORTER-AUTH",
                IsEnabled = true,
                Status = "error",
                LastEventAt = Now.AddDays(-1),
            }
        );
        db.OutboundWebhookEndpoints.Add(
            new()
            {
                BroadcasterId = Tenant,
                Name = "discord relay",
                Fqdn = "hooks.example.test",
                SigningSecretEnvelope = "SECRET-OUTBOUND-SIGNING",
                SecondarySigningSecretEnvelope = "SECRET-OUTBOUND-SECOND",
                IsEnabled = false,
                DisabledAt = Now.AddHours(-2),
                DisabledReason = "10 consecutive failures",
                ConsecutiveFailureCount = 10,
                LastDeliveryAt = Now.AddHours(-3),
            }
        );
        db.InboundWebhookEndpoints.Add(
            new()
            {
                BroadcasterId = Tenant,
                Name = "kofi in",
                Token = "SECRET-INBOUND-TOKEN",
                VerificationSecretEnvelope = "SECRET-INBOUND-VERIFY",
                IsEnabled = true,
                LastReceivedAt = Now.AddMinutes(-30),
            }
        );
        db.Services.Add(
            new()
            {
                BroadcasterId = Tenant,
                Name = "Spotify",
                Enabled = true,
                UserName = "made_up_listener",
                ClientId = "client-1",
                ClientSecret = "SECRET-SERVICE-CLIENT",
                AccessToken = "SECRET-SERVICE-ACCESS",
                RefreshToken = "SECRET-SERVICE-REFRESH",
                TokenExpiry = Now.AddHours(1),
                Scopes = ["user-read-playback-state"],
            }
        );
        await db.SaveChangesAsync();
    }

    private static AutomationApiToken AutomationToken(
        string name,
        DateTime? expiresAt,
        DateTime? revokedAt,
        DateTime? lastUsedAt
    ) =>
        new()
        {
            BroadcasterId = Tenant,
            Name = name,
            TokenHash = "SECRET-AUTOMATION-HASH",
            TokenPrefix = "nnz_ab12",
            ScopesJson = """["pipelines:run"]""",
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
            LastUsedAt = lastUsedAt,
            CreatedByUserId = Guid.NewGuid(),
        };

    private sealed class NoopScopeGrant : IScopeGrantService
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
        ) => Task.FromResult(Result.Success<IReadOnlyList<string>>([]));
    }
}
