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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// The cheer's <c>{message}</c> variable feeds chat templates and pipelines that speak it, so a viewer's cheermote
/// ("Cheer100") must never reach it: the old bot stripped cheermotes before speaking the message.
/// </summary>
public sealed class CheerEventHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d504");

    private static async Task<Dictionary<string, string>> VariablesForAsync(string message)
    {
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(AuthTestBuilder.NewContext())
            .AddSingleton(executor)
            .BuildServiceProvider();
        CheerEventHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IPipelineEngine>(),
            NullLogger<CheerEventHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                UserId = "42",
                UserDisplayName = "Bit_Baron",
                Bits = 100,
                Message = message,
                IsAnonymous = false,
            }
        );

        ICall call = executor.ReceivedCalls().Single();
        return (Dictionary<string, string>)call.GetArguments()[4]!;
    }

    [Fact]
    public async Task The_message_variable_has_no_cheermotes_and_no_double_spaces()
    {
        Dictionary<string, string> variables = await VariablesForAsync(
            "Cheer100 great stream Cheer50 keep going uni1"
        );

        variables["message"].Should().Be("great stream keep going");
        variables["bits"].Should().Be("100");
        variables["user"].Should().Be("Bit_Baron");
    }

    [Fact]
    public async Task A_message_with_only_cheermotes_becomes_empty_so_nothing_is_spoken()
    {
        Dictionary<string, string> variables = await VariablesForAsync("Cheer100 Cheer100");

        variables["message"].Should().BeEmpty();
    }
}
