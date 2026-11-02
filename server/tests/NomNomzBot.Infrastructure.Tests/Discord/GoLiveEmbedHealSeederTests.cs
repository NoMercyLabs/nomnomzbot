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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Discord.ValueObjects;
using NomNomzBot.Infrastructure.Discord;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// A go-live rule saved before the ready embed shipped kept its plain text and no embed, and its title and game
/// placeholders read the channel's live state, which is empty at the moment the stream starts. The heal gives such an
/// untouched rule the ready message and embed, and leaves every rule the streamer changed alone.
/// </summary>
public sealed class GoLiveEmbedHealSeederTests
{
    private const string OldDefault = "{channel.name} {tense} live! {stream.title} ({stream.game})";

    private static async Task<DiscordSqliteTestDatabase> SeedAsync(
        string trigger,
        string message,
        DiscordEmbedConfig? embed
    )
    {
        DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connection = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        await DiscordTestHarness.SeedConfigAsync(
            database,
            channel,
            connection,
            message,
            "200",
            triggerType: trigger,
            embed: embed
        );

        return database;
    }

    private static async Task HealAsync(
        DiscordSqliteTestDatabase database,
        ITemplateHelperValidator? validator = null
    )
    {
        await using DiscordTestDbContext db = database.NewContext();
        await new GoLiveEmbedHealSeeder(
            db,
            validator ?? DiscordTemplateTestSupport.CreateValidator()
        ).SeedAsync();
    }

    private static async Task<DiscordNotificationConfig> OnlyRuleAsync(
        DiscordSqliteTestDatabase database
    )
    {
        await using DiscordTestDbContext db = database.NewContext();
        return await db.DiscordNotificationConfigs.IgnoreQueryFilters().SingleAsync();
    }

    [Fact]
    public async Task An_untouched_old_go_live_rule_gets_the_ready_message_and_embed()
    {
        using DiscordSqliteTestDatabase database = await SeedAsync("go_live", OldDefault, null);

        await HealAsync(database);

        DiscordNotificationConfig rule = await OnlyRuleAsync(database);
        rule.MessageTemplate.Should().Be(DiscordConfigDefaults.GoLiveMessage);
        rule.EmbedConfig.Should().NotBeNull();
        rule.EmbedConfig!.Title.Should().Be("{title}");
        rule.EmbedConfig.TitleUrl.Should().Be("{channel.url}");
        rule.EmbedConfig.AuthorName.Should().Be("{channel.name} is now live on Twitch!");
        rule.EmbedConfig.ImageUrl.Should().Be("{stream.thumbnail}");
        rule.EmbedConfig.ButtonLabel.Should().Be("Watch Stream");
        rule.EmbedConfig.Fields.Should().ContainSingle().Which.Value.Should().Be("{game}");
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        using DiscordSqliteTestDatabase database = await SeedAsync("go_live", OldDefault, null);
        await HealAsync(database);
        DiscordNotificationConfig first = await OnlyRuleAsync(database);

        await HealAsync(database);

        DiscordNotificationConfig second = await OnlyRuleAsync(database);
        second.MessageTemplate.Should().Be(DiscordConfigDefaults.GoLiveMessage);
        second.EmbedConfig!.Title.Should().Be(first.EmbedConfig!.Title);
        second.UpdatedAt.Should().Be(first.UpdatedAt);
    }

    [Fact]
    public async Task A_go_live_rule_the_streamer_worded_is_left_alone()
    {
        const string Own = "{channel.name} is on! {title}";
        using DiscordSqliteTestDatabase database = await SeedAsync("go_live", Own, null);

        await HealAsync(database);

        DiscordNotificationConfig rule = await OnlyRuleAsync(database);
        rule.MessageTemplate.Should().Be(Own);
        rule.EmbedConfig.Should().BeNull();
    }

    [Fact]
    public async Task A_rule_with_its_own_embed_is_left_alone()
    {
        DiscordEmbedConfig own = new("Mine", null, "#FF0000", null, null, null, null);
        using DiscordSqliteTestDatabase database = await SeedAsync("go_live", OldDefault, own);

        await HealAsync(database);

        DiscordNotificationConfig rule = await OnlyRuleAsync(database);
        rule.MessageTemplate.Should().Be(OldDefault);
        rule.EmbedConfig!.Title.Should().Be("Mine");
        rule.EmbedConfig.ButtonLabel.Should().BeNull();
    }

    [Fact]
    public async Task Defaults_the_save_check_rejects_are_never_written()
    {
        ITemplateHelperValidator validator = Substitute.For<ITemplateHelperValidator>();
        validator
            .Validate(Arg.Any<string?>(), Arg.Any<TemplateHelperContext>())
            .Returns(Result.Failure("unknown helper", "INVALID_TEMPLATE"));
        using DiscordSqliteTestDatabase database = await SeedAsync("go_live", OldDefault, null);

        await HealAsync(database, validator);

        DiscordNotificationConfig rule = await OnlyRuleAsync(database);
        rule.MessageTemplate.Should().Be(OldDefault);
        rule.EmbedConfig.Should().BeNull();
    }

    [Fact]
    public async Task Another_trigger_with_the_same_words_is_left_alone()
    {
        using DiscordSqliteTestDatabase database = await SeedAsync("follow", OldDefault, null);

        await HealAsync(database);

        DiscordNotificationConfig rule = await OnlyRuleAsync(database);
        rule.MessageTemplate.Should().Be(OldDefault);
        rule.EmbedConfig.Should().BeNull();
    }
}
