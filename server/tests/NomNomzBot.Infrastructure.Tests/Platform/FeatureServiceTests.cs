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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves <see cref="FeatureService.ToggleFeatureAsync"/> publishes the E5 dashboard live-sync event after every
/// successful toggle so a second open dashboard's Features page refetches, that an invalid channel id (the
/// only rejection path) never publishes, that the four chat-decoration keys (chat-decoration spec §5/§9·9) are
/// registered in the catalogue with their own defaults, and that a channel with NO row yet toggles away from its
/// key's default in a single call (not two) — the audited "first disable click re-enables" bug.
/// </summary>
public sealed class FeatureServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000001001");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (FeatureService Sut, RecordingEventBus Bus) Build()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        RecordingEventBus bus = new();
        return (new FeatureService(db, new FakeTimeProvider(Now), bus), bus);
    }

    [Fact]
    public async Task Toggle_publishes_ChannelConfigChangedEvent_for_the_features_domain()
    {
        (FeatureService sut, RecordingEventBus bus) = Build();

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "custom_code"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel
                && e.Domain == "features"
                && e.EntityId == "custom_code"
                && e.Action == "toggled"
            );
    }

    [Fact]
    public async Task Toggling_twice_flips_the_state_and_publishes_each_time()
    {
        (FeatureService sut, RecordingEventBus bus) = Build();

        await sut.ToggleFeatureAsync(Channel.ToString(), "custom_code");
        Result<FeatureStatusDto> second = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "custom_code"
        );

        second.Value.IsEnabled.Should().BeFalse();
        bus.Published.OfType<ChannelConfigChangedEvent>().Should().HaveCount(2);
    }

    [Fact]
    public async Task An_invalid_channel_id_publishes_nothing()
    {
        (FeatureService sut, RecordingEventBus bus) = Build();

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync("not-a-guid", "custom_code");

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeatures_reports_the_four_decoration_keys_at_their_own_default_when_no_row_exists()
    {
        (FeatureService sut, _) = Build();

        Result<List<FeatureStatusDto>> result = await sut.GetFeaturesAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        Dictionary<string, bool> byKey = result.Value.ToDictionary(
            f => f.FeatureKey,
            f => f.IsEnabled
        );

        // Third-party emote providers default ON (the near-universal want); link preview defaults OFF (it makes
        // an outbound fetch); custom_code stays OFF (unchanged pre-existing behavior) — the bug this closes is
        // GetFeaturesAsync not returning these four keys AT ALL, so every channel's toggle was silently ignored.
        byKey.Should().Contain("use_7tv", true);
        byKey.Should().Contain("use_bttv", true);
        byKey.Should().Contain("use_ffz", true);
        byKey.Should().Contain("use_link_preview", false);
        byKey.Should().Contain("custom_code", false);
    }

    [Fact]
    public async Task GetFeatures_reports_an_explicit_row_state_over_the_key_default()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        db.ChannelFeatures.Add(
            new ChannelFeature
            {
                BroadcasterId = Channel,
                FeatureKey = "use_7tv",
                IsEnabled = false,
            }
        );
        await db.SaveChangesAsync();
        FeatureService sut = new(db, new FakeTimeProvider(Now), new RecordingEventBus());

        Result<List<FeatureStatusDto>> result = await sut.GetFeaturesAsync(Channel.ToString());

        result
            .Value.Single(f => f.FeatureKey == "use_7tv")
            .IsEnabled.Should()
            .BeFalse("an explicit disabled row overrides the default-ON catalogue state");
    }

    [Fact]
    public async Task Toggling_a_default_on_key_with_no_row_yet_disables_it_in_a_single_call()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        FeatureService sut = new(db, new FakeTimeProvider(Now), new RecordingEventBus());

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "use_7tv"
        );

        // ONE call from a channel that never touched this row must land it disabled — not enabled (which would
        // mean the channel needs a SECOND click to actually turn off a feature that was already on by default).
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();

        ChannelFeature? row = await db.ChannelFeatures.FirstOrDefaultAsync(f =>
            f.BroadcasterId == Channel && f.FeatureKey == "use_7tv"
        );
        row.Should().NotBeNull();
        row!.IsEnabled.Should().BeFalse();
        row.EnabledAt.Should().BeNull();
    }

    [Fact]
    public async Task Toggling_a_default_off_key_with_no_row_yet_enables_it_in_a_single_call()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        FeatureService sut = new(db, new FakeTimeProvider(Now), new RecordingEventBus());

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "use_link_preview"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();

        ChannelFeature? row = await db.ChannelFeatures.FirstOrDefaultAsync(f =>
            f.BroadcasterId == Channel && f.FeatureKey == "use_link_preview"
        );
        row.Should().NotBeNull();
        row!.IsEnabled.Should().BeTrue();
        row.EnabledAt.Should().Be(Now.UtcDateTime);
    }
}
