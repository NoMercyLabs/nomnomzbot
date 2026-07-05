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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;
using Record = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// <see cref="ModerationService.GetAutomodConfigAsync"/> reads the channel's four built-in AutoMod filters back out
/// of the free-form <c>Record</c> rows that <c>SaveAutomodConfigAsync</c> writes. This endpoint touches no Twitch/
/// Helix API — its only inputs are persisted rows — so the one realistic failure mode is a row whose stored JSON
/// (or one of its settings values) is not the shape the reader expects. These tests prove the reader returns a
/// well-formed <see cref="AutomodConfigDto"/> for both the happy path and every malformed-data path, and never
/// throws (the bug that surfaced as an unhandled HTTP 500 for a moderator opening the AutoMod page).
/// </summary>
public sealed class ModerationServiceAutomodConfigTests
{
    private const string RuleRecordType = "moderation_rule";
    private static readonly Guid Channel = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8a");

    private static ModerationService NewService(ModerationServiceTestDbContext db) =>
        new(
            db,
            Substitute.For<ITwitchModerationApi>(), // never touched by the read path
            NullLogger<ModerationService>.Instance,
            Substitute.For<IEventBus>() // never touched by the read path
        );

    private static Record Rule(string dataJson) =>
        new()
        {
            BroadcasterId = Channel,
            RecordType = RuleRecordType,
            Data = dataJson,
            UserId = Channel.ToString(),
        };

    // ─── Happy path: the returned DTO mirrors the stored filter rows ───────────

    [Fact]
    public async Task GetAutomodConfigAsync_WithValidRules_ReturnsConfigMatchingStoredShape()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Records.AddRange(
            Rule(
                """{"Type":"link_filter","Settings":{"whitelist":["twitch.tv","youtube.com"]},"IsEnabled":true}"""
            ),
            Rule("""{"Type":"caps_filter","Settings":{"threshold":80},"IsEnabled":true}"""),
            Rule(
                """{"Type":"banned_phrases","Settings":{"phrases":["badword"]},"IsEnabled":true}"""
            ),
            Rule("""{"Type":"emote_spam","Settings":{"maxEmotes":5},"IsEnabled":false}""")
        );
        await db.SaveChangesAsync();

        Result<AutomodConfigDto> result = await NewService(db)
            .GetAutomodConfigAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        AutomodConfigDto config = result.Value;

        config.LinkFilter.Enabled.Should().BeTrue();
        config.LinkFilter.Whitelist.Should().Equal("twitch.tv", "youtube.com");

        config.CapsFilter.Enabled.Should().BeTrue();
        config.CapsFilter.Threshold.Should().Be(80);

        config.BannedPhrases.Enabled.Should().BeTrue();
        config.BannedPhrases.Phrases.Should().Equal("badword");

        // The stored emote-spam row is disabled but still carries its value — both must round-trip.
        config.EmoteSpam.Enabled.Should().BeFalse();
        config.EmoteSpam.MaxEmotes.Should().Be(5);
    }

    // ─── Regression: a setting stored in the wrong shape must not 500 ──────────

    [Fact]
    public async Task GetAutomodConfigAsync_WithMalformedSettingValues_DegradesToDefaultsWithoutThrowing()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Records.AddRange(
            // threshold persisted as a string instead of a number — the old reader called JsonElement.GetInt32()
            // and threw InvalidOperationException, which bubbled up as an unhandled 500.
            Rule("""{"Type":"caps_filter","Settings":{"threshold":"seventy"},"IsEnabled":true}"""),
            // whitelist persisted as an object instead of an array — the old reader called EnumerateArray() and
            // threw InvalidOperationException.
            Rule("""{"Type":"link_filter","Settings":{"whitelist":{"nope":1}},"IsEnabled":true}""")
        );
        await db.SaveChangesAsync();

        Result<AutomodConfigDto> result = await NewService(db)
            .GetAutomodConfigAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        AutomodConfigDto config = result.Value;

        // The enabled flag (a well-formed field) is still honoured; only the malformed value folds to its default.
        config.CapsFilter.Enabled.Should().BeTrue();
        config.CapsFilter.Threshold.Should().Be(70);

        config.LinkFilter.Enabled.Should().BeTrue();
        config.LinkFilter.Whitelist.Should().BeEmpty();
    }

    // ─── Regression: one unparseable row must not sink the whole read ─────────

    [Fact]
    public async Task GetAutomodConfigAsync_WithUnparseableRuleRow_SkipsItAndReturnsOtherFilters()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Records.AddRange(
            // Not valid JSON, but still contains the "emote_spam" token so the reader's substring filter selects it.
            Rule("""{"Type":"emote_spam", BROKEN}"""),
            Rule("""{"Type":"caps_filter","Settings":{"threshold":90},"IsEnabled":true}""")
        );
        await db.SaveChangesAsync();

        Result<AutomodConfigDto> result = await NewService(db)
            .GetAutomodConfigAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        AutomodConfigDto config = result.Value;

        // The good row still parses…
        config.CapsFilter.Enabled.Should().BeTrue();
        config.CapsFilter.Threshold.Should().Be(90);
        // …and the corrupt emote_spam row is skipped, leaving that filter at its default.
        config.EmoteSpam.Enabled.Should().BeFalse();
        config.EmoteSpam.MaxEmotes.Should().Be(10);
    }

    // ─── No rows: every filter reports its documented default ─────────────────

    [Fact]
    public async Task GetAutomodConfigAsync_WithNoRules_ReturnsAllDefaults()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();

        Result<AutomodConfigDto> result = await NewService(db)
            .GetAutomodConfigAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        AutomodConfigDto config = result.Value;

        config.LinkFilter.Enabled.Should().BeFalse();
        config.LinkFilter.Whitelist.Should().BeEmpty();
        config.CapsFilter.Enabled.Should().BeFalse();
        config.CapsFilter.Threshold.Should().Be(70);
        config.BannedPhrases.Enabled.Should().BeFalse();
        config.BannedPhrases.Phrases.Should().BeEmpty();
        config.EmoteSpam.Enabled.Should().BeFalse();
        config.EmoteSpam.MaxEmotes.Should().Be(10);
    }

    // ─── A non-GUID channel id is a typed 404, not an exception ───────────────

    [Fact]
    public async Task GetAutomodConfigAsync_WithInvalidChannelId_ReturnsTypedChannelNotFound()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();

        Result<AutomodConfigDto> result = await NewService(db).GetAutomodConfigAsync("not-a-guid");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }
}
