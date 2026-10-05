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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// A gifted-sub recipient is announced as a gift ("X was gifted a sub by Y"), never as a self-subscriber. Twitch
/// fires <c>channel.subscribe</c> (<c>is_gift = true</c>) once per recipient, so that event must not trigger the
/// "just subscribed" response; the recipient/gifter pairing arrives as <see cref="GiftSubscriptionReceivedEvent"/>.
/// Every recipient of a gift bomb is named individually as well as the batch itself.
/// </summary>
public sealed class GiftSubscriptionReceivedEventHandlerTests
{
    private const string ReceivedKey = "channel.subscription.gift.received";
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d503");

    private sealed record Harness(
        AuthDbContext Db,
        GiftSubscriptionReceivedEventHandler Received,
        NewSubscriptionEventHandler NewSub,
        GiftSubscriptionEventHandler Gift,
        IEventResponseExecutor Executor
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(executor)
            .BuildServiceProvider();
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();

        return new(
            db,
            new(
                scopes,
                Substitute.For<IPipelineEngine>(),
                NullLogger<GiftSubscriptionReceivedEventHandler>.Instance
            ),
            new(
                scopes,
                Substitute.For<IPipelineEngine>(),
                NullLogger<NewSubscriptionEventHandler>.Instance
            ),
            new(
                scopes,
                Substitute.For<IPipelineEngine>(),
                NullLogger<GiftSubscriptionEventHandler>.Instance
            ),
            executor
        );
    }

    private static GiftSubscriptionReceivedEvent Received(
        bool anonymous = false,
        string? communityGiftId = null
    ) =>
        new()
        {
            BroadcasterId = Channel,
            RecipientUserId = "777",
            RecipientDisplayName = "Lucky_Viewer",
            GifterUserId = anonymous ? string.Empty : "555",
            GifterDisplayName = anonymous ? string.Empty : "Generous_Gifter",
            IsAnonymous = anonymous,
            Tier = "1000",
            CommunityGiftId = communityGiftId,
        };

    private static Dictionary<string, string> CapturedVariables(IEventResponseExecutor executor)
    {
        ICall call = executor.ReceivedCalls().Single();
        return (Dictionary<string, string>)call.GetArguments()[4]!;
    }

    private static string Render(string template, Dictionary<string, string> variables) =>
        new TemplateResolver(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        ).Resolve(template, variables);

    [Fact]
    public async Task A_standalone_gift_fires_the_gift_received_response_naming_recipient_and_gifter()
    {
        Harness h = Build();
        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();

        await h.Received.HandleAsync(Received());

        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                ReceivedKey,
                "777",
                "Lucky_Viewer",
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
        Dictionary<string, string> variables = CapturedVariables(h.Executor);
        variables["user"].Should().Be("Lucky_Viewer");
        variables["gifter"].Should().Be("Generous_Gifter");
        variables["tier"].Should().Be("1");
        variables["anonymous"].Should().Be("false");

        // The text the chat/TTS actually gets: the default tone's first line rendered with those variables.
        string template = EventResponseToneCatalog.FirstInformative(ReceivedKey)!;
        Render(template, variables).Should().Be("@Lucky_Viewer been gifted a tier 1 subscription!");
    }

    [Fact]
    public async Task An_anonymous_gift_names_the_recipient_and_an_anonymous_gifter()
    {
        Harness h = Build();
        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();

        await h.Received.HandleAsync(Received(anonymous: true));

        // The anonymous wording is its own response key — a template, not a name stuffed into a sentence.
        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                ReceivedKey + ".anonymous",
                "777",
                "Lucky_Viewer",
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
        Dictionary<string, string> variables = CapturedVariables(h.Executor);
        variables["anonymous"].Should().Be("true");
        variables["gifter.id"].Should().BeEmpty();
        string template = EventResponseToneCatalog.FirstInformative(ReceivedKey + ".anonymous")!;
        Render(template, variables).Should().Be("An anonymous gifter gave a sub to Lucky_Viewer!");
    }

    [Fact]
    public async Task An_anonymous_gift_bomb_is_announced_once_to_the_community_without_a_blank_name()
    {
        Harness h = Build();
        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();
        GiftSubscriptionEvent bomb = new()
        {
            BroadcasterId = Channel,
            GifterUserId = string.Empty,
            GifterDisplayName = string.Empty,
            Tier = "1000",
            GiftCount = 5,
            IsAnonymous = true,
            Recipients = [],
        };

        await h.Gift.HandleAsync(bomb);

        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "channel.subscription.gift.anonymous",
                null,
                "Anonymous",
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
        string template = EventResponseToneCatalog.FirstInformative(
            "channel.subscription.gift.anonymous"
        )!;
        Render(template, CapturedVariables(h.Executor))
            .Should()
            .Be("An anonymous gifter gave 5 sub(s) to the community!");
    }

    [Fact]
    public async Task A_named_gift_bomb_still_uses_the_regular_gift_response()
    {
        Harness h = Build();
        GiftSubscriptionEvent bomb = new()
        {
            BroadcasterId = Channel,
            GifterUserId = "555",
            GifterDisplayName = "Generous_Gifter",
            Tier = "1000",
            GiftCount = 5,
            IsAnonymous = false,
            Recipients = [],
        };

        await h.Gift.HandleAsync(bomb);

        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "channel.subscription.gift",
                "555",
                "Generous_Gifter",
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Every_recipient_inside_a_gift_bomb_is_named_individually()
    {
        Harness h = Build();

        await h.Received.HandleAsync(Received(communityGiftId: "batch-42"));

        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                ReceivedKey,
                "777",
                "Lucky_Viewer",
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
        CapturedVariables(h.Executor)["gifter"].Should().Be("Generous_Gifter");
    }

    [Fact]
    public async Task The_recipient_side_channel_subscribe_no_longer_fires_the_just_subscribed_response()
    {
        Harness h = Build();
        NewSubscriptionEvent giftedSub = new()
        {
            BroadcasterId = Channel,
            UserId = "777",
            UserDisplayName = "Lucky_Viewer",
            Tier = "1000",
            IsGift = true,
        };

        await h.NewSub.HandleAsync(giftedSub);

        h.Executor.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task The_seeded_gift_received_default_is_on_and_only_uses_variables_the_event_fills()
    {
        Harness h = Build();
        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();

        PlatformEventResponseDefault row = await h.Db.PlatformEventResponseDefaults.SingleAsync(d =>
            d.EventType == ReceivedKey
        );

        row.IsEnabled.Should().BeTrue();
        EventResponsePresetCatalog
            .Presets.Single(p => p.EventType == ReceivedKey)
            .Variables.Should()
            .Contain(["user", "user.id", "gifter", "gifter.id", "tier", "anonymous"]);
    }

    [Fact]
    public async Task A_fresh_seed_makes_the_gift_recipient_default_speak_through_tts()
    {
        Harness h = Build();
        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();

        List<PlatformEventResponseDefault> rows = await h
            .Db.PlatformEventResponseDefaults.AsNoTracking()
            .ToListAsync();

        rows.Single(d => d.EventType == ReceivedKey).SpeakWithTts.Should().BeTrue();
        rows.Where(d => d.SpeakWithTts)
            .Select(d => d.EventType)
            .Should()
            .BeEquivalentTo([ReceivedKey]);
    }

    [Fact]
    public async Task The_seeder_turns_tts_on_once_for_an_untouched_gift_recipient_row_and_leaves_an_admin_touched_one_alone()
    {
        Harness h = Build();
        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();
        PlatformEventResponseDefault row = await h.Db.PlatformEventResponseDefaults.SingleAsync(d =>
            d.EventType == ReceivedKey
        );
        row.SpeakWithTts = false;
        await h.Db.SaveChangesAsync();

        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();
        row.SpeakWithTts.Should().BeTrue();

        row.SpeakWithTts = false;
        row.UpdatedByUserId = Guid.CreateVersion7();
        await h.Db.SaveChangesAsync();

        await new PlatformEventResponseDefaultsSeeder(h.Db).SeedAsync();
        row.SpeakWithTts.Should().BeFalse();
    }
}
