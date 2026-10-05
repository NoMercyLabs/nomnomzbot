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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Eventing;
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
            new()
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
            new()
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
                    v["duration"] == "10 minutes"
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
            new()
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

    private sealed record LiveHarness(
        IServiceScopeFactory Scopes,
        AuthDbContext Db,
        IChatProvider Chat
    );

    private static async Task<LiveHarness> LiveAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "live-ext",
                Name = "livechannel",
                NameNormalized = "livechannel",
                Status = AuthEnums.ChannelStatus.Active,
                Personality = PersonalityTone.Informative,
            }
        );
        await db.SaveChangesAsync();
        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();
        await new EventResponseDefaultsSeeder(db).SeedAsync();

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => call.ArgAt<string>(0));
        IChatProvider chat = Substitute.For<IChatProvider>();
        EventResponseExecutor executor = new(
            db,
            Substitute.For<IPipelineEngine>(),
            templates,
            chat,
            Substitute.For<IEventResponseOverlayNotifier>(),
            Substitute.For<ITtsDispatchService>(),
            NullLogger<EventResponseExecutor>.Instance
        );
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton<IEventResponseExecutor>(executor)
            .BuildServiceProvider();
        return new(provider.GetRequiredService<IServiceScopeFactory>(), db, chat);
    }

    private static List<string> Sent(LiveHarness h) =>
        [
            .. h
                .Chat.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IChatProvider.SendMessageAsync))
                .Where(c => (Guid)c.GetArguments()[0]! == Channel)
                .Select(c => (string)c.GetArguments()[1]!),
        ];

    [Fact]
    public async Task A_moderator_grant_posts_one_legacy_line_and_logs_the_event()
    {
        LiveHarness h = await LiveAsync();
        ModeratorAddedAlertHandler handler = new(
            h.Scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<ModeratorAddedAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                UserId = "555",
                UserDisplayName = "NewMod",
                UserLogin = "newmod",
            }
        );

        Sent(h).Should().HaveCount(1);
        EventResponseToneCatalog
            .Get(PersonalityTone.Informative, "channel.moderator.add")
            .Should()
            .Contain(Sent(h).Single());
        ChannelEvent logged = await h.Db.ChannelEvents.SingleAsync();
        logged.Type.Should().Be("channel.moderator.add");
        logged.ChannelId.Should().Be(Channel);
        logged.Data.Should().Contain("NewMod").And.Contain("555");
    }

    [Fact]
    public async Task A_moderator_revoke_posts_one_legacy_line_and_logs_the_event()
    {
        LiveHarness h = await LiveAsync();
        ModeratorRemovedAlertHandler handler = new(
            h.Scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<ModeratorRemovedAlertHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                UserId = "555",
                UserDisplayName = "OldMod",
                UserLogin = "oldmod",
            }
        );

        Sent(h).Should().HaveCount(1);
        EventResponseToneCatalog
            .Get(PersonalityTone.Informative, "channel.moderator.remove")
            .Should()
            .Contain(Sent(h).Single());
        ChannelEvent logged = await h.Db.ChannelEvents.SingleAsync();
        logged.Type.Should().Be("channel.moderator.remove");
        logged.Data.Should().Contain("OldMod");
    }

    [Fact]
    public async Task A_ban_a_timeout_and_an_unban_each_post_one_line()
    {
        LiveHarness h = await LiveAsync();

        await new UserBannedAlertHandler(
            h.Scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<UserBannedAlertHandler>.Instance
        ).HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                TargetUserId = "1",
                TargetDisplayName = "Troll",
                ModeratorUserId = "m",
                Reason = "spam",
            }
        );
        Sent(h).Should().HaveCount(1);

        await new UserTimedOutAlertHandler(
            h.Scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<UserTimedOutAlertHandler>.Instance
        ).HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                TargetUserId = "2",
                TargetDisplayName = "Heated",
                ModeratorUserId = "m",
                DurationSeconds = 600,
            }
        );
        Sent(h).Should().HaveCount(2);

        await new UserUnbannedAlertHandler(
            h.Scopes,
            Substitute.For<IPipelineEngine>(),
            NullLogger<UserUnbannedAlertHandler>.Instance
        ).HandleAsync(
            new()
            {
                BroadcasterId = Channel,
                OccurredAt = DateTimeOffset.UtcNow,
                TargetUserId = "1",
                TargetDisplayName = "Troll",
                ModeratorUserId = "m",
            }
        );
        Sent(h).Should().HaveCount(3);
        EventResponseToneCatalog
            .Get(PersonalityTone.Informative, "channel.ban")
            .Should()
            .Contain(Sent(h)[0])
            .And.Contain(Sent(h)[1]);
        EventResponseToneCatalog
            .Get(PersonalityTone.Informative, "channel.unban")
            .Should()
            .Contain(Sent(h)[2]);
    }
}
