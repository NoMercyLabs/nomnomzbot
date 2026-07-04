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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves <see cref="FeatureService.ToggleFeatureAsync"/> publishes the E5 dashboard live-sync event after every
/// successful toggle so a second open dashboard's Features page refetches, and that an invalid channel id (the
/// only rejection path) never publishes.
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
}
