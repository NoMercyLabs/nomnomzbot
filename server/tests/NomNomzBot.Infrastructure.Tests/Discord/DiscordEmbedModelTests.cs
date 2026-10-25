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
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Discord.ValueObjects;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Discord.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the embed's author line, title link, footer icon, timestamp flag and link button: they save, they
/// load back, they render their placeholders on preview and on a real dispatch, an unknown placeholder is
/// refused at save, and a rule saved before these fields existed still loads and posts as it did.
/// </summary>
public sealed class DiscordEmbedModelTests
{
    private static readonly DateTimeOffset WentLive = new(2026, 6, 22, 20, 0, 0, TimeSpan.Zero);

    // The shape a rule saved before the author, link, icon, time and button fields existed.
    private const string OldRowJson = """
        {"Title":"Live now","Description":"Come watch","Color":"#9146FF","ThumbnailUrl":null,
         "ImageUrl":null,"FooterText":"foot","Fields":[{"Name":"Game","Value":"Chat","Inline":true}]}
        """;

    private static DiscordEmbedConfig FullEmbed() =>
        new(
            "{title}",
            null,
            "#9146FF",
            null,
            "{stream.thumbnail}",
            "NomNomzBot",
            [new("Game", "{game}", true)],
            TitleUrl: "{channel.url}",
            AuthorName: "{channel.name} is now live on Twitch!",
            AuthorIconUrl: "{channel.avatar}",
            AuthorUrl: "{channel.url}",
            FooterIconUrl: "{channel.avatar}",
            ShowTimestamp: true,
            ButtonLabel: "Watch Stream",
            ButtonUrl: "{channel.url}"
        );

    private static DiscordNotificationConfigService NewService(DiscordTestDbContext db) =>
        new(
            db,
            new DiscordTestUnitOfWork(db),
            DiscordTemplateTestSupport.CreateResolver(),
            DiscordTemplateTestSupport.CreateValidator(),
            Substitute.For<IEventBus>()
        );

    private static DiscordGoLiveNotificationHandler GoLive(
        IDiscordNotificationDispatcher dispatcher
    ) =>
        new(
            dispatcher,
            new OutboundSanctionAccessor(),
            NullLogger<DiscordGoLiveNotificationHandler>.Instance
        );

    [Fact]
    public void ARowSavedBeforeTheNewFields_StillLoads_WithTheNewPartsOff()
    {
        DiscordEmbedConfig loaded = JsonConvert.DeserializeObject<DiscordEmbedConfig>(OldRowJson)!;

        loaded.Title.Should().Be("Live now");
        loaded.Fields.Should().ContainSingle().Which.Name.Should().Be("Game");
        loaded.TitleUrl.Should().BeNull();
        loaded.AuthorName.Should().BeNull();
        loaded.FooterIconUrl.Should().BeNull();
        loaded.ShowTimestamp.Should().BeFalse();
        loaded.ButtonLabel.Should().BeNull();
        loaded.ButtonUrl.Should().BeNull();
    }

    [Fact]
    public async Task ARowSavedBeforeTheNewFields_PostsAsBefore()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "go_live",
                "live",
                JsonConvert.DeserializeObject<DiscordEmbedConfig>(OldRowJson)
            );

        await harness.RaiseAsync(GoLive, LiveEvent(harness.BroadcasterId));

        DiscordEmbedDto posted = harness.Gateway.Posts.Single().Message.Embed!;
        posted.Title.Should().Be("Live now");
        posted.FooterText.Should().Be("foot");
        posted.AuthorName.Should().BeNull();
        posted.ShowTimestamp.Should().BeFalse();
        posted.ButtonLabel.Should().BeNull();
    }

    [Fact]
    public async Task TheNewFields_SaveAndLoadBack_ThroughTheDatabase()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connection = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        DiscordEmbedDto dto = DiscordEmbedMapper.ToDto(FullEmbed())!;

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordNotificationConfigDto> created = await NewService(db)
                .CreateConfigAsync(
                    channel,
                    connection,
                    new("go_live", "chan-1", null, "live", dto, null, null, true)
                );
            created.IsSuccess.Should().BeTrue(created.ErrorMessage);
            created.Value.EmbedConfig.Should().BeEquivalentTo(dto);
        }

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordEmbedConfig stored = (
                await db.DiscordNotificationConfigs.SingleAsync()
            ).EmbedConfig!;
            stored.Should().BeEquivalentTo(FullEmbed());
        }
    }

    [Theory]
    [InlineData("AuthorName")]
    [InlineData("AuthorIconUrl")]
    [InlineData("AuthorUrl")]
    [InlineData("TitleUrl")]
    [InlineData("FooterIconUrl")]
    [InlineData("ButtonLabel")]
    [InlineData("ButtonUrl")]
    [InlineData("ThumbnailUrl")]
    [InlineData("ImageUrl")]
    public async Task AnUnknownPlaceholder_InAnyNewField_IsRefusedAtSave(string field)
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connection = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        DiscordEmbedDto embed = field switch
        {
            "AuthorName" => new(null, null, null, null, null, null, null, AuthorName: "{nope.xyz}"),
            "AuthorIconUrl" => new(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                AuthorIconUrl: "{nope.xyz}"
            ),
            "AuthorUrl" => new(null, null, null, null, null, null, null, AuthorUrl: "{nope.xyz}"),
            "TitleUrl" => new(null, null, null, null, null, null, null, TitleUrl: "{nope.xyz}"),
            "FooterIconUrl" => new(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                FooterIconUrl: "{nope.xyz}"
            ),
            "ButtonLabel" => new(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                ButtonLabel: "{nope.xyz}"
            ),
            "ButtonUrl" => new(null, null, null, null, null, null, null, ButtonUrl: "{nope.xyz}"),
            "ThumbnailUrl" => new(null, null, null, "{nope.xyz}", null, null, null),
            _ => new(null, null, null, null, "{nope.xyz}", null, null),
        };

        await using DiscordTestDbContext db = database.NewContext();
        Result<DiscordNotificationConfigDto> result = await NewService(db)
            .CreateConfigAsync(
                channel,
                connection,
                new("go_live", "chan-1", null, "live", embed, null, null, true)
            );

        result.IsFailure.Should().BeTrue($"{field} holds a placeholder the bot does not know");
        (await db.DiscordNotificationConfigs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Preview_RendersThePlaceholders_InTheNewFields()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connection = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        await using DiscordTestDbContext db = database.NewContext();
        DiscordNotificationConfigService service = NewService(db);
        Result<DiscordNotificationConfigDto> created = await service.CreateConfigAsync(
            channel,
            connection,
            new(
                "go_live",
                "chan-1",
                null,
                "live",
                DiscordEmbedMapper.ToDto(FullEmbed()),
                null,
                null,
                true
            )
        );

        Result<DiscordNotificationPreviewDto> preview = await service.PreviewAsync(
            channel,
            created.Value.Id
        );

        DiscordEmbedDto shown = preview.Value.RenderedEmbed!;
        shown.AuthorName.Should().Be("SampleStreamer is now live on Twitch!");
        shown.AuthorUrl.Should().Be("https://www.twitch.tv/SampleStreamer");
        shown.TitleUrl.Should().Be("https://www.twitch.tv/SampleStreamer");
        shown
            .AuthorIconUrl.Should()
            .Be("https://static-cdn.jtvnw.net/jtv_user_pictures/samplestreamer.png");
        shown.ButtonUrl.Should().Be("https://www.twitch.tv/SampleStreamer");
        shown.ButtonLabel.Should().Be("Watch Stream");
        shown.ShowTimestamp.Should().BeTrue();
    }

    [Fact]
    public async Task ARealGoLive_PostsTheAuthorLinkedTitleTimestampAndButton_ForTheChannel()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("go_live", "live", FullEmbed());
        await harness.ShapeChannelAsync(
            AuthEnums.Platform.Twitch,
            "Stoney_Eagle",
            "https://cdn.example/avatar.png"
        );

        await harness.RaiseAsync(GoLive, LiveEvent(harness.BroadcasterId));

        (string ChannelId, DiscordOutboundMessage Message) post = harness.Gateway.Posts.Single();
        DiscordEmbedDto embed = post.Message.Embed!;
        embed.AuthorName.Should().Be("Stoney_Eagle is now live on Twitch!");
        embed.AuthorIconUrl.Should().Be("https://cdn.example/avatar.png");
        embed.AuthorUrl.Should().Be("https://www.twitch.tv/stoney_eagle");
        embed.TitleUrl.Should().Be("https://www.twitch.tv/stoney_eagle");
        embed.Title.Should().Be("blame the lag");
        embed.Fields!.Single().Value.Should().Be("Just Chatting");
        embed.ButtonLabel.Should().Be("Watch Stream");
        embed.ButtonUrl.Should().Be("https://www.twitch.tv/stoney_eagle");
        embed.ShowTimestamp.Should().BeTrue();
        post.Message.SentAt.Should().NotBeNull("the timestamp is the dispatch time");
    }

    [Fact]
    public async Task ALinkThatRendersToNoWebAddress_LeavesThePost_InsteadOfBreakingIt()
    {
        DiscordEmbedConfig embed = new(
            "{title}",
            null,
            null,
            null,
            null,
            null,
            null,
            AuthorName: "{channel.name}",
            AuthorIconUrl: "{channel.avatar}",
            ButtonLabel: "Watch Stream",
            ButtonUrl: "{channel.url}"
        );
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("go_live", "live", embed);
        // A platform with no public page address and an owner with no picture.
        await harness.ShapeChannelAsync("youtube", "Stoney_Eagle", null);

        await harness.RaiseAsync(GoLive, LiveEvent(harness.BroadcasterId));

        DiscordEmbedDto posted = harness.Gateway.Posts.Single().Message.Embed!;
        posted.AuthorName.Should().Be("Stoney_Eagle");
        posted.AuthorIconUrl.Should().BeNull();
        posted.ButtonUrl.Should().BeNull("the channel has no web address on this platform");
    }

    [Fact]
    public async Task AGoLiveRuleMadeWithNoMessage_StartsWithTheReadyMessageAndEmbed()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connection = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        await using DiscordTestDbContext db = database.NewContext();
        Result<DiscordNotificationConfigDto> created = await NewService(db)
            .CreateConfigAsync(
                channel,
                connection,
                new("go_live", "chan-1", null, null, null, null, null, true)
            );

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        DiscordNotificationConfig stored = await db.DiscordNotificationConfigs.SingleAsync();
        stored.MessageTemplate.Should().Be(DiscordConfigDefaults.GoLiveMessage);
        stored.EmbedConfig.Should().BeEquivalentTo(DiscordConfigDefaults.GoLiveEmbed);
        stored.EmbedConfig!.AuthorName.Should().Be("{channel.name} is now live on Twitch!");
        stored.EmbedConfig.ImageUrl.Should().Be("{stream.thumbnail}");
        stored.EmbedConfig.ShowTimestamp.Should().BeTrue();
        stored.EmbedConfig.ButtonLabel.Should().Be("Watch Stream");
        stored.EmbedConfig.ButtonUrl.Should().Be("{channel.url}");
    }

    [Fact]
    public async Task AGoLiveRuleWithItsOwnMessage_IsStoredAsSent_AndOtherTriggersGetNoDefault()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connection = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        await using DiscordTestDbContext db = database.NewContext();
        DiscordNotificationConfigService service = NewService(db);
        await service.CreateConfigAsync(
            channel,
            connection,
            new("go_live", "chan-1", null, "mine", null, null, null, true)
        );
        await service.CreateConfigAsync(
            channel,
            connection,
            new("follow", "chan-1", null, null, null, null, null, true)
        );

        List<DiscordNotificationConfig> rows = await db
            .DiscordNotificationConfigs.OrderBy(c => c.TriggerType)
            .ToListAsync();
        rows.Single(c => c.TriggerType == "follow").MessageTemplate.Should().BeNull();
        rows.Single(c => c.TriggerType == "follow").EmbedConfig.Should().BeNull();
        rows.Single(c => c.TriggerType == "go_live").MessageTemplate.Should().Be("mine");
        rows.Single(c => c.TriggerType == "go_live").EmbedConfig.Should().BeNull();
    }

    [Fact]
    public async Task TheReadyGoLiveRule_PostsTheStreamersLinkAvatarPreviewAndButton()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "go_live",
                DiscordConfigDefaults.GoLiveMessage,
                DiscordConfigDefaults.GoLiveEmbed
            );
        await harness.ShapeChannelAsync(
            AuthEnums.Platform.Twitch,
            "Stoney_Eagle",
            "https://cdn.example/avatar.png"
        );

        await harness.RaiseAsync(GoLive, LiveEvent(harness.BroadcasterId));

        (string ChannelId, DiscordOutboundMessage Message) post = harness.Gateway.Posts.Single();
        post.Message.Content.Should()
            .Be(
                "Stoney_Eagle is live on Twitch streaming Just Chatting, blame the lag. Come hang out: https://www.twitch.tv/stoney_eagle"
            );
        DiscordEmbedDto embed = post.Message.Embed!;
        embed.AuthorName.Should().Be("Stoney_Eagle is now live on Twitch!");
        embed.AuthorIconUrl.Should().Be("https://cdn.example/avatar.png");
        embed.Title.Should().Be("blame the lag");
        embed.TitleUrl.Should().Be("https://www.twitch.tv/stoney_eagle");
        embed
            .ImageUrl.Should()
            .Be(
                $"https://static-cdn.jtvnw.net/previews-ttv/live_user_stoney_eagle-1280x720.jpg?t={WentLive.ToUnixTimeSeconds()}"
            );
        embed.ButtonUrl.Should().Be("https://www.twitch.tv/stoney_eagle");
    }

    private static ChannelOnlineEvent LiveEvent(Guid broadcaster) =>
        new()
        {
            Provider = AuthEnums.Platform.Twitch,
            BroadcasterId = broadcaster,
            BroadcasterDisplayName = "Stoney_Eagle",
            StreamTitle = "blame the lag",
            GameName = "Just Chatting",
            StartedAt = WentLive,
        };
}
