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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S-OWN09 — the write path for a built-in's per-channel response-template override
/// (<see cref="IBuiltinCommandService.SetResponseOverrideAsync"/>), the generic mechanism the S-OWN17
/// "editable built-in response" pattern generalizes to every built-in, not just one. Proves real state
/// change on the relational test database: setting an override persists it and it round-trips through
/// <see cref="IBuiltinCommandService.ListAsync"/>, and clearing it removes it — never a surface/smoke check.
/// </summary>
public sealed class BuiltinCommandServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e02");
    private const string OrdinaryKey = "lurk";
    private const string ReservedKey = "forgetme";

    private static CommandsTestDbContext NewDb()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "builtin-service-channel",
                NameNormalized = "builtin-service-channel",
            }
        );
        db.SaveChanges();
        return db;
    }

    private const string SiblingKey = "unlurk"; // speaks with the lurk reply group
    private const string ModOnlyKey = "whisper"; // catalogue floor = Moderator (10)
    private const string SongRequestKey = "sr"; // one of DefaultCommandsSeeder's keys

    private static IBuiltinCommand FakeBuiltin(string key, bool reserved = false, int floor = 0) =>
        new FakeBuiltinCommand(key, reserved, floor);

    private static (
        BuiltinCommandService Sut,
        CommandsTestDbContext Db,
        IChannelRegistry Registry
    ) Build()
    {
        CommandsTestDbContext db = NewDb();
        IBuiltinCommand[] all =
        [
            FakeBuiltin(OrdinaryKey),
            FakeBuiltin(SiblingKey),
            FakeBuiltin(ModOnlyKey, floor: 10),
            FakeBuiltin(SongRequestKey),
            FakeBuiltin(ReservedKey, reserved: true),
        ];
        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        foreach (IBuiltinCommand command in all)
            catalog.Get(command.BuiltinKey).Returns(command);
        catalog.Get("unknown").Returns((IBuiltinCommand?)null);
        catalog.GetAll().Returns(all);

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry
            .InvalidateBuiltinsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        RecordingEventBus bus = new();
        return (new BuiltinCommandService(catalog, db, bus, registry), db, registry);
    }

    // ─── SetSpeakWithTtsAsync (S-OBS-12) ─────────────────────────────────────────

    [Fact]
    public async Task SetSpeakWithTts_persists_and_round_trips_through_ListAsync()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, IChannelRegistry registry) = Build();

        Result setResult = await sut.SetSpeakWithTtsAsync(Channel.ToString(), OrdinaryKey, true);
        setResult.IsSuccess.Should().BeTrue();

        ChannelBuiltinCommand row = await db.ChannelBuiltinCommands.SingleAsync(c =>
            c.BroadcasterId == Channel && c.BuiltinKey == OrdinaryKey
        );
        row.OverridesJson.Should().Contain("speakWithTts");
        row.IsEnabled.Should()
            .BeTrue("a fresh TTS-override row must not silently disable the built-in");

        IReadOnlyList<BuiltinCommandDto> listed = (await sut.ListAsync(Channel.ToString())).Value;
        listed.Single(d => d.BuiltinKey == OrdinaryKey).SpeakWithTts.Should().BeTrue();

        // Invalidates the in-memory registry cache so a running ChatMessageHandler picks up the new flag.
        await registry.Received(1).InvalidateBuiltinsAsync(Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetSpeakWithTts_false_clears_a_stored_flag()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        (await sut.SetSpeakWithTtsAsync(Channel.ToString(), OrdinaryKey, true))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.SetSpeakWithTtsAsync(Channel.ToString(), OrdinaryKey, false))
            .IsSuccess.Should()
            .BeTrue();

        ChannelBuiltinCommand row = await db.ChannelBuiltinCommands.SingleAsync(c =>
            c.BroadcasterId == Channel && c.BuiltinKey == OrdinaryKey
        );
        row.OverridesJson.Should().BeNull();

        IReadOnlyList<BuiltinCommandDto> listed = (await sut.ListAsync(Channel.ToString())).Value;
        listed.Single(d => d.BuiltinKey == OrdinaryKey).SpeakWithTts.Should().BeFalse();
    }

    [Fact]
    public async Task SetSpeakWithTts_keeps_the_channel_reply_texts_on_the_same_row()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();
        db.ChannelBuiltinCommands.Add(
            new()
            {
                BroadcasterId = Channel,
                BuiltinKey = OrdinaryKey,
                IsEnabled = true,
                OverridesJson = """{"responses":{"lurking":"grass, {user}"}}""",
            }
        );
        await db.SaveChangesAsync();

        (await sut.SetSpeakWithTtsAsync(Channel.ToString(), OrdinaryKey, true))
            .IsSuccess.Should()
            .BeTrue();

        // Setting the TTS flag must NOT wipe out the stored reply text — read-merge-write on the blob.
        ChannelBuiltinCommand row = await db.ChannelBuiltinCommands.SingleAsync(c =>
            c.BroadcasterId == Channel && c.BuiltinKey == OrdinaryKey
        );
        row.OverridesJson.Should()
            .Be("""{"responses":{"lurking":"grass, {user}"},"speakWithTts":true}""");

        IReadOnlyList<BuiltinCommandDto> listed = (await sut.ListAsync(Channel.ToString())).Value;
        BuiltinCommandDto dto = listed.Single(d => d.BuiltinKey == OrdinaryKey);
        dto.SpeakWithTts.Should().BeTrue();
        dto.ReplyGroup.Should().Be("lurk");
    }

    [Fact]
    public async Task SetSpeakWithTts_on_a_reserved_builtin_fails_and_persists_nothing()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        Result result = await sut.SetSpeakWithTtsAsync(Channel.ToString(), ReservedKey, true);

        result.IsFailure.Should().BeTrue();
        (await db.ChannelBuiltinCommands.AnyAsync(c => c.BuiltinKey == ReservedKey))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task SetSpeakWithTts_on_an_unknown_key_fails_with_NOT_FOUND()
    {
        (BuiltinCommandService sut, _, _) = Build();

        Result result = await sut.SetSpeakWithTtsAsync(Channel.ToString(), "unknown", true);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ─── UpdateSettingsAsync / ResetAsync (commands-pipelines.md §4.5) ───────────

    private static Task<ChannelBuiltinCommand> RowAsync(CommandsTestDbContext db, string key) =>
        db
            .ChannelBuiltinCommands.AsNoTracking()
            .SingleAsync(c => c.BroadcasterId == Channel && c.BuiltinKey == key);

    [Fact]
    public async Task UpdateSettings_persists_cooldown_and_floor_and_keeps_replies_and_tts()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, IChannelRegistry registry) = Build();
        db.ChannelBuiltinCommands.Add(
            new()
            {
                BroadcasterId = Channel,
                BuiltinKey = OrdinaryKey,
                IsEnabled = true,
                OverridesJson = """{"responses":{"lurking":"grass, {user}"},"speakWithTts":true}""",
            }
        );
        await db.SaveChangesAsync();

        Result<BuiltinCommandDto> result = await sut.UpdateSettingsAsync(
            Channel.ToString(),
            OrdinaryKey,
            new(CooldownSeconds: 60, MinPermissionLevel: "Subscriber")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.CooldownSecondsOverride.Should().Be(60);
        result.Value.MinPermissionLevelOverride.Should().Be("Subscriber");
        result.Value.SpeakWithTts.Should().BeTrue("a settings write must not clear the TTS flag");
        result.Value.ReplyOverrideCount.Should().Be(1);

        // The exact stored blob: every earlier field survives, the two new ones are the ladder value + seconds.
        (await RowAsync(db, OrdinaryKey))
            .OverridesJson.Should()
            .Be(
                """{"responses":{"lurking":"grass, {user}"},"speakWithTts":true,"cooldownSeconds":60,"minPermissionLevel":2}"""
            );
        await registry.Received(1).InvalidateBuiltinsAsync(Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateSettings_with_the_default_values_stores_no_override()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        // Fake catalogue default: 5 s, Everyone.
        Result<BuiltinCommandDto> result = await sut.UpdateSettingsAsync(
            Channel.ToString(),
            OrdinaryKey,
            new(CooldownSeconds: 5, MinPermissionLevel: "Everyone")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.CooldownSecondsOverride.Should().BeNull();
        result.Value.MinPermissionLevelOverride.Should().BeNull();
        (await db.ChannelBuiltinCommands.AnyAsync(c => c.BuiltinKey == OrdinaryKey))
            .Should()
            .BeFalse("an all-default write on a channel with no row has nothing to store");
    }

    [Fact]
    public async Task UpdateSettings_refuses_to_lower_a_safety_floor_and_persists_nothing()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        Result<BuiltinCommandDto> result = await sut.UpdateSettingsAsync(
            Channel.ToString(),
            ModOnlyKey,
            new(CooldownSeconds: null, MinPermissionLevel: "Everyone")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("Moderator");
        (await db.ChannelBuiltinCommands.AnyAsync(c => c.BuiltinKey == ModOnlyKey))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task UpdateSettings_can_raise_a_safety_floor()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        Result<BuiltinCommandDto> result = await sut.UpdateSettingsAsync(
            Channel.ToString(),
            ModOnlyKey,
            new(CooldownSeconds: null, MinPermissionLevel: "Broadcaster")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.MinPermissionLevelOverride.Should().Be("Broadcaster");
        (await RowAsync(db, ModOnlyKey)).OverridesJson.Should().Be("""{"minPermissionLevel":40}""");
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(3601, null)]
    [InlineData(null, "Admin")]
    [InlineData(null, "10")]
    public async Task UpdateSettings_rejects_an_out_of_range_cooldown_or_an_unknown_rung(
        int? cooldown,
        string? floor
    )
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        Result<BuiltinCommandDto> result = await sut.UpdateSettingsAsync(
            Channel.ToString(),
            OrdinaryKey,
            new(cooldown, floor)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ChannelBuiltinCommands.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSettings_on_a_reserved_builtin_fails()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        Result<BuiltinCommandDto> result = await sut.UpdateSettingsAsync(
            Channel.ToString(),
            ReservedKey,
            new(CooldownSeconds: 60, MinPermissionLevel: null)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ChannelBuiltinCommands.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Reset_restores_every_default_and_clears_shared_replies_but_not_the_sibling_settings()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, IChannelRegistry registry) = Build();
        db.ChannelBuiltinCommands.AddRange(
            new ChannelBuiltinCommand
            {
                BroadcasterId = Channel,
                BuiltinKey = OrdinaryKey,
                IsEnabled = false,
                OverridesJson =
                    """{"responses":{"lurking":"grass"},"speakWithTts":true,"cooldownSeconds":60,"minPermissionLevel":2}""",
            },
            // !unlurk speaks with the lurk group: its legacy reply text belongs to the reply set being reset,
            // its own cooldown belongs to !unlurk and must survive.
            new ChannelBuiltinCommand
            {
                BroadcasterId = Channel,
                BuiltinKey = SiblingKey,
                IsEnabled = true,
                OverridesJson = """{"responses":{"notlurking":"back!"},"cooldownSeconds":30}""",
            }
        );
        await db.SaveChangesAsync();

        BuiltinCommandDto before = (await sut.GetAsync(Channel.ToString(), OrdinaryKey)).Value;
        before.ReplyOverrideCount.Should().Be(2, "the reset consequence counts both group rows");

        Result<BuiltinCommandDto> result = await sut.ResetAsync(Channel.ToString(), OrdinaryKey);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.IsEnabled.Should().BeTrue();
        result.Value.SpeakWithTts.Should().BeFalse();
        result.Value.CooldownSecondsOverride.Should().BeNull();
        result.Value.MinPermissionLevelOverride.Should().BeNull();
        result.Value.ReplyOverrideCount.Should().Be(0);

        ChannelBuiltinCommand own = await RowAsync(db, OrdinaryKey);
        own.IsEnabled.Should().BeTrue();
        own.OverridesJson.Should().BeNull();
        (await RowAsync(db, SiblingKey)).OverridesJson.Should().Be("""{"cooldownSeconds":30}""");
        await registry.Received(1).InvalidateBuiltinsAsync(Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_reply_write_keeps_the_cooldown_and_floor_on_the_same_row()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();
        (
            await sut.UpdateSettingsAsync(
                Channel.ToString(),
                OrdinaryKey,
                new(CooldownSeconds: 90, MinPermissionLevel: "Vip")
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        // TTS goes through the same read-merge-write as a reply text.
        (await sut.SetSpeakWithTtsAsync(Channel.ToString(), OrdinaryKey, true))
            .IsSuccess.Should()
            .BeTrue();

        (await RowAsync(db, OrdinaryKey))
            .OverridesJson.Should()
            .Be("""{"speakWithTts":true,"cooldownSeconds":90,"minPermissionLevel":4}""");
    }

    [Fact]
    public async Task A_default_commands_reseed_never_overwrites_the_channel_settings()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();
        (
            await sut.UpdateSettingsAsync(
                Channel.ToString(),
                SongRequestKey,
                new(CooldownSeconds: 120, MinPermissionLevel: "Subscriber")
            )
        )
            .IsSuccess.Should()
            .BeTrue();
        (await sut.SetEnabledAsync(Channel.ToString(), SongRequestKey, false))
            .IsSuccess.Should()
            .BeTrue();

        await new Infrastructure.Content.Commands.DefaultCommandsSeeder(db).SeedAsync(Channel);
        db.ChangeTracker.Clear();

        ChannelBuiltinCommand row = await RowAsync(db, SongRequestKey);
        row.IsEnabled.Should().BeFalse("the seeder only adds missing rows");
        row.OverridesJson.Should().Be("""{"cooldownSeconds":120,"minPermissionLevel":2}""");
        (await db.ChannelBuiltinCommands.CountAsync(c => c.BuiltinKey == "skip"))
            .Should()
            .Be(1, "the seeder still adds the rows the channel is missing");
    }

    private sealed class FakeBuiltinCommand : IBuiltinCommand
    {
        public FakeBuiltinCommand(string builtinKey, bool reserved, int floor)
        {
            BuiltinKey = builtinKey;
            IsReserved = reserved;
            DefaultMinPermissionLevel = floor;
        }

        public string BuiltinKey { get; }
        public int DefaultCooldownSeconds => 5;
        public int DefaultMinPermissionLevel { get; }
        public bool IsReserved { get; }

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success("ok"));
    }
}
