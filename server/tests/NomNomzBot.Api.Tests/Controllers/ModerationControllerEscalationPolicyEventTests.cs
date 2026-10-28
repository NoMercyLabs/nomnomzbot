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

using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves saving the escalation policy tells the channel's other open dashboards to refetch it, and that a
/// refused save tells them nothing.
/// </summary>
public sealed class ModerationControllerEscalationPolicyEventTests
{
    private static ModerationController Build(
        IModerationEscalationService escalation,
        IEventBus eventBus
    ) =>
        new(
            Substitute.For<IModerationService>(),
            Substitute.For<IOperatorNetworkBanService>(),
            Substitute.For<IViewerReportService>(),
            Substitute.For<IModerationQueueService>(),
            Substitute.For<ISharedBanService>(),
            Substitute.For<INetworkNukeService>(),
            escalation,
            Substitute.For<ICurrentUserService>(),
            ApiTestDbContext.New(),
            TimeProvider.System,
            Substitute.For<ITwitchModerationApi>(),
            Substitute.For<ITemplateHelperValidator>(),
            Substitute.For<IModerationHistoryService>(),
            Substitute.For<IMassBanConsentService>(),
            [],
            Substitute.For<IShoutoutSuggestionService>(),
            eventBus
        );

    private static UpsertEscalationPolicyRequest Request() => new(true, [], 24, false);

    [Fact]
    public async Task A_saved_policy_tells_the_channels_open_dashboards_to_refetch_it()
    {
        Guid channelId = Guid.CreateVersion7();
        IModerationEscalationService escalation = Substitute.For<IModerationEscalationService>();
        escalation
            .UpsertPolicyAsync(
                channelId,
                Arg.Any<UpsertEscalationPolicyRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<ModerationEscalationPolicyDto>(null!));
        IEventBus eventBus = Substitute.For<IEventBus>();

        await Build(escalation, eventBus)
            .UpsertEscalationPolicy(channelId.ToString(), Request(), CancellationToken.None);

        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<ChannelConfigChangedEvent>(e =>
                    e.BroadcasterId == channelId
                    && e.Domain == "escalation"
                    && e.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_refused_save_publishes_nothing()
    {
        Guid channelId = Guid.CreateVersion7();
        IModerationEscalationService escalation = Substitute.For<IModerationEscalationService>();
        escalation
            .UpsertPolicyAsync(
                channelId,
                Arg.Any<UpsertEscalationPolicyRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<ModerationEscalationPolicyDto>(
                    "Steps must ascend.",
                    "VALIDATION_FAILED"
                )
            );
        IEventBus eventBus = Substitute.For<IEventBus>();

        await Build(escalation, eventBus)
            .UpsertEscalationPolicy(channelId.ToString(), Request(), CancellationToken.None);

        await eventBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<ChannelConfigChangedEvent>(), Arg.Any<CancellationToken>());
    }
}
