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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Infrastructure.Commands;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A command's reply delivery is stored as one JSON column and read back through the production context:
/// what a streamer saves is what the bot reads, and a command saved before the setting existed reads as chat.
/// </summary>
public sealed class CommandReplyDeliveryPersistenceTests : IDisposable
{
    private static readonly string Channel = RealCommandsDb.Channel.ToString();

    private readonly RealCommandsDb _real = new();
    private CommandService Commands => _real.Commands;

    public void Dispose() => _real.Dispose();

    private async Task<string?> StoredJsonAsync(string name)
    {
        List<string?> rows = await _real
            .Db.Database.SqlQuery<string?>(
                $"SELECT ReplyDeliveryJson AS Value FROM Commands WHERE Name = {name}"
            )
            .ToListAsync();
        return rows.Single();
    }

    [Fact]
    public async Task An_overlay_html_delivery_is_stored_as_json_and_read_back_equal()
    {
        await Commands.CreateAsync(
            Channel,
            new CreateCommandDto
            {
                Name = "train",
                TemplateResponse = "<marquee>CHOO CHOO {user}</marquee>",
                ReplyDelivery = new ReplyDelivery(ReplyTarget.Overlay, Html: true),
            }
        );
        _real.Db.ChangeTracker.Clear();

        (await StoredJsonAsync("train")).Should().Be("{\"target\":\"overlay\",\"html\":true}");
        CommandDto read = (await Commands.GetAsync(Channel, "train")).Value;
        read.ReplyDelivery.Should().Be(new ReplyDelivery(ReplyTarget.Overlay, Html: true));
    }

    [Fact]
    public async Task A_look_is_stored_inside_the_delivery_json_and_read_back_equal()
    {
        ReplyLook look = new(ReplyHighlight.Warning, ReplySize.Large, DurationSeconds: 25);
        await Commands.CreateAsync(
            Channel,
            new CreateCommandDto
            {
                Name = "train",
                TemplateResponse = "choo",
                ReplyDelivery = new ReplyDelivery(ReplyTarget.Overlay, Html: false, Look: look),
            }
        );
        _real.Db.ChangeTracker.Clear();

        (await StoredJsonAsync("train"))
            .Should()
            .Be(
                "{\"target\":\"overlay\",\"html\":false,\"look\":{\"highlight\":\"warning\",\"size\":\"large\",\"durationSeconds\":25}}"
            );
        (await Commands.GetAsync(Channel, "train"))
            .Value.ReplyDelivery.Should()
            .Be(new ReplyDelivery(ReplyTarget.Overlay, Html: false, Look: look));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(121)]
    public async Task A_look_with_a_duration_outside_the_allowed_range_is_refused_on_create_and_update(
        int seconds
    )
    {
        ReplyDelivery bad = new(ReplyTarget.Overlay, Look: new ReplyLook(DurationSeconds: seconds));
        await Commands.CreateAsync(
            Channel,
            new CreateCommandDto { Name = "train", TemplateResponse = "choo" }
        );

        Result<CommandDto> created = await Commands.CreateAsync(
            Channel,
            new CreateCommandDto
            {
                Name = "other",
                TemplateResponse = "choo",
                ReplyDelivery = bad,
            }
        );
        Result<CommandDto> updated = await Commands.UpdateAsync(
            Channel,
            "train",
            new UpdateCommandDto { ReplyDelivery = bad }
        );
        _real.Db.ChangeTracker.Clear();

        created.IsFailure.Should().BeTrue();
        updated.IsFailure.Should().BeTrue();
        (await Commands.GetAsync(Channel, "other")).IsFailure.Should().BeTrue();
        (await StoredJsonAsync("train")).Should().BeNull();
    }

    [Fact]
    public async Task A_command_saved_without_a_delivery_stores_null_and_reads_as_none_set()
    {
        await Commands.CreateAsync(
            Channel,
            new CreateCommandDto { Name = "plain", TemplateResponse = "hi" }
        );
        _real.Db.ChangeTracker.Clear();

        (await StoredJsonAsync("plain")).Should().BeNull();
        (await Commands.GetAsync(Channel, "plain")).Value.ReplyDelivery.Should().BeNull();
    }

    [Fact]
    public async Task An_update_changes_the_delivery_and_an_update_without_one_keeps_it()
    {
        await Commands.CreateAsync(
            Channel,
            new CreateCommandDto { Name = "train", TemplateResponse = "choo" }
        );

        await Commands.UpdateAsync(
            Channel,
            "train",
            new UpdateCommandDto { ReplyDelivery = new ReplyDelivery(ReplyTarget.Both) }
        );
        await Commands.UpdateAsync(Channel, "train", new UpdateCommandDto { IsEnabled = true });
        _real.Db.ChangeTracker.Clear();

        (await StoredJsonAsync("train")).Should().Be("{\"target\":\"both\",\"html\":false}");
        (await Commands.GetAsync(Channel, "train"))
            .Value.ReplyDelivery.Should()
            .Be(new ReplyDelivery(ReplyTarget.Both));
    }

    [Fact]
    public async Task The_list_item_carries_the_delivery()
    {
        await Commands.CreateAsync(
            Channel,
            new CreateCommandDto
            {
                Name = "train",
                TemplateResponse = "choo",
                ReplyDelivery = new ReplyDelivery(ReplyTarget.None),
            }
        );
        _real.Db.ChangeTracker.Clear();

        PagedList<CommandListItem> page = (
            await Commands.ListAsync(Channel, new PaginationParams())
        ).Value;

        page.Items.Single(i => i.Name == "train")
            .ReplyDelivery.Should()
            .Be(new ReplyDelivery(ReplyTarget.None));
    }
}
