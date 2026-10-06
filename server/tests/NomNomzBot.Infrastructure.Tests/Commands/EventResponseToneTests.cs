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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.PlatformDefaults;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Tone-aware event-response defaults (commands-pipelines.md §3.8): a row that follows the platform default
/// speaks in its channel's personality tone; the platform admin's text beats the tone; a row with its own text
/// never changes voice. Proves the line that reaches chat, the lines the dashboard is told, what the first
/// edit copies, and what the seeder does to legacy and admin-edited defaults.
/// </summary>
public sealed class EventResponseToneTests
{
    private const string Follow = "channel.follow";
    private const string Cheer = "channel.cheer";
    private const string AdBreak = "channel.ad_break.begin";
    private const string AdBreakEnd = "channel.ad_break.end";
    private const string AdBreakUpcoming = "channel.ad_break.upcoming";
    private static readonly Guid SassyChannel = Guid.Parse("0199f300-0000-7000-8000-00000000c001");
    private static readonly Guid OwnTextChannel = Guid.Parse(
        "0199f300-0000-7000-8000-00000000c002"
    );
    private static readonly Guid Admin = Guid.Parse("0199f300-0000-7000-8000-00000000c003");

    private const string OwnWelcome = "My own welcome, {user}";

    private sealed record Harness(
        AuthDbContext Db,
        EventResponseExecutor Executor,
        EventResponseService Channels,
        EventResponseDefaultsAdminService Platform,
        IChatProvider Chat
    );

    private static async Task<Harness> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.AddRange(
            NewChannel(SassyChannel, "sassychannel", PersonalityTone.Sassy),
            NewChannel(OwnTextChannel, "owntextchannel", PersonalityTone.Sassy)
        );
        await db.SaveChangesAsync();
        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();
        await new EventResponseDefaultsSeeder(db).SeedAsync();

        EventResponse own = await db.EventResponses.SingleAsync(r =>
            r.BroadcasterId == OwnTextChannel && r.EventType == Follow
        );
        own.FollowsPlatformDefault = false;
        own.IsEnabled = true;
        own.ResponseType = "chat_message";
        own.Message = OwnWelcome;
        await db.SaveChangesAsync();

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
        EventResponseService channels = new(
            db,
            new RecordingEventBus(),
            new TemplateHelperValidator()
        );
        EventResponseDefaultsAdminService platform = new(
            db,
            new TemplateHelperValidator(),
            new FakeTimeProvider(new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero))
        );
        return new(db, executor, channels, platform, chat);
    }

    private static Channel NewChannel(Guid id, string name, string personality) =>
        new()
        {
            Id = id,
            OwnerUserId = id,
            Provider = AuthEnums.Platform.Twitch,
            ExternalChannelId = name + "-ext",
            Name = name,
            NameNormalized = name,
            Status = AuthEnums.ChannelStatus.Active,
            Personality = personality,
        };

    private static Task FireFollowAsync(Harness h, Guid channel) =>
        h.Executor.ExecuteAsync(channel, Follow, "u1", "viewer", new() { ["user"] = "viewer" });

    private static List<string> SentTo(Harness h, Guid channel) =>
        [
            .. h
                .Chat.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IChatProvider.SendMessageAsync))
                .Where(c => (Guid)c.GetArguments()[0]! == channel)
                .Select(c => (string)c.GetArguments()[1]!),
        ];

    private static async Task SetChannelToneAsync(Harness h, Guid channel, string tone)
    {
        Channel row = await h.Db.Channels.SingleAsync(c => c.Id == channel);
        row.Personality = tone;
        await h.Db.SaveChangesAsync();
    }

    // ── runtime ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_following_row_in_a_sassy_channel_sends_only_sassy_lines()
    {
        Harness h = await BuildAsync();

        for (int i = 0; i < 12; i++)
            await FireFollowAsync(h, SassyChannel);

        List<string> sent = SentTo(h, SassyChannel);
        sent.Should().HaveCount(12);
        sent.Should()
            .OnlyContain(m =>
                EventResponseToneCatalog.Get(PersonalityTone.Sassy, Follow).Contains(m)
            );
        sent.Distinct()
            .Count()
            .Should()
            .BeGreaterThan(1, "the bot picks among the lines, it does not repeat one");
    }

    [Fact]
    public async Task A_tone_change_changes_the_voice_of_a_following_row_on_the_next_event()
    {
        Harness h = await BuildAsync();
        await SetChannelToneAsync(h, SassyChannel, PersonalityTone.Hype);

        await FireFollowAsync(h, SassyChannel);

        SentTo(h, SassyChannel)
            .Single()
            .Should()
            .BeOneOf(EventResponseToneCatalog.Get(PersonalityTone.Hype, Follow));
    }

    [Fact]
    public async Task Admin_text_beats_the_tone()
    {
        Harness h = await BuildAsync();
        Result<EventResponseDefaultDto> saved = await h.Platform.SetAsync(
            Follow,
            new(true, "Thanks {user}!", ConfirmedChannelsAffected: 1),
            Admin
        );

        await FireFollowAsync(h, SassyChannel);

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        SentTo(h, SassyChannel).Should().Equal("Thanks {user}!");
    }

    [Fact]
    public async Task A_row_with_its_own_text_ignores_the_tone()
    {
        Harness h = await BuildAsync();

        await FireFollowAsync(h, OwnTextChannel);
        await SetChannelToneAsync(h, OwnTextChannel, PersonalityTone.Hype);
        await FireFollowAsync(h, OwnTextChannel);

        SentTo(h, OwnTextChannel).Should().Equal(OwnWelcome, OwnWelcome);
    }

    [Fact]
    public async Task An_enabled_row_of_its_own_with_no_text_speaks_its_channels_tone_while_the_platform_default_is_off()
    {
        Harness h = await BuildAsync();
        EventResponse row = await h.Db.EventResponses.SingleAsync(r =>
            r.BroadcasterId == SassyChannel && r.EventType == AdBreak
        );
        row.FollowsPlatformDefault = false;
        row.IsEnabled = true;
        row.ResponseType = "chat_message";
        row.Message = null;
        await h.Db.SaveChangesAsync();
        (await h.Db.PlatformEventResponseDefaults.SingleAsync(d => d.EventType == AdBreak))
            .IsEnabled.Should()
            .BeFalse("the ad-break default ships off, so only the row's own switch can turn it on");

        await h.Executor.ExecuteAsync(
            SassyChannel,
            AdBreak,
            "u1",
            "viewer",
            new() { ["ad.duration"] = "3 minutes" }
        );

        SentTo(h, SassyChannel)
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOneOf(EventResponseToneCatalog.Get(PersonalityTone.Sassy, AdBreak));
    }

    [Theory]
    [InlineData(AdBreak, "An ad break has started for {ad.duration}. Please stay tuned!")]
    [InlineData(AdBreakEnd, "The ad break has ended. Thanks for your patience!")]
    [InlineData(
        AdBreakUpcoming,
        "Heads up: an ad break is coming {ad.when} ({ad.seconds} seconds long). Subscribers skip ads."
    )]
    public async Task The_informative_ad_break_lines_are_the_old_bots_texts(
        string eventType,
        string expected
    )
    {
        Harness h = await BuildAsync();
        await SetChannelToneAsync(h, SassyChannel, PersonalityTone.Informative);
        EventResponse row = await h.Db.EventResponses.SingleAsync(r =>
            r.BroadcasterId == SassyChannel && r.EventType == eventType
        );
        row.FollowsPlatformDefault = false;
        row.IsEnabled = true;
        row.ResponseType = "chat_message";
        row.Message = null;
        await h.Db.SaveChangesAsync();

        await h.Executor.ExecuteAsync(
            SassyChannel,
            eventType,
            null,
            null,
            new()
            {
                ["ad.duration"] = "3 minutes",
                ["ad.when"] = "in ~3 minutes",
                ["ad.seconds"] = "90",
            }
        );

        SentTo(h, SassyChannel).Should().Equal(expected);
    }

    // ── what the dashboard is told ──────────────────────────────────────────

    [Fact]
    public async Task A_following_row_lists_the_lines_of_its_channels_tone_and_reports_no_text_of_its_own()
    {
        Harness h = await BuildAsync();

        EventResponseDto dto = (
            await h.Channels.GetByEventTypeAsync(SassyChannel.ToString(), Follow)
        ).Value;

        dto.FollowsPlatformDefault.Should().BeTrue();
        dto.ToneLines.Should().Equal(EventResponseToneCatalog.Get(PersonalityTone.Sassy, Follow));
        dto.Message.Should()
            .BeNull("an editor that saves the shown text back must never freeze one tone line");
    }

    [Fact]
    public async Task A_following_row_lists_the_admin_text_alone_when_one_is_set()
    {
        Harness h = await BuildAsync();
        await h.Platform.SetAsync(
            Follow,
            new(true, "Thanks {user}!", ConfirmedChannelsAffected: 1),
            Admin
        );

        EventResponseDto dto = (
            await h.Channels.GetByEventTypeAsync(SassyChannel.ToString(), Follow)
        ).Value;

        dto.ToneLines.Should().Equal("Thanks {user}!");
        dto.Message.Should().Be("Thanks {user}!");
    }

    [Fact]
    public async Task A_row_with_its_own_text_has_no_tone_lines()
    {
        Harness h = await BuildAsync();

        EventResponseDto dto = (
            await h.Channels.GetByEventTypeAsync(OwnTextChannel.ToString(), Follow)
        ).Value;

        dto.FollowsPlatformDefault.Should().BeFalse();
        dto.ToneLines.Should().BeEmpty();
        dto.Message.Should().Be(OwnWelcome);
    }

    [Fact]
    public async Task Emptying_the_text_of_an_own_row_hands_it_the_channels_tone_lines_and_the_tone_change_count()
    {
        Harness h = await BuildAsync();
        PaginationParams page = new() { Page = 1, PageSize = 100 };
        EventResponseListItem before = (
            await h.Channels.ListAsync(OwnTextChannel.ToString(), page)
        ).Value.Items.Single(i => i.EventType == Follow);

        Result<EventResponseDto> saved = await h.Channels.UpsertAsync(
            OwnTextChannel.ToString(),
            Follow,
            new() { Message = "" }
        );

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved
            .Value.ToneLines.Should()
            .Equal(
                EventResponseToneCatalog.Get(PersonalityTone.Sassy, Follow),
                "the save answers with the lines of the channel's own tone"
            );
        EventResponseDto dto = (
            await h.Channels.GetByEventTypeAsync(OwnTextChannel.ToString(), Follow)
        ).Value;
        dto.FollowsPlatformDefault.Should().BeFalse();
        dto.ToneLines.Should().Equal(EventResponseToneCatalog.Get(PersonalityTone.Sassy, Follow));
        dto.Message.Should()
            .BeNullOrEmpty("the row keeps no text, so saving it again never freezes one tone line");
        EventResponseListItem after = (
            await h.Channels.ListAsync(OwnTextChannel.ToString(), page)
        ).Value.Items.Single(i => i.EventType == Follow);
        before.SpeaksInTone.Should().BeFalse("its own text never changes voice");
        after.SpeaksInTone.Should().BeTrue("a personality change now changes what it says");
    }

    [Fact]
    public async Task The_first_edit_of_a_following_row_keeps_speaking_every_line_of_its_tone()
    {
        Harness h = await BuildAsync();

        EventResponseDto saved = (
            await h.Channels.UpsertAsync(
                SassyChannel.ToString(),
                Cheer,
                new() { SpeakWithTts = true }
            )
        ).Value;

        saved.FollowsPlatformDefault.Should().BeFalse();
        saved.ToneLines.Should().Equal(EventResponseToneCatalog.Get(PersonalityTone.Sassy, Cheer));
        saved.Message.Should().BeNull();
        (
            await h
                .Db.EventResponses.AsNoTracking()
                .SingleAsync(r => r.BroadcasterId == SassyChannel && r.EventType == Cheer)
        )
            .Message.Should()
            .BeNull("a tts toggle is no reason to freeze one line of the tone");
    }

    // ── the platform admin's view and the seeder ────────────────────────────

    [Fact]
    public async Task With_no_admin_text_the_admin_view_shows_the_informative_default_and_handing_it_back_keeps_the_row_tone_aware()
    {
        Harness h = await BuildAsync();
        string informative = EventResponseToneCatalog.FirstInformative(Follow)!;

        EventResponseDefaultDto shown = (await h.Platform.ListAsync()).Value.Single(d =>
            d.EventType == Follow
        );
        Result<EventResponseDefaultDto> handedBack = await h.Platform.SetAsync(
            Follow,
            new(true, informative, ConfirmedChannelsAffected: 0),
            Admin
        );

        shown.Message.Should().Be(informative);
        handedBack.IsSuccess.Should().BeTrue(handedBack.ErrorMessage);
        (
            await h
                .Db.PlatformEventResponseDefaults.AsNoTracking()
                .SingleAsync(d => d.EventType == Follow)
        )
            .Message.Should()
            .BeNull(
                "saving the shown default without changing it must not freeze every tone into it"
            );
    }

    [Fact]
    public async Task A_fresh_seed_writes_no_message_and_enables_exactly_the_events_that_ship_on()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();

        List<PlatformEventResponseDefault> rows = await db
            .PlatformEventResponseDefaults.AsNoTracking()
            .ToListAsync();
        rows.Should().OnlyContain(r => r.Message == null);
        rows.Where(r => r.IsEnabled)
            .Select(r => r.EventType)
            .Should()
            .BeEquivalentTo(
                PlatformEventResponseDefaultsSeeder
                    .LegacyMessages.Keys.Concat([
                        "channel.poll.end",
                        "channel.ban",
                        "channel.unban",
                        "channel.moderator.add",
                        "channel.moderator.remove",
                    ])
                    .ToList()
            );
    }

    [Fact]
    public async Task The_seeder_turns_an_untouched_moderation_row_on_and_leaves_an_admin_touched_one_off()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();
        PlatformEventResponseDefault untouched = await db.PlatformEventResponseDefaults.SingleAsync(
            d => d.EventType == "channel.ban"
        );
        PlatformEventResponseDefault turnedOff = await db.PlatformEventResponseDefaults.SingleAsync(
            d => d.EventType == "channel.unban"
        );
        untouched.IsEnabled = false;
        turnedOff.IsEnabled = false;
        turnedOff.UpdatedByUserId = Admin;
        await db.SaveChangesAsync();

        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();

        List<PlatformEventResponseDefault> rows = await db
            .PlatformEventResponseDefaults.AsNoTracking()
            .ToListAsync();
        rows.Single(r => r.EventType == "channel.ban").IsEnabled.Should().BeTrue();
        rows.Single(r => r.EventType == "channel.unban")
            .IsEnabled.Should()
            .BeFalse("an admin turned it off on purpose");
    }

    [Theory]
    [InlineData("channel.ban", "@{user} has been banned from the channel. Reason: {reason}")]
    [InlineData("channel.unban", "@{user} has been unbanned from the channel.")]
    [InlineData("channel.moderator.add", "@{user} has been added as a moderator in the channel.")]
    [InlineData(
        "channel.moderator.remove",
        "@{user} has been removed as a moderator in the channel."
    )]
    public async Task A_moderation_event_speaks_its_legacy_line_in_an_informative_channel(
        string eventType,
        string legacyLine
    )
    {
        Harness h = await BuildAsync();
        await SetChannelToneAsync(h, SassyChannel, PersonalityTone.Informative);

        for (int i = 0; i < 60; i++)
            await h.Executor.ExecuteAsync(
                SassyChannel,
                eventType,
                "u1",
                "viewer",
                new()
                {
                    ["user"] = "viewer",
                    ["user.id"] = "u1",
                    ["moderator"] = "mod",
                    ["reason"] = "spam",
                    ["duration"] = "permanent",
                }
            );

        List<string> sent = SentTo(h, SassyChannel);
        sent.Should().HaveCount(60);
        sent.Should()
            .OnlyContain(m =>
                EventResponseToneCatalog.Get(PersonalityTone.Informative, eventType).Contains(m)
            );
        sent.Should().Contain(legacyLine);
    }

    [Fact]
    public async Task The_seeder_clears_a_legacy_line_and_keeps_an_admin_edited_one()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();
        PlatformEventResponseDefault legacy = await db.PlatformEventResponseDefaults.SingleAsync(
            d => d.EventType == "channel.subscribe"
        );
        PlatformEventResponseDefault edited = await db.PlatformEventResponseDefaults.SingleAsync(
            d => d.EventType == Follow
        );
        legacy.Message = PlatformEventResponseDefaultsSeeder.LegacyMessages["channel.subscribe"];
        edited.Message = "Hello {user}, welcome to the couch!";
        await db.SaveChangesAsync();

        await new PlatformEventResponseDefaultsSeeder(db).SeedAsync();

        List<PlatformEventResponseDefault> rows = await db
            .PlatformEventResponseDefaults.AsNoTracking()
            .ToListAsync();
        rows.Single(r => r.EventType == "channel.subscribe")
            .Message.Should()
            .BeNull("a row still on the legacy seeded line becomes tone-aware");
        rows.Single(r => r.EventType == Follow)
            .Message.Should()
            .Be("Hello {user}, welcome to the couch!", "an admin's edit is never touched");
    }

    public static TheoryData<string> CatalogueEventTypes()
    {
        TheoryData<string> data = [];
        foreach (string eventType in EventResponseToneCatalog.EventTypes)
            data.Add(eventType);
        return data;
    }

    // Owner report 2026-10-06 ("you call this sassy?"): a row saved while the dashboard pre-filled the preset text
    // holds the informative line as literal text, so a sassy channel kept speaking it.
    [Theory]
    [MemberData(nameof(CatalogueEventTypes))]
    public async Task An_own_row_holding_the_informative_default_as_text_still_speaks_its_channels_tone(
        string eventType
    )
    {
        Harness h = await BuildAsync();
        EventResponse row = await h.Db.EventResponses.SingleAsync(r =>
            r.BroadcasterId == SassyChannel && r.EventType == eventType
        );
        row.FollowsPlatformDefault = false;
        row.IsEnabled = true;
        row.ResponseType = "chat_message";
        row.Message = EventResponseToneCatalog.FirstInformative(eventType);
        await h.Db.SaveChangesAsync();

        await h.Executor.ExecuteAsync(SassyChannel, eventType, "u1", "viewer", new());

        SentTo(h, SassyChannel)
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOneOf(EventResponseToneCatalog.Get(PersonalityTone.Sassy, eventType));
    }

    [Theory]
    [MemberData(nameof(CatalogueEventTypes))]
    public async Task Saving_the_informative_default_as_the_text_of_a_chat_row_stores_no_text(
        string eventType
    )
    {
        Harness h = await BuildAsync();

        Result<EventResponseDto> saved = await h.Channels.UpsertAsync(
            SassyChannel.ToString(),
            eventType,
            new()
            {
                IsEnabled = true,
                Message = EventResponseToneCatalog.FirstInformative(eventType),
            }
        );

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        (
            await h
                .Db.EventResponses.AsNoTracking()
                .SingleAsync(r => r.BroadcasterId == SassyChannel && r.EventType == eventType)
        )
            .Message.Should()
            .BeNull("a saved copy of the shown default must not freeze every tone into one line");
        saved
            .Value.ToneLines.Should()
            .Equal(EventResponseToneCatalog.Get(PersonalityTone.Sassy, eventType));
    }

    [Fact]
    public void Every_legacy_seeded_line_is_the_first_informative_line_of_its_event()
    {
        EventResponseToneCatalog
            .EventTypes.Should()
            .Contain(PlatformEventResponseDefaultsSeeder.LegacyMessages.Keys);
        foreach (
            (string eventType, string legacy) in PlatformEventResponseDefaultsSeeder.LegacyMessages
        )
        {
            // The gift recipient line became the old bot's sentence; the seeded text stays listed only so an untouched row is cleared.
            if (eventType == "channel.subscription.gift.received")
                continue;

            EventResponseToneCatalog
                .FirstInformative(eventType)
                .Should()
                .Be(legacy, $"the {eventType} catalogue must keep the line channels already saw");
        }
    }
}
