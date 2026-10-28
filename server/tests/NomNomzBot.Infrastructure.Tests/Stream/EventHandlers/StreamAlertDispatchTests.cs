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
using NomNomzBot.Infrastructure.Stream.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.EventHandlers;

/// <summary>
/// Proves the new stream-plane trigger sources dispatch their configured event responses end-to-end from the
/// published domain event: <c>channel.ad_break.begin</c> fires with the resolved <c>{ad.*}</c> variables, and
/// <c>channel.raid.out</c> fires with the raid TARGET as <c>{user}</c> plus the viewer count — through the ONE
/// shared <see cref="IEventResponseExecutor"/> path every trigger source uses.
/// </summary>
public sealed class StreamAlertDispatchTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d101");

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
    public async Task An_ad_break_event_dispatches_the_response_with_the_ad_variables()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        AdBreakBeganAlertHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<AdBreakBeganAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                DurationSeconds = 180,
                IsAutomatic = false,
                StartedAt = DateTimeOffset.UtcNow,
                RequesterUserId = "555",
                RequesterDisplayName = "StoneyMod",
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "channel.ad_break.begin",
                "555",
                "StoneyMod",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["ad.duration"] == "3 minutes"
                    && v["ad.automatic"] == "false"
                    && v["user"] == "StoneyMod"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_automatic_ad_break_dispatches_with_an_empty_requester()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        AdBreakBeganAlertHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<AdBreakBeganAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                DurationSeconds = 60,
                IsAutomatic = true,
                StartedAt = DateTimeOffset.UtcNow,
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "channel.ad_break.begin",
                null,
                null,
                Arg.Is<Dictionary<string, string>>(v =>
                    v["ad.duration"] == "1 minute" // singular — the plural "3 minutes" case above must not be the only one pinned
                    && v["ad.automatic"] == "true"
                    && v["user"] == ""
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_ad_break_with_a_non_round_duration_falls_back_to_seconds()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        AdBreakBeganAlertHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<AdBreakBeganAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                DurationSeconds = 45,
                IsAutomatic = false,
                StartedAt = DateTimeOffset.UtcNow,
                RequesterUserId = "555",
                RequesterDisplayName = "StoneyMod",
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "channel.ad_break.begin",
                "555",
                "StoneyMod",
                Arg.Is<Dictionary<string, string>>(v => v["ad.duration"] == "45 seconds"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_outgoing_raid_dispatches_channel_raid_out_naming_the_target()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        OutgoingRaidAlertHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<OutgoingRaidAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                ToUserId = "141981764",
                ToDisplayName = "TwitchDev",
                ToLogin = "twitchdev",
                ViewerCount = 42,
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "channel.raid.out",
                "141981764",
                "TwitchDev",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["user"] == "TwitchDev"
                    && v["user.name"] == "twitchdev"
                    && v["viewers"] == "42"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
