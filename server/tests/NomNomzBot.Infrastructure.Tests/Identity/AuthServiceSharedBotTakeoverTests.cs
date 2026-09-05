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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The shared bot is a PLATFORM identity — the deployment owner owns that Twitch account and the client it
/// speaks through. Both endpoints that establish it are <c>[AllowAnonymous]</c> (the first-run wizard has
/// nobody signed in, and a device-code poll carries no JWT), so nothing stopped an arbitrary visitor from
/// completing the bot device flow with their own account and installing themselves as the platform bot.
/// <para>
/// That is not hypothetical: on 2026-09-04 a channel account (<c>qtkitte</c>) landed beside <c>nomz_bot</c>,
/// both rows <c>IdentityType=shared</c> and <c>IsActive=true</c>, both pointing at the same
/// <c>twitch_bot</c> connection — so "which account is the bot" became order-dependent, and EventSub chat
/// subscriptions were signed by one identity while naming the other.
/// </para>
/// These prove the slot is guarded without breaking the two cases that must stay open: first run, and
/// re-authorizing the bot that already holds the slot.
/// </summary>
public sealed class AuthServiceSharedBotTakeoverTests
{
    private const string IncumbentUserId = "1335549269";
    private const string StrangerUserId = "76424740";

    [Fact]
    public async Task AStranger_CannotTakeOverAnEstablishedSharedBot()
    {
        (AuthService service, AuthDbContext db) = Build();
        SeedSharedBot(db, IncumbentUserId, "nomz_bot");

        Result result = await service.EnsureSharedBotSlotAvailableAsync(
            User(StrangerUserId, "qtkitte")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SHARED_BOT_ALREADY_CONNECTED");
        result
            .ErrorMessage.Should()
            .Contain("nomz_bot", "the operator needs to know what holds the slot");
    }

    /// <summary>First run: no shared bot yet, so the onboarding wizard must be able to establish one.</summary>
    [Fact]
    public async Task WithNoSharedBotYet_TheSlotIsOpen()
    {
        (AuthService service, AuthDbContext _) = Build();

        Result result = await service.EnsureSharedBotSlotAvailableAsync(
            User(IncumbentUserId, "nomz_bot")
        );

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Re-auth of the SAME bot must keep working — an expired or scope-extended grant is renewed through this
    /// exact path, and blocking it would strand the bot with no way back short of a DB edit.
    /// </summary>
    [Fact]
    public async Task TheIncumbentBot_CanReauthorizeItself()
    {
        (AuthService service, AuthDbContext db) = Build();
        SeedSharedBot(db, IncumbentUserId, "nomz_bot");

        Result result = await service.EnsureSharedBotSlotAvailableAsync(
            User(IncumbentUserId, "nomz_bot")
        );

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// A bot that was disconnected (soft-deleted / deactivated) frees the slot, so the admin plane's
    /// "disconnect it, then connect a different one" instruction in the failure message is actually true.
    /// </summary>
    [Fact]
    public async Task ADisconnectedIncumbent_FreesTheSlot()
    {
        (AuthService service, AuthDbContext db) = Build();
        SeedSharedBot(db, IncumbentUserId, "nomz_bot", isActive: false);

        Result result = await service.EnsureSharedBotSlotAvailableAsync(
            User(StrangerUserId, "someone_else")
        );

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// A per-channel CUSTOM bot is a different thing entirely — many are legitimate, one per channel — and
    /// must never be mistaken for the shared slot's holder.
    /// </summary>
    [Fact]
    public async Task ACustomPerChannelBot_DoesNotOccupyTheSharedSlot()
    {
        (AuthService service, AuthDbContext db) = Build();
        SeedSharedBot(
            db,
            StrangerUserId,
            "a_channels_own_bot",
            identityType: AuthEnums.BotIdentityType.Custom
        );

        Result result = await service.EnsureSharedBotSlotAvailableAsync(
            User(IncumbentUserId, "nomz_bot")
        );

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// The other half of the 2026-09-04 incident: the dashboard's channel Integrations page had only the
    /// SHARED flow to call, so a channel connecting "its bot" replaced the platform bot every other channel
    /// speaks through. A channel-scoped device poll now exists, and it must establish a CUSTOM bot for that
    /// broadcaster while leaving the shared slot exactly as it found it.
    /// </summary>
    [Fact]
    public async Task TheChannelScopedPoll_DoesNotDisturbTheSharedBot()
    {
        (AuthService service, AuthDbContext db) = Build();
        SeedSharedBot(db, IncumbentUserId, "nomz_bot");

        // Not authorized by Twitch (no device approval in this harness), so the poll returns a
        // non-authorized status rather than establishing anything — the point is that it does NOT route
        // through the shared-bot path on its way there.
        Result<DeviceBotPollDto> result = await service.PollChannelBotDeviceLoginAsync(
            Guid.NewGuid(),
            "device-code"
        );

        result.IsSuccess.Should().BeTrue();

        BotAccount shared = db.BotAccounts.Single(b =>
            b.IdentityType == AuthEnums.BotIdentityType.Shared
        );
        shared.BotUserId.Should().Be(IncumbentUserId);
        shared
            .BotUsername.Should()
            .Be("nomz_bot", "the shared bot must be untouched by a channel poll");
        shared.IsActive.Should().BeTrue();
    }

    private static TwitchUserInfo User(string id, string login) =>
        new(id, login, login, ProfileImageUrl: null, BroadcasterType: "");

    private static void SeedSharedBot(
        AuthDbContext db,
        string botUserId,
        string botUsername,
        bool isActive = true,
        string identityType = AuthEnums.BotIdentityType.Shared
    )
    {
        db.BotAccounts.Add(
            new BotAccount
            {
                Id = Guid.NewGuid(),
                IdentityType = identityType,
                Platform = AuthEnums.Platform.Twitch,
                BotUserId = botUserId,
                BotUsername = botUsername,
                IsActive = isActive,
                DeletedAt = isActive ? null : DateTime.UtcNow,
            }
        );
        db.SaveChanges();
    }

    private static (AuthService Service, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Twitch:ClientId"] = "cid" })
            .Build();

        // Default the device poll to Pending: NSubstitute otherwise returns a null Task result and the poll
        // NREs before it can demonstrate anything about which bot path it takes.
        ITwitchDeviceCodeService deviceCode = Substitute.For<ITwitchDeviceCodeService>();
        deviceCode
            .PollOnceAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new DevicePollOutcome(DevicePollStatus.Pending));

        AuthService service = new(
            db,
            Substitute.For<ITwitchAuthService>(),
            deviceCode,
            Substitute.For<IIntegrationTokenVault>(),
            Substitute.For<ISessionService>(),
            Substitute.For<ISessionRevocationService>(),
            new RecordingEventBus(),
            AuthTestBuilder.CredentialsProvider(db, protector, config),
            Substitute.For<IHttpClientFactory>(),
            config,
            new(DeploymentMode.SelfHostLite),
            TimeProvider.System,
            new(),
            Substitute.For<IPlatformOwnerPrincipalMinter>(),
            NullLogger<AuthService>.Instance
        );

        return (service, db);
    }
}
