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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves saving the spam-defense policy tells the channel's other open dashboards to refetch it, and that a
/// refused save tells them nothing.
/// </summary>
public sealed class SpamDefensePolicyEventTests
{
    private readonly ISpamDefenseService _spamDefense = Substitute.For<ISpamDefenseService>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly Guid _channel = Guid.CreateVersion7();

    private SpamDefenseController Build() =>
        new(
            _spamDefense,
            Substitute.For<ICurrentUserService>(),
            Substitute.For<ILockdownService>(),
            _eventBus
        );

    [Fact]
    public async Task A_saved_policy_tells_the_channels_open_dashboards_to_refetch_it()
    {
        _spamDefense
            .UpdateSettingsAsync(
                _channel,
                Arg.Any<SpamDefenseSettings>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new SpamDefenseSettings()));

        await Build()
            .UpdatePolicy(_channel.ToString(), new SpamDefenseSettings(), CancellationToken.None);

        await _eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<ChannelConfigChangedEvent>(e =>
                    e.BroadcasterId == _channel
                    && e.Domain == "spam-defense"
                    && e.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_refused_save_publishes_nothing()
    {
        _spamDefense
            .UpdateSettingsAsync(
                _channel,
                Arg.Any<SpamDefenseSettings>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<SpamDefenseSettings>("refused", "VALIDATION_FAILED"));

        await Build()
            .UpdatePolicy(_channel.ToString(), new SpamDefenseSettings(), CancellationToken.None);

        await _eventBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<ChannelConfigChangedEvent>(), Arg.Any<CancellationToken>());
    }
}
