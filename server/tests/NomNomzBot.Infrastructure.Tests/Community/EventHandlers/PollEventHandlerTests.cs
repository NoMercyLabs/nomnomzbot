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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Infrastructure.Community.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Community.EventHandlers;

/// <summary>Proves <c>{poll.duration}</c> renders through the same shared <c>HumanDuration</c> helper as the
/// ad-break/timeout handlers — this was the one call site of that change with zero prior test coverage.</summary>
public sealed class PollEventHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d201");

    private static (IServiceScopeFactory Scopes, IEventResponseExecutor Executor) Harness()
    {
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(AuthTestBuilder.NewContext())
            .AddSingleton(executor)
            .BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), executor);
    }

    [Fact]
    public async Task A_poll_begin_dispatches_channel_poll_begin_with_a_human_readable_duration()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        PollBeganHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<PollBeganHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                PollId = "poll-1",
                Title = "Best game?",
                Choices = [new("c1", "Deep Rock", 0, 0), new("c2", "Hollow Knight", 0, 0)],
                DurationSeconds = 120,
                EndsAt = DateTimeOffset.UtcNow.AddMinutes(2),
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "channel.poll.begin",
                Channel.ToString(),
                null,
                Arg.Is<Dictionary<string, string>>(v =>
                    v["poll.duration"] == "2 minutes"
                    && v["poll.title"] == "Best game?"
                    && v["poll.choices"] == "Deep Rock, Hollow Knight"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
