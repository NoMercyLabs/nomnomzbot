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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the moderation-notice trigger sources dispatch end-to-end from the published domain events:
/// <c>channel.ban</c> fires for BOTH a permanent ban ({duration} = "permanent") and a timeout ({duration} =
/// seconds), and <c>channel.unban</c> fires with the moderator's display name — with the id as the honest
/// fallback when a non-Twitch ingest carries no display name.
/// </summary>
public sealed class ModerationAlertHandlersTests
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
    public async Task A_permanent_ban_dispatches_channel_ban_with_duration_permanent()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        UserBannedAlertHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<UserBannedAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new UserBannedEvent
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                TargetUserId = "1234",
                TargetDisplayName = "Troll",
                ModeratorUserId = "mod-1",
                ModeratorDisplayName = "Mod_One",
                Reason = "rule violation",
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "channel.ban",
                "1234",
                "Troll",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["user"] == "Troll"
                    && v["moderator"] == "Mod_One"
                    && v["reason"] == "rule violation"
                    && v["duration"] == "permanent"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_timeout_dispatches_channel_ban_with_the_timeout_seconds()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        UserTimedOutAlertHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<UserTimedOutAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new UserTimedOutEvent
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                TargetUserId = "1234",
                TargetDisplayName = "Troll",
                ModeratorUserId = "mod-1",
                DurationSeconds = 600,
                Reason = null,
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "channel.ban",
                "1234",
                "Troll",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["duration"] == "600"
                    // No display name on this ingest → the moderator id is the honest fallback.
                    && v["moderator"] == "mod-1"
                    && v["reason"] == ""
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unban_dispatches_channel_unban_with_the_moderator()
    {
        (IServiceScopeFactory scopes, IEventResponseExecutor executor) = Harness();
        UserUnbannedAlertHandler handler = new(
            scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<UserUnbannedAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new UserUnbannedEvent
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                TargetUserId = "1234",
                TargetDisplayName = "Reformed",
                ModeratorUserId = "mod-1",
                ModeratorDisplayName = "Mod_One",
            }
        );

        await executor
            .Received(1)
            .ExecuteAsync(
                Channel,
                "channel.unban",
                "1234",
                "Reformed",
                Arg.Is<Dictionary<string, string>>(v =>
                    v["user"] == "Reformed" && v["moderator"] == "Mod_One"
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
