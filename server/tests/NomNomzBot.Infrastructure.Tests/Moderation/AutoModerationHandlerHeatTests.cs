// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Auto-mod punishments must travel the same road every other punishment travels.
///
/// <para>They did not. The handler called <see cref="ITwitchModerationApi"/> straight, which lands the
/// timeout on Twitch and tells NomNomzBot nothing: <see cref="IModerationService"/> is what emits
/// <c>UserTimedOut</c>/<c>UserBanned</c>, and those events are the only thing
/// <c>ModerationProjectionService</c> turns into heat. So every offence auto-mod caught cost the
/// offender zero heat, and a repeat offender kept arriving at the escalation ladder as a first-timer —
/// the one population the ladder exists for.</para>
///
/// <para>Each test below asserts BOTH halves: the service was used, and Helix was not reached behind
/// its back. Asserting only the first would still pass if someone called both.</para>
/// </summary>
public sealed class AutoModerationHandlerHeatTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-0000000000a1");
    private static readonly Guid OwnerUserId = Guid.Parse("0192d000-0000-7000-8000-0000000000a2");
    private const string OffenderTwitchId = "900777";

    private static async Task<(
        AutoModerationHandler Handler,
        IModerationService Moderation,
        ITwitchModerationApi Twitch
    )> BuildAsync(string action, int? durationSeconds = null, bool withOwner = true)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "700777",
                OwnerUserId = withOwner ? OwnerUserId : Guid.Empty,
                Name = "c",
                NameNormalized = "c",
            }
        );
        db.Records.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = OwnerUserId.ToString(),
                RecordType = "moderation_rule",
                Data = $$"""
                {
                  "Name": "no links",
                  "Type": "links",
                  "Action": "{{action}}",
                  "IsEnabled": true,
                  "DurationSeconds": {{durationSeconds?.ToString() ?? "null"}},
                  "Reason": "links are not allowed here"
                }
                """,
            }
        );
        await db.SaveChangesAsync();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, null)));
        moderation
            .BanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, null)));

        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(moderation);
        services.AddSingleton(twitch);
        ServiceProvider provider = services.BuildServiceProvider();

        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        AutoModerationHandler handler = new(
            scopeFactory,
            new AutoModRuleCache(
                scopeFactory,
                TimeProvider.System,
                NullLogger<AutoModRuleCache>.Instance
            ),
            NullLogger<AutoModerationHandler>.Instance
        );

        return (handler, moderation, twitch);
    }

    private static ChatMessageReceivedEvent LinkMessage() =>
        new()
        {
            MessageId = "m-1",
            BroadcasterId = Channel,
            TwitchBroadcasterId = "700777",
            UserId = OffenderTwitchId,
            UserDisplayName = "Offender",
            UserLogin = "offender",
            Message = "come see https://free-nitro.example",
            Fragments = [],
            IsBroadcaster = false,
            IsModerator = false,
            IsVip = false,
            IsSubscriber = false,
            Badges = [],
        };

    [Fact]
    public async Task ATimeoutRuleFeedsHeatByGoingThroughTheModerationService()
    {
        (
            AutoModerationHandler handler,
            IModerationService moderation,
            ITwitchModerationApi twitch
        ) = await BuildAsync("timeout", durationSeconds: 300);

        await handler.HandleAsync(LinkMessage(), CancellationToken.None);

        await moderation
            .Received(1)
            .TimeoutAsync(
                Channel.ToString(),
                OwnerUserId,
                OffenderTwitchId,
                300,
                "links are not allowed here",
                null,
                Arg.Any<CancellationToken>()
            );

        // The half that was broken: reaching Helix directly is what skipped the heat projection.
        await twitch
            .DidNotReceive()
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ABanRuleFeedsHeatByGoingThroughTheModerationService()
    {
        (
            AutoModerationHandler handler,
            IModerationService moderation,
            ITwitchModerationApi twitch
        ) = await BuildAsync("ban");

        await handler.HandleAsync(LinkMessage(), CancellationToken.None);

        await moderation
            .Received(1)
            .BanAsync(
                Channel.ToString(),
                OwnerUserId,
                OffenderTwitchId,
                "links are not allowed here",
                null,
                Arg.Any<CancellationToken>()
            );

        await twitch
            .DidNotReceive()
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ATimeoutRuleWithNoDurationUsesTheSixtySecondDefault()
    {
        (AutoModerationHandler handler, IModerationService moderation, _) = await BuildAsync(
            "timeout"
        );

        await handler.HandleAsync(LinkMessage(), CancellationToken.None);

        await moderation
            .Received(1)
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                60,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeletingAMessageStillGoesStraightToHelix()
    {
        // A deletion is not an action against the account, so it carries no heat and needs no ledger
        // entry. Routing it through the moderation service would invent an offence that never happened.
        (
            AutoModerationHandler handler,
            IModerationService moderation,
            ITwitchModerationApi twitch
        ) = await BuildAsync("delete");

        await handler.HandleAsync(LinkMessage(), CancellationToken.None);

        await twitch
            .Received(1)
            .DeleteChatMessageAsync(Channel, "m-1", Arg.Any<CancellationToken>());

        await moderation
            .DidNotReceive()
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AChannelWithNoResolvableOwnerActionsNobody()
    {
        // Without an owner there is no token to sign the action with. Falling back to the direct Helix
        // call would "work" and silently reopen the hole this class exists to close.
        (
            AutoModerationHandler handler,
            IModerationService moderation,
            ITwitchModerationApi twitch
        ) = await BuildAsync("timeout", withOwner: false);

        await handler.HandleAsync(LinkMessage(), CancellationToken.None);

        await moderation
            .DidNotReceive()
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await twitch
            .DidNotReceive()
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
