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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The per-channel reply catalogue (commands-pipelines.md §11) on a real SQLite test database: a slot's own text
/// changes exactly that slot, a legacy single override still applies and is migrated on the next write, reset
/// restores the tone default, an unknown variable is refused, and each reply reports the layer its text comes
/// from for the channel's personality.
/// </summary>
public sealed class BuiltinReplyServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e21");

    private const string Sr = BuiltinResponseSlots.SongRequest.Key;
    private const string Added = BuiltinResponseSlots.SongRequest.Added;
    private const string Duplicate = BuiltinResponseSlots.SongRequest.Duplicate;

    private sealed record Harness(
        BuiltinReplyService Sut,
        CommandsTestDbContext Db,
        IChannelRegistry Registry,
        RecordingEventBus Bus
    );

    private static Harness Build(
        string personality = PersonalityTone.Informative,
        IPlatformBuiltinReplyDefaults? platform = null
    )
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "reply-channel",
                NameNormalized = "reply-channel",
                Personality = personality,
            }
        );
        db.SaveChanges();

        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        catalog
            .GetAll()
            .Returns(
                new IBuiltinCommand[]
                {
                    new FakeBuiltin(Sr, reserved: false),
                    new FakeBuiltin("lurk", reserved: false),
                    new FakeBuiltin("unlurk", reserved: false),
                    new FakeBuiltin(BuiltinResponseSlots.Forgetme.Key, reserved: true),
                }
            );

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        RecordingEventBus bus = new();
        BuiltinReplyService sut = new(
            db,
            catalog,
            platform ?? NoPlatformBuiltinReplies.Instance,
            new TemplateHelperValidator(),
            registry,
            bus
        );
        return new(sut, db, registry, bus);
    }

    private static async Task<BuiltinReplyDto> ReplyAsync(Harness h, string group, string slot)
    {
        IReadOnlyList<BuiltinReplyGroupDto> groups = (
            await h.Sut.ListAsync(Channel.ToString())
        ).Value;
        return groups.Single(g => g.BuiltinKey == group).Replies.Single(r => r.Slot == slot);
    }

    [Fact]
    public async Task Setting_one_slot_changes_that_reply_only_and_persists_it_per_slot()
    {
        Harness h = Build();

        Result<BuiltinReplyDto> set = await h.Sut.SetAsync(
            Channel.ToString(),
            Sr,
            Duplicate,
            "Already queued by {requested.by}, {user}!"
        );

        set.IsSuccess.Should().BeTrue(set.ErrorMessage);
        set.Value.EffectiveTemplate.Should().Be("Already queued by {requested.by}, {user}!");
        set.Value.Source.Should().Be(BuiltinReplySource.Channel);
        set.Value.IsOverridden.Should().BeTrue();

        ChannelBuiltinCommand row = await h.Db.ChannelBuiltinCommands.SingleAsync();
        row.BuiltinKey.Should().Be(Sr);
        row.OverridesJson.Should()
            .Be("""{"responses":{"duplicate":"Already queued by {requested.by}, {user}!"}}""");

        BuiltinReplyDto added = await ReplyAsync(h, Sr, Added);
        added.IsOverridden.Should().BeFalse("only the duplicate slot was re-worded");
        added.Source.Should().Be(BuiltinReplySource.Tone);
        added.EffectiveTemplate.Should().Be(ToneTemplateCatalog.ShippedTemplate(Sr, Added));

        await h.Registry.Received(1).InvalidateBuiltinsAsync(Channel, Arg.Any<CancellationToken>());
        h.Bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e => e.EntityId == "sr/duplicate" && e.Action == "reply_set");
    }

    [Fact]
    public async Task A_legacy_single_override_applies_to_its_slot_and_migrates_on_the_next_write()
    {
        Harness h = Build();
        h.Db.ChannelBuiltinCommands.Add(
            new()
            {
                BroadcasterId = Channel,
                BuiltinKey = Sr,
                IsEnabled = true,
                OverridesJson = """{"responseTemplate":"Queued {track.name}!"}""",
            }
        );
        await h.Db.SaveChangesAsync();

        BuiltinReplyDto legacy = await ReplyAsync(h, Sr, Added);
        legacy.EffectiveTemplate.Should().Be("Queued {track.name}!");
        legacy.IsOverridden.Should().BeTrue();
        (await ReplyAsync(h, Sr, Duplicate))
            .IsOverridden.Should()
            .BeFalse("the legacy field only ever fed the added reply");

        (await h.Sut.SetAsync(Channel.ToString(), Sr, Duplicate, "Dupe, {user}."))
            .IsSuccess.Should()
            .BeTrue();

        ChannelBuiltinCommand row = await h.Db.ChannelBuiltinCommands.SingleAsync();
        row.OverridesJson.Should()
            .Be("""{"responses":{"added":"Queued {track.name}!","duplicate":"Dupe, {user}."}}""");
    }

    [Fact]
    public async Task Reset_restores_the_tone_default_and_clears_the_stored_text()
    {
        Harness h = Build(PersonalityTone.Informative);
        await h.Sut.SetAsync(Channel.ToString(), Sr, Added, "Mine: {track.name}");

        Result<BuiltinReplyDto> reset = await h.Sut.ResetAsync(Channel.ToString(), Sr, Added);

        reset.IsSuccess.Should().BeTrue();
        reset.Value.IsOverridden.Should().BeFalse();
        reset.Value.Source.Should().Be(BuiltinReplySource.Tone);
        reset.Value.EffectiveTemplate.Should().Be(ToneTemplateCatalog.ShippedTemplate(Sr, Added));
        reset.Value.EffectiveTemplate.Should().Be(reset.Value.DefaultTemplate);
        (await h.Db.ChannelBuiltinCommands.SingleAsync()).OverridesJson.Should().BeNull();
        h.Bus.Published.OfType<ChannelConfigChangedEvent>()
            .Select(e => e.Action)
            .Should()
            .Equal("reply_set", "reply_reset");
    }

    [Fact]
    public async Task An_unknown_variable_is_refused_by_name_and_nothing_is_written()
    {
        Harness h = Build();

        Result<BuiltinReplyDto> result = await h.Sut.SetAsync(
            Channel.ToString(),
            Sr,
            Added,
            "Added {track.nmae}"
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("track.nmae").And.Contain("track.name");
        (await h.Db.ChannelBuiltinCommands.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_slot_is_not_found_and_a_blank_template_resets()
    {
        Harness h = Build();
        await h.Sut.SetAsync(Channel.ToString(), Sr, Added, "Mine {track.name}");

        Result<BuiltinReplyDto> unknown = await h.Sut.SetAsync(
            Channel.ToString(),
            Sr,
            "nonsense",
            "x"
        );
        Result<BuiltinReplyDto> blank = await h.Sut.SetAsync(Channel.ToString(), Sr, Added, "   ");

        unknown.ErrorCode.Should().Be("NOT_FOUND");
        blank.Value.IsOverridden.Should().BeFalse();
        (await h.Db.ChannelBuiltinCommands.SingleAsync()).OverridesJson.Should().BeNull();
    }

    [Fact]
    public async Task The_catalogue_reports_the_tone_lines_of_the_channel_personality()
    {
        Harness h = Build(PersonalityTone.Sassy);

        BuiltinReplyDto added = await ReplyAsync(h, Sr, Added);

        added
            .ToneVariations.Should()
            .Equal(ToneTemplateCatalog.Get(PersonalityTone.Sassy, Sr, Added));
        added.DefaultTemplate.Should().Be(added.ToneVariations[0]);
        added.Source.Should().Be(BuiltinReplySource.Tone);
        added.Label.Key.Should().Be("builtin.reply.sr.added.label");
        added
            .Variables.Select(v => v.Name)
            .Should()
            .BeEquivalentTo(ToneTemplateCatalog.Variables(Sr, Added));
        added.Variables.Single(v => v.Name == "track.name").SampleValue.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_platform_reply_is_the_default_and_the_source_until_the_channel_sets_its_own()
    {
        IPlatformBuiltinReplyDefaults platform = Substitute.For<IPlatformBuiltinReplyDefaults>();
        platform
            .GetAsync(Sr, Added, Arg.Any<CancellationToken>())
            .Returns("Platform says {track.name}");
        Harness h = Build(PersonalityTone.Hype, platform);

        BuiltinReplyDto before = await ReplyAsync(h, Sr, Added);
        BuiltinReplyDto after = (
            await h.Sut.SetAsync(Channel.ToString(), Sr, Added, "Own {track.name}")
        ).Value;

        before.Source.Should().Be(BuiltinReplySource.Platform);
        before.EffectiveTemplate.Should().Be("Platform says {track.name}");
        before.ToneVariations.Should().BeEmpty("the platform text replaces the tone lines");
        after.Source.Should().Be(BuiltinReplySource.Channel);
        after.DefaultTemplate.Should().Be("Platform says {track.name}");
    }

    [Fact]
    public async Task A_shared_reply_group_lists_every_trigger_that_speaks_with_it()
    {
        Harness h = Build();

        IReadOnlyList<BuiltinReplyGroupDto> groups = (
            await h.Sut.ListAsync(Channel.ToString())
        ).Value;

        groups.Single(g => g.BuiltinKey == "lurk").CommandKeys.Should().Equal("lurk", "unlurk");
        groups
            .Single(g => g.BuiltinKey == BuiltinResponseSlots.BotStatus.Key)
            .CommandKeys.Should()
            .BeEmpty();
    }

    private sealed class FakeBuiltin(string builtinKey, bool reserved) : IBuiltinCommand
    {
        public string BuiltinKey { get; } = builtinKey;
        public int DefaultCooldownSeconds => 5;
        public int DefaultMinPermissionLevel => 0;
        public bool IsReserved { get; } = reserved;

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success("ok"));
    }
}
