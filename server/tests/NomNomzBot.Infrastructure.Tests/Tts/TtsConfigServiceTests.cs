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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the P.1 config table behavior: a channel with no row reads as the binding new-channel defaults
/// (censor ON, self_host/edge, no row created by the read); the first update CREATES the row and persists
/// exactly the patched fields; a later partial update leaves untouched fields alone; MinBitsToTts=0 clears
/// the bits gate; and every update publishes the E5 dashboard live-sync event.
/// </summary>
public sealed class TtsConfigServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000d01");

    private static (TtsConfigService Sut, TtsTestDbContext Db, RecordingEventBus Bus) Build()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        ITtsService ttsService = Substitute.For<ITtsService>();
        RecordingEventBus bus = new();
        return (new TtsConfigService(db, ttsService, bus), db, bus);
    }

    [Fact]
    public async Task Get_without_a_row_returns_the_binding_defaults_and_creates_nothing()
    {
        (TtsConfigService sut, TtsTestDbContext db, _) = Build();

        Result<TtsConfigDto> result = await sut.GetConfigAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();
        result.Value.Mode.Should().Be("self_host");
        result.Value.DefaultProvider.Should().Be("edge");
        result.Value.ProfanityCensorEnabled.Should().BeTrue("the swear filter is opt-OUT");
        result.Value.ModApprovalRequired.Should().BeFalse();
        result.Value.MinBitsToTts.Should().BeNull();
        (await db.TtsConfigs.CountAsync()).Should().Be(0, "reads never write");
    }

    [Fact]
    public async Task First_update_creates_the_row_and_persists_the_patched_fields()
    {
        (TtsConfigService sut, TtsTestDbContext db, RecordingEventBus bus) = Build();

        Result<TtsConfigDto> result = await sut.UpdateConfigAsync(
            Channel,
            new UpdateTtsConfigDto
            {
                IsEnabled = false,
                MaxCharacters = 120,
                MinBitsToTts = 50,
            }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();
        result.Value.MaxCharacters.Should().Be(120);
        result.Value.MinBitsToTts.Should().Be(50);

        TtsConfig row = await db.TtsConfigs.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.IsEnabled.Should().BeFalse();
        row.MaxCharacters.Should().Be(120);
        row.MinBitsToTts.Should().Be(50);
        row.ProfanityCensorEnabled.Should().BeTrue("unpatched fields keep their defaults");

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel && e.Domain == "tts-config" && e.Action == "updated"
            );
    }

    [Fact]
    public async Task A_partial_update_changes_only_what_was_sent()
    {
        (TtsConfigService sut, TtsTestDbContext db, _) = Build();
        await sut.UpdateConfigAsync(
            Channel,
            new UpdateTtsConfigDto { MaxCharacters = 120, DefaultVoiceId = "en-GB-SoniaNeural" }
        );

        await sut.UpdateConfigAsync(Channel, new UpdateTtsConfigDto { ModApprovalRequired = true });

        TtsConfig row = await db.TtsConfigs.SingleAsync();
        row.ModApprovalRequired.Should().BeTrue();
        row.MaxCharacters.Should().Be(120, "the earlier write survives a partial patch");
        row.DefaultVoiceId.Should().Be("en-GB-SoniaNeural");
        (await db.TtsConfigs.CountAsync())
            .Should()
            .Be(1, "updates upsert the single per-channel row");
    }

    [Fact]
    public async Task MinBitsToTts_zero_clears_the_bits_gate()
    {
        (TtsConfigService sut, TtsTestDbContext db, _) = Build();
        await sut.UpdateConfigAsync(Channel, new UpdateTtsConfigDto { MinBitsToTts = 100 });

        Result<TtsConfigDto> result = await sut.UpdateConfigAsync(
            Channel,
            new UpdateTtsConfigDto { MinBitsToTts = 0 }
        );

        result.Value.MinBitsToTts.Should().BeNull();
        (await db.TtsConfigs.SingleAsync()).MinBitsToTts.Should().BeNull();
    }
}
