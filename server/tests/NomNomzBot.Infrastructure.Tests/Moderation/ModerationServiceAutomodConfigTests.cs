// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            Substitute.For<ITwitchModeratorsApi>(), // never touched by the read path
            Substitute.For<IChannelRegistry>(),
            TimeProvider.System,
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

    // ─── S066c: two concurrent whole-config saves must not silently clobber ───

    private static AutomodConfigDto Config(int capsThreshold) =>
        new(
            new AutomodLinkFilterDto(false, []),
            new AutomodCapsFilterDto(true, capsThreshold),
            new AutomodBannedPhrasesDto(false, []),
            new AutomodEmoteSpamDto(false, 10)
        );

    /// <summary>
    /// Fires a raw ADO.NET write against the same open SQLite connection the instant BEFORE a targeted
    /// non-query command executes — landing a "concurrent" writer's change into the exact gap between
    /// <c>SaveAutomodConfigAsync</c>'s read of the existing row (already completed and materialized into
    /// its in-memory <c>existing</c> snapshot by this point) and its guarded <c>ExecuteUpdateAsync</c>
    /// write (the very command about to run), deterministically, with no threads or timing involved.
    /// Fires at most once per instance, on the first non-query command whose parameter values contain
    /// <paramref name="parameterValueContains"/> — the per-rule-type substring EF parameterizes rather
    /// than inlines, so matching must inspect bound parameter values, not the (parameterized) command text.
    /// </summary>
    private sealed class InjectConcurrentWriteInterceptor(
        string parameterValueContains,
        Action<SqliteConnection> injectWrite
    ) : DbCommandInterceptor
    {
        private bool _fired;

        /// <summary>Off during seeding (the seed call's own insert/update of the not-yet-existing row must
        /// not trigger the injection) — the test arms it only once the row it targets already exists.</summary>
        public bool Armed { get; set; }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result
        )
        {
            bool isTargetUpdate =
                command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command
                    .Parameters.Cast<DbParameter>()
                    .Any(p =>
                        p.Value is string s
                        && s.Contains(parameterValueContains, StringComparison.Ordinal)
                    );

            if (Armed && !_fired && isTargetUpdate)
            {
                _fired = true;
                injectWrite((SqliteConnection)command.Connection!);
            }
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => new(NonQueryExecuting(command, eventData, result));
    }

    /// <summary>
    /// Simulates two mods editing AutoMod settings at once: Mod B's write (the raw SQL landed by the
    /// interceptor) commits into the exact window between this call's read of the existing caps-filter row
    /// and its own guarded write, so this call's in-hand snapshot is stale by the time it tries to save.
    /// It must fail with the real concurrency error, and the database must still hold Mod B's value — never
    /// silently overwritten with Mod A's now-outdated edit.
    /// </summary>
    [Fact]
    public async Task SaveAutomodConfigAsync_WithStaleVersion_FailsWithConcurrencyConflictInsteadOfOverwriting()
    {
        InjectConcurrentWriteInterceptor interceptor = new(
            parameterValueContains: "caps_filter",
            injectWrite: connection =>
            {
                using SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText =
                    "UPDATE Records SET Data = @data WHERE RecordType = 'moderation_rule' AND Data LIKE '%caps_filter%'";
                cmd.Parameters.AddWithValue(
                    "@data",
                    """{"Type":"caps_filter","Settings":{"threshold":77},"IsEnabled":true}"""
                );
                cmd.ExecuteNonQuery();
            }
        );

        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New(
            interceptor
        );
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "123",
                Name = "chan",
                NameNormalized = "chan",
            }
        );
        await db.SaveChangesAsync();

        ModerationService service = NewService(db);

        // Establishes the caps-filter row at threshold 30 (interceptor still disarmed — nothing to read yet).
        Result<AutomodConfigDto> seeded = await service.SaveAutomodConfigAsync(
            Channel.ToString(),
            Config(capsThreshold: 30)
        );
        seeded.IsSuccess.Should().BeTrue();
        interceptor.Armed = true;

        // This save's internal read of the caps-filter row sees threshold 30, then — before its guarded
        // write executes — the interceptor lands Mod B's concurrent change (threshold 77) directly on the
        // database. This save's in-hand snapshot (30) no longer matches the live row, so its guarded write
        // must match zero rows and the whole call must report CONCURRENCY_CONFLICT, not partially apply.
        Result<AutomodConfigDto> staleSave = await service.SaveAutomodConfigAsync(
            Channel.ToString(),
            Config(capsThreshold: 40)
        );

        staleSave.IsFailure.Should().BeTrue();
        staleSave.ErrorCode.Should().Be("CONCURRENCY_CONFLICT");

        // Clear this DbContext's identity map before the read-back: the seed save above still has its
        // inserted Record entities tracked, and the interceptor's raw-SQL write (simulating a totally
        // separate process/request) bypassed the tracker entirely, same as a real concurrent writer would.
        // Reading through the still-tracked instances would show this process's own stale in-memory values
        // rather than proving what actually landed on disk.
        db.ChangeTracker.Clear();

        // Mod B's concurrently-landed value must still be the one on disk — the stale save never applied.
        AutomodConfigDto persisted = (
            await service.GetAutomodConfigAsync(Channel.ToString())
        ).Value;
        persisted.CapsFilter.Threshold.Should().Be(77);
    }

    // ─── Saving against the current row succeeds normally ─────────────────────

    [Fact]
    public async Task SaveAutomodConfigAsync_WithCurrentVersion_SucceedsAndPersists()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                TwitchChannelId = "123",
                Name = "chan",
                NameNormalized = "chan",
            }
        );
        await db.SaveChangesAsync();

        ModerationService service = NewService(db);

        await service.SaveAutomodConfigAsync(Channel.ToString(), Config(capsThreshold: 30));
        Result<AutomodConfigDto> result = await service.SaveAutomodConfigAsync(
            Channel.ToString(),
            Config(capsThreshold: 55)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CapsFilter.Threshold.Should().Be(55);

        AutomodConfigDto persisted = (
            await service.GetAutomodConfigAsync(Channel.ToString())
        ).Value;
        persisted.CapsFilter.Threshold.Should().Be(55);
    }
}
