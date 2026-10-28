// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Domain.Tests.Commands;

public sealed class ReplyLookTests
{
    [Fact]
    public void A_look_is_written_with_lowercase_token_names()
    {
        ReplyDelivery delivery = new(
            ReplyTarget.Overlay,
            false,
            new(ReplyHighlight.Warning, ReplySize.Large, 20)
        );

        string json = JsonSerializer.Serialize(delivery, JsonSerializerOptions.Web);

        json.Should()
            .Contain(
                "\"look\":{\"highlight\":\"warning\",\"size\":\"large\",\"durationSeconds\":20}"
            );
    }

    [Fact]
    public void A_look_reads_back_the_same_value()
    {
        ReplyDelivery delivery = new(
            ReplyTarget.Both,
            true,
            new(ReplyHighlight.Destructive, ReplySize.Normal, 5)
        );

        string json = JsonSerializer.Serialize(delivery, JsonSerializerOptions.Web);
        ReplyDelivery? back = JsonSerializer.Deserialize<ReplyDelivery>(
            json,
            JsonSerializerOptions.Web
        );

        back.Should().Be(delivery);
    }

    [Fact]
    public void A_delivery_without_a_look_keeps_the_stored_json_of_older_rows()
    {
        string json = JsonSerializer.Serialize(
            new ReplyDelivery(ReplyTarget.Overlay, true),
            JsonSerializerOptions.Web
        );

        json.Should().Be("{\"target\":\"overlay\",\"html\":true}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(121)]
    public void A_duration_outside_one_to_one_hundred_twenty_seconds_is_a_problem(int seconds)
    {
        ReplyLook look = new(DurationSeconds: seconds);

        look.Problem().Should().Contain("between 1 and 120 seconds");
        new ReplyDelivery(ReplyTarget.Overlay, false, look).Problem().Should().Be(look.Problem());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    public void The_duration_bounds_themselves_are_storable(int seconds)
    {
        new ReplyLook(DurationSeconds: seconds).Problem().Should().BeNull();
    }

    [Fact]
    public void A_look_that_chose_nothing_is_dropped_from_storage()
    {
        ReplyDelivery? stored = ReplyDelivery.ForStorage(
            new(ReplyTarget.Overlay, false, ReplyLook.Default)
        );

        stored.Should().Be(new ReplyDelivery(ReplyTarget.Overlay));
        stored!.Look.Should().BeNull();
    }

    [Fact]
    public void A_look_that_changes_the_line_is_kept_in_storage()
    {
        ReplyLook look = new(ReplyHighlight.Success);

        ReplyDelivery? stored = ReplyDelivery.ForStorage(new(ReplyTarget.Both, false, look));

        stored!.Look.Should().Be(look);
        look.HighlightToken.Should().Be("success");
        look.SizeToken.Should().Be("normal");
    }
}
