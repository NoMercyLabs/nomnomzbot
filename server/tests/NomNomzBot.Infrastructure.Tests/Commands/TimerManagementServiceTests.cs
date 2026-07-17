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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves <see cref="TimerManagementService"/> publishes the E5 dashboard live-sync event after every successful
/// create/update/delete/toggle so a second open dashboard's Timers page refetches, and that a rejected mutation
/// (duplicate name, unknown timer) never publishes.
/// </summary>
public sealed class TimerManagementServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");

    private static (TimerManagementService Sut, RecordingEventBus Bus) Build()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        RecordingEventBus bus = new();
        return (new TimerManagementService(db, bus, Billing.TestTiers.Unlimited()), bus);
    }

    private static CreateTimerDto Req(string name = "greeting") =>
        new() { Name = name, Messages = ["hi"] };

    [Fact]
    public async Task Create_publishes_ChannelConfigChangedEvent_for_the_timers_domain()
    {
        (TimerManagementService sut, RecordingEventBus bus) = Build();

        TimerDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel
                && e.Domain == "timers"
                && e.EntityId == created.Id.ToString()
                && e.Action == "created"
            );
    }

    [Fact]
    public async Task Create_of_a_duplicate_name_publishes_nothing()
    {
        (TimerManagementService sut, RecordingEventBus bus) = Build();
        await sut.CreateAsync(Channel.ToString(), Req());
        bus.Published.Clear();

        Result<TimerDto> result = await sut.CreateAsync(Channel.ToString(), Req());

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_publishes_an_updated_action()
    {
        (TimerManagementService sut, RecordingEventBus bus) = Build();
        TimerDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;
        bus.Published.Clear();

        await sut.UpdateAsync(
            Channel.ToString(),
            created.Id,
            new UpdateTimerDto { IntervalMinutes = 45 }
        );

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.Domain == "timers" && e.EntityId == created.Id.ToString() && e.Action == "updated"
            );
    }

    [Fact]
    public async Task FireOnce_round_trips_through_create_get_and_update()
    {
        (TimerManagementService sut, RecordingEventBus _) = Build();

        // Created as a one-shot: the flag persists and is reflected back.
        TimerDto created = (
            await sut.CreateAsync(
                Channel.ToString(),
                new CreateTimerDto
                {
                    Name = "welcome-once",
                    Messages = ["hi"],
                    FireOnce = true,
                }
            )
        ).Value;
        created.FireOnce.Should().BeTrue();

        TimerDto fetched = (await sut.GetAsync(Channel.ToString(), created.Id)).Value;
        fetched.FireOnce.Should().BeTrue("the persisted timer is a one-shot");

        // Flipped back to a loop via update; other fields (name) untouched.
        TimerDto updated = (
            await sut.UpdateAsync(
                Channel.ToString(),
                created.Id,
                new UpdateTimerDto { FireOnce = false }
            )
        ).Value;
        updated.FireOnce.Should().BeFalse("update cleared one-shot mode");
        updated.Name.Should().Be("welcome-once", "an unset field is left as-is");
    }

    [Fact]
    public async Task Update_of_an_unknown_timer_publishes_nothing()
    {
        (TimerManagementService sut, RecordingEventBus bus) = Build();

        Result<TimerDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            Guid.CreateVersion7(),
            new UpdateTimerDto()
        );

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Toggle_publishes_a_toggled_action()
    {
        (TimerManagementService sut, RecordingEventBus bus) = Build();
        TimerDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;
        bus.Published.Clear();

        await sut.ToggleAsync(Channel.ToString(), created.Id);

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.Domain == "timers" && e.EntityId == created.Id.ToString() && e.Action == "toggled"
            );
    }

    [Fact]
    public async Task Delete_publishes_a_deleted_action()
    {
        (TimerManagementService sut, RecordingEventBus bus) = Build();
        TimerDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;
        bus.Published.Clear();

        (await sut.DeleteAsync(Channel.ToString(), created.Id)).IsSuccess.Should().BeTrue();

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.Domain == "timers" && e.EntityId == created.Id.ToString() && e.Action == "deleted"
            );
    }

    [Fact]
    public async Task Delete_of_an_unknown_timer_publishes_nothing()
    {
        (TimerManagementService sut, RecordingEventBus bus) = Build();

        Result result = await sut.DeleteAsync(Channel.ToString(), Guid.CreateVersion7());

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }
}
