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

    private static IBuiltinCommand FakeBuiltin(string key, bool reserved = false) =>
        new FakeBuiltinCommand(key, reserved);

    private static (
        BuiltinCommandService Sut,
        CommandsTestDbContext Db,
        IChannelRegistry Registry
    ) Build()
    {
        CommandsTestDbContext db = NewDb();
        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        catalog.Get(OrdinaryKey).Returns(FakeBuiltin(OrdinaryKey));
        catalog.Get(ReservedKey).Returns(FakeBuiltin(ReservedKey, reserved: true));
        catalog.Get("unknown").Returns((IBuiltinCommand?)null);
        catalog
            .GetAll()
            .Returns(
                new IBuiltinCommand[]
                {
                    FakeBuiltin(OrdinaryKey),
                    FakeBuiltin(ReservedKey, reserved: true),
                }
            );

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry
            .InvalidateBuiltinsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        RecordingEventBus bus = new();
        return (new BuiltinCommandService(catalog, db, bus, registry), db, registry);
    }

    [Fact]
    public async Task SetResponseOverride_persists_and_round_trips_through_ListAsync()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        Result setResult = await sut.SetResponseOverrideAsync(
            Channel.ToString(),
            OrdinaryKey,
            "go touch grass, {{user.name}}"
        );
        setResult.IsSuccess.Should().BeTrue();

        // Persisted state: exactly one row, carrying the exact template inside OverridesJson.
        ChannelBuiltinCommand row = await db.ChannelBuiltinCommands.SingleAsync(c =>
            c.BroadcasterId == Channel && c.BuiltinKey == OrdinaryKey
        );
        row.OverridesJson.Should().NotBeNullOrWhiteSpace();
        row.OverridesJson.Should().Contain("go touch grass, {{user.name}}");
        row.IsEnabled.Should()
            .BeTrue("a fresh override row must not silently disable the built-in");

        // Retrieval: ListAsync must surface the same override text on the DTO.
        IReadOnlyList<BuiltinCommandDto> listed = (await sut.ListAsync(Channel.ToString())).Value;
        BuiltinCommandDto dto = listed.Single(d => d.BuiltinKey == OrdinaryKey);
        dto.ResponseOverride.Should().Be("go touch grass, {{user.name}}");
    }

    [Fact]
    public async Task SetResponseOverride_with_blank_template_clears_a_stored_override()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        (await sut.SetResponseOverrideAsync(Channel.ToString(), OrdinaryKey, "custom text"))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.SetResponseOverrideAsync(Channel.ToString(), OrdinaryKey, "   "))
            .IsSuccess.Should()
            .BeTrue();

        ChannelBuiltinCommand row = await db.ChannelBuiltinCommands.SingleAsync(c =>
            c.BroadcasterId == Channel && c.BuiltinKey == OrdinaryKey
        );
        row.OverridesJson.Should().BeNull();

        IReadOnlyList<BuiltinCommandDto> listed = (await sut.ListAsync(Channel.ToString())).Value;
        listed.Single(d => d.BuiltinKey == OrdinaryKey).ResponseOverride.Should().BeNull();
    }

    [Fact]
    public async Task SetResponseOverride_on_a_reserved_builtin_fails_and_persists_nothing()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        Result result = await sut.SetResponseOverrideAsync(Channel.ToString(), ReservedKey, "nope");

        result.IsFailure.Should().BeTrue();
        (await db.ChannelBuiltinCommands.AnyAsync(c => c.BuiltinKey == ReservedKey))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task SetResponseOverride_on_an_unknown_key_fails_with_NOT_FOUND()
    {
        (BuiltinCommandService sut, _, _) = Build();

        Result result = await sut.SetResponseOverrideAsync(Channel.ToString(), "unknown", "x");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
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
    public async Task SetSpeakWithTts_coexists_with_an_existing_response_override_on_the_same_row()
    {
        (BuiltinCommandService sut, CommandsTestDbContext db, _) = Build();

        (
            await sut.SetResponseOverrideAsync(
                Channel.ToString(),
                OrdinaryKey,
                "grass, {{user.name}}"
            )
        )
            .IsSuccess.Should()
            .BeTrue();
        (await sut.SetSpeakWithTtsAsync(Channel.ToString(), OrdinaryKey, true))
            .IsSuccess.Should()
            .BeTrue();

        // Setting the TTS flag must NOT wipe out the previously stored response override — the read-merge-write
        // fix this slice makes to the OverridesJson blob.
        ChannelBuiltinCommand row = await db.ChannelBuiltinCommands.SingleAsync(c =>
            c.BroadcasterId == Channel && c.BuiltinKey == OrdinaryKey
        );
        row.OverridesJson.Should().Contain("grass, {{user.name}}");
        row.OverridesJson.Should().Contain("speakWithTts");

        IReadOnlyList<BuiltinCommandDto> listed = (await sut.ListAsync(Channel.ToString())).Value;
        BuiltinCommandDto dto = listed.Single(d => d.BuiltinKey == OrdinaryKey);
        dto.ResponseOverride.Should().Be("grass, {{user.name}}");
        dto.SpeakWithTts.Should().BeTrue();
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

    private sealed class FakeBuiltinCommand : IBuiltinCommand
    {
        public FakeBuiltinCommand(string builtinKey, bool reserved)
        {
            BuiltinKey = builtinKey;
            IsReserved = reserved;
        }

        public string BuiltinKey { get; }
        public int DefaultCooldownSeconds => 5;
        public int DefaultMinPermissionLevel => 0;
        public bool IsReserved { get; }

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success("ok"));
    }
}
