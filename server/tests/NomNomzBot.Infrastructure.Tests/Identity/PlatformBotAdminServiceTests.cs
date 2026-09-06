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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S-BOT-PLATFORM-UI: <c>a8b897e6</c> correctly stopped the channel Integrations screen from touching the
/// shared platform bot, but left NO surface at all to re-connect or replace it once first-run setup is done —
/// the operator's only route was a database edit. These prove the admin-plane surface this slice adds:
/// <list type="bullet">
/// <item>the real, three-way state (never-connected / working / token-unusable — the ENCRYPTION_KEY-rotation
/// case), read from the stored credential, never assumed;</item>
/// <item>a swap is genuinely gated on <c>platform:bot:manage</c> and audited naming the operator;</item>
/// <item>the blast radius (channels with no bot of their own) is counted fresh and a stale count fails
/// closed, exactly like <c>NetworkBlockService</c>;</item>
/// <item>a completed reconnect actually REPLACES the stored credential — proved against the real vault, not
/// a returned <c>OK</c>.</item>
/// </list>
/// </summary>
public sealed class PlatformBotAdminServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);
    private const string IncumbentBotId = "1335549269";
    private const string IncumbentBotLogin = "nomz_bot";

    private static (PlatformBotAdminService Sut, AuthDbContext Db, IAuthService AuthService) Build(
        DeploymentMode mode = DeploymentMode.Saas,
        IPlatformBotReadinessGate? readiness = null
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(Now);
        PlatformIamService iam = new(db, bus, clock, new(mode));
        IAuthService authService = Substitute.For<IAuthService>();
        PlatformBotAdminService sut = new(
            db,
            authService,
            readiness ?? Substitute.For<IPlatformBotReadinessGate>(),
            iam
        );
        return (sut, db, authService);
    }

    private static Guid SeedPrincipal(AuthDbContext db, params string[] permissionKeys)
    {
        Guid principalId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        db.IamPrincipals.Add(
            new()
            {
                Id = principalId,
                PrincipalType = IamPrincipalType.Employee,
                Name = "operator",
                IsActive = true,
            }
        );
        db.IamRoles.Add(new() { Id = roleId, Name = $"role-{roleId}" });
        foreach (string key in permissionKeys)
        {
            Guid permissionId = Guid.NewGuid();
            db.IamPermissions.Add(
                new()
                {
                    Id = permissionId,
                    Key = key,
                    Category = IamCategory.Iam,
                }
            );
            db.IamRolePermissions.Add(new() { RoleId = roleId, PermissionId = permissionId });
        }
        db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = principalId,
                RoleId = roleId,
                AssignedByPrincipalId = principalId,
            }
        );
        db.SaveChanges();
        return principalId;
    }

    private static Guid SeedChannel(AuthDbContext db, string name)
    {
        Guid channelId = Guid.NewGuid();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                TwitchChannelId = $"tw-{channelId}",
                Name = name,
                NameNormalized = name,
            }
        );
        db.SaveChanges();
        return channelId;
    }

    private static BotAccount SeedSharedBot(
        AuthDbContext db,
        string botUserId = IncumbentBotId,
        string botUsername = IncumbentBotLogin
    )
    {
        BotAccount bot = new()
        {
            Id = Guid.NewGuid(),
            IdentityType = AuthEnums.BotIdentityType.Shared,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = botUserId,
            BotUsername = botUsername,
            IsActive = true,
        };
        db.BotAccounts.Add(bot);
        db.SaveChanges();
        return bot;
    }

    private static void SeedCustomBotFor(AuthDbContext db, Guid broadcasterId)
    {
        BotAccount bot = new()
        {
            Id = Guid.NewGuid(),
            IdentityType = AuthEnums.BotIdentityType.Custom,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = $"custom-{broadcasterId}",
            BotUsername = $"custom_bot_{broadcasterId}",
            IsActive = true,
        };
        db.BotAccounts.Add(bot);
        db.ChannelBotAuthorizations.Add(
            new()
            {
                BroadcasterId = broadcasterId,
                BotAccountId = bot.Id,
                AuthorizedAt = Now.UtcDateTime,
                IsActive = true,
            }
        );
        db.SaveChanges();
    }

    // ── DONE-WHEN 1: the real three-way state ──────────────────────────────────

    [Fact]
    public async Task Status_reports_NeverConnected_when_no_shared_bot_row_exists()
    {
        (PlatformBotAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);

        Result<PlatformBotAdminStatusDto> result = await sut.GetStatusAsync(principal);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.State.Should().Be(PlatformBotConnectionState.NeverConnected);
        result.Value.BotUsername.Should().BeNull();
    }

    [Fact]
    public async Task Status_reports_ConnectedAndWorking_when_the_stored_token_decrypts()
    {
        IPlatformBotReadinessGate readiness = Substitute.For<IPlatformBotReadinessGate>();
        readiness.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        (PlatformBotAdminService sut, AuthDbContext db, _) = Build(readiness: readiness);
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);
        SeedSharedBot(db);

        Result<PlatformBotAdminStatusDto> result = await sut.GetStatusAsync(principal);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.State.Should().Be(PlatformBotConnectionState.ConnectedAndWorking);
        result.Value.BotUsername.Should().Be(IncumbentBotLogin);
    }

    /// <summary>The ENCRYPTION_KEY-rotation case: a bot row exists but the stored token no longer decrypts —
    /// this must be distinguishable from both "working" and "never connected".</summary>
    [Fact]
    public async Task Status_reports_ConnectedTokenUnusable_when_the_stored_token_no_longer_decrypts()
    {
        IPlatformBotReadinessGate readiness = Substitute.For<IPlatformBotReadinessGate>();
        readiness.IsPlatformBotConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);
        (PlatformBotAdminService sut, AuthDbContext db, _) = Build(readiness: readiness);
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);
        SeedSharedBot(db);

        Result<PlatformBotAdminStatusDto> result = await sut.GetStatusAsync(principal);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.State.Should()
            .Be(
                PlatformBotConnectionState.ConnectedTokenUnusable,
                "a bot row exists but the token cannot be read — never-connected and unusable must not collapse to the same state"
            );
        result
            .Value.BotUsername.Should()
            .Be(IncumbentBotLogin, "the operator needs to know WHICH account needs re-authorizing");
    }

    // ── DONE-WHEN 2: genuinely gated + audited ─────────────────────────────────

    [Fact]
    public async Task Status_is_refused_as_a_genuine_denial_without_the_permission_and_is_audited()
    {
        (PlatformBotAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, IamPermissionKeys.TenantRead); // holds a key, but not this one

        Result<PlatformBotAdminStatusDto> result = await sut.GetStatusAsync(principal);

        result
            .IsFailure.Should()
            .BeTrue("a denial must be a genuine failure, not an empty/default status");
        result.ErrorCode.Should().Be("FORBIDDEN");

        IamAuditLog audit = await db.IamAuditLogs.SingleAsync(a =>
            a.Permission == IamPermissionKeys.PlatformBotManage
        );
        audit.Outcome.Should().Be(IamOutcome.Denied);
        audit
            .PrincipalId.Should()
            .Be(principal, "the audit row must name the operator who was denied");
    }

    [Fact]
    public async Task PreviewReconnect_requires_a_justification_and_the_denial_is_audited_against_the_operator()
    {
        (PlatformBotAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);

        Result<PlatformBotReconnectPreviewDto> allowed = await sut.PreviewReconnectAsync(
            principal,
            "rotating after an ENCRYPTION_KEY change"
        );
        allowed.IsSuccess.Should().BeTrue(allowed.ErrorMessage);

        IamAuditLog audit = await db.IamAuditLogs.SingleAsync(a =>
            a.Permission == IamPermissionKeys.PlatformBotManage
        );
        audit.Outcome.Should().Be(IamOutcome.Allowed);
        audit.PrincipalId.Should().Be(principal);
        audit.Justification.Should().Be("rotating after an ENCRYPTION_KEY change");
    }

    // ── DONE-WHEN 3: counted, real, fail-closed on staleness ───────────────────

    [Fact]
    public async Task Preview_counts_only_channels_with_no_active_bot_of_their_own()
    {
        (PlatformBotAdminService sut, AuthDbContext db, _) = Build();
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);
        SeedChannel(db, "channel-1");
        SeedChannel(db, "channel-2");
        Guid channelWithOwnBot = SeedChannel(db, "channel-3");
        SeedCustomBotFor(db, channelWithOwnBot);

        Result<PlatformBotReconnectPreviewDto> result = await sut.PreviewReconnectAsync(
            principal,
            "rotate"
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.AffectedChannelCount.Should()
            .Be(2, "channel-3 speaks through its own custom bot");
    }

    [Fact]
    public async Task StartReconnect_fails_closed_on_a_stale_count_and_never_starts_the_device_login()
    {
        (PlatformBotAdminService sut, AuthDbContext db, IAuthService authService) = Build();
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);
        SeedChannel(db, "channel-1");
        SeedChannel(db, "channel-2");
        // The real count is 2 — the operator is acting on a stale preview that said 5.

        Result<DeviceCodeStartDto> result = await sut.StartReconnectAsync(
            principal,
            "rotate",
            confirmedAffectedChannelCount: 5
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PREVIEW_STALE");
        await authService.DidNotReceive().StartBotDeviceLoginAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PollReconnect_fails_closed_on_a_stale_count_and_never_polls_twitch()
    {
        (PlatformBotAdminService sut, AuthDbContext db, IAuthService authService) = Build();
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);
        SeedChannel(db, "channel-1");
        // The real count is 1 now — a channel must have registered its own bot since the preview said 3.

        Result<DeviceBotPollDto> result = await sut.PollReconnectAsync(
            principal,
            "device-code",
            "rotate",
            confirmedAffectedChannelCount: 3
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PREVIEW_STALE");
        await authService
            .DidNotReceive()
            .PollBotDeviceLoginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartReconnect_with_the_current_count_delegates_to_the_bot_device_login()
    {
        (PlatformBotAdminService sut, AuthDbContext db, IAuthService authService) = Build();
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);
        SeedChannel(db, "channel-1");
        authService
            .StartBotDeviceLoginAsync(Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new DeviceCodeStartDto("dev-code", "USER-1", "https://x", 5, 600))
            );

        Result<DeviceCodeStartDto> result = await sut.StartReconnectAsync(
            principal,
            "rotate",
            confirmedAffectedChannelCount: 1
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.DeviceCode.Should().Be("dev-code");
    }

    // ── DONE-WHEN 4: a completed reconnect actually replaces the credential ────

    /// <summary>
    /// Runs the REAL <see cref="AuthService"/> + REAL <see cref="IntegrationTokenVault"/> underneath
    /// <see cref="PlatformBotAdminService"/> so the proof is the actual state change — not a returned
    /// <c>Authorized</c> status. The incumbent's token can no longer decrypt (simulated ENCRYPTION_KEY
    /// rotation via a corrupted ciphertext column); after the operator completes the gated reconnect, the
    /// SAME shared bot row now holds a token that DOES decrypt to the freshly authorized value.
    /// </summary>
    [Fact]
    public async Task CompletedReconnect_replaces_the_stored_credential_so_the_bot_resolves_to_the_new_token()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        IIntegrationTokenVault vault = new IntegrationTokenVault(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Twitch:ClientId"] = "cid" })
            .Build();

        // The incumbent shared bot, connected with a token the vault can decrypt just fine.
        BotAccount incumbent = SeedSharedBot(db);
        Result<IntegrationConnectionDto> oldConnection = await vault.UpsertConnectionAsync(
            new(
                BroadcasterId: null,
                "twitch_bot",
                IncumbentBotId,
                IncumbentBotLogin,
                ["user:write:chat", "user:read:chat"],
                "cid",
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );
        oldConnection.IsSuccess.Should().BeTrue(oldConnection.ErrorMessage);
        await vault.StoreTokensAsync(
            oldConnection.Value.Id,
            new("stale-access-token", "stale-refresh-token", null, DateTime.UtcNow.AddHours(4))
        );
        incumbent.ConnectionId = oldConnection.Value.Id;
        await db.SaveChangesAsync();

        // Simulate the ENCRYPTION_KEY-rotation case: the access token row's ciphertext is now unreadable.
        IntegrationToken tokenRow = await db.IntegrationTokens.SingleAsync(t =>
            t.ConnectionId == oldConnection.Value.Id && t.TokenType == AuthEnums.TokenType.Access
        );
        tokenRow.CipherText = "not-valid-ciphertext-anymore";
        await db.SaveChangesAsync();
        (await vault.GetAccessTokenAsync(oldConnection.Value.Id))
            .IsFailure.Should()
            .BeTrue(
                "the corrupted ciphertext must actually fail to decrypt, or this test proves nothing"
            );

        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        deviceCode
            .PollOnceAsync(
                "device-code",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new DevicePollOutcome(
                    DevicePollStatus.Authorized,
                    new TokenResult(
                        "fresh-access-token",
                        "fresh-refresh-token",
                        DateTime.UtcNow.AddHours(4),
                        ["user:write:chat", "user:read:chat"]
                    )
                )
            );

        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory
            .CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(
                new SucceedingHelixHandler(IncumbentBotId, IncumbentBotLogin)
            ));

        AuthService authService = new(
            db,
            Substitute.For<ITwitchAuthService>(),
            deviceCode,
            vault,
            Substitute.For<ISessionService>(),
            Substitute.For<ISessionRevocationService>(),
            new RecordingEventBus(),
            AuthTestBuilder.CredentialsProvider(db, protector, config),
            httpClientFactory,
            config,
            new(DeploymentMode.Saas),
            TimeProvider.System,
            new(),
            Substitute.For<IPlatformOwnerPrincipalMinter>(),
            NullLogger<AuthService>.Instance
        );

        PlatformIamService iam = new(
            db,
            new RecordingEventBus(),
            TimeProvider.System,
            new(DeploymentMode.Saas)
        );
        PlatformBotAdminService sut = new(
            db,
            authService,
            Substitute.For<IPlatformBotReadinessGate>(),
            iam
        );
        Guid principal = SeedPrincipal(db, IamPermissionKeys.PlatformBotManage);

        Result<DeviceBotPollDto> pollResult = await sut.PollReconnectAsync(
            principal,
            "device-code",
            "re-authorizing after an ENCRYPTION_KEY rotation",
            confirmedAffectedChannelCount: 0
        );

        pollResult.IsSuccess.Should().BeTrue(pollResult.ErrorMessage);
        pollResult.Value.Status.Should().Be(DeviceLoginStatus.Authorized);

        // The state change, not the returned status: the SAME shared bot row's connection now decrypts to
        // the FRESH token — every channel resolving through this row gets the new account from here on.
        BotAccount reloaded = await db.BotAccounts.SingleAsync(b => b.Id == incumbent.Id);
        reloaded.ConnectionId.Should().NotBeNull();
        Result<DecryptedTokenDto> newAccess = await vault.GetAccessTokenAsync(
            reloaded.ConnectionId!.Value
        );
        newAccess.IsSuccess.Should().BeTrue(newAccess.ErrorMessage);
        newAccess.Value.Value.Should().Be("fresh-access-token");
    }

    /// <summary>A passthrough scope-grant so the vault's reconcile call is a no-op for this real-vault test.</summary>
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
        ) => Task.FromResult(Result.Success<IReadOnlyList<string>>(actualScopes));
    }

    private sealed class SucceedingHelixHandler(string userId, string login) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
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
                                id = userId,
                                login,
                                display_name = login,
                                profile_image_url = (string?)null,
                                broadcaster_type = "",
                                type = "",
                                created_at = DateTime.UtcNow,
                            },
                        },
                    }
                ),
            };
            return Task.FromResult(response);
        }
    }
}
