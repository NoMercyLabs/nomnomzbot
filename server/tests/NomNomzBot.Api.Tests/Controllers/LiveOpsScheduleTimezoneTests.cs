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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The schedule read carries the streamer's saved timezone (on the schedule and on every segment) so the
/// dashboard can render and seed in it, and every schedule write refuses a zone that is not a real IANA id
/// before anything is sent to Twitch.
/// </summary>
public sealed class LiveOpsScheduleTimezoneTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");

    private static (
        LiveOpsController Controller,
        ITwitchScheduleApi Schedule,
        IChannelService Channels
    ) Build()
    {
        ITwitchScheduleApi schedule = Substitute.For<ITwitchScheduleApi>();
        IChannelService channels = Substitute.For<IChannelService>();
        LiveOpsController controller = new(
            Substitute.For<ITwitchPollsApi>(),
            Substitute.For<ITwitchPredictionsApi>(),
            Substitute.For<ITwitchRaidsApi>(),
            Substitute.For<ITwitchAdsApi>(),
            Substitute.For<ITwitchClipsApi>(),
            schedule,
            Substitute.For<ITwitchStreamsApi>(),
            channels
        );
        return (controller, schedule, channels);
    }

    private static TwitchSchedule SampleSchedule() =>
        new(
            [
                new TwitchScheduleSegment(
                    "seg-1",
                    DateTimeOffset.Parse("2026-10-05T18:00:00Z"),
                    DateTimeOffset.Parse("2026-10-05T20:30:00Z"),
                    "Variety",
                    null,
                    null,
                    false
                ),
                new TwitchScheduleSegment(
                    "seg-2",
                    DateTimeOffset.Parse("2026-10-06T18:00:00Z"),
                    DateTimeOffset.Parse("2026-10-06T19:00:00Z"),
                    "Chill",
                    null,
                    null,
                    true
                ),
            ],
            "1",
            "Streamer",
            "streamer",
            null
        );

    private static ChannelBasicsDto Basics(string? timezone) =>
        new("!", null, "en", true, timezone);

    private static T Data<T>(IActionResult result) =>
        ((StatusResponseDto<T>)result.Should().BeOfType<OkObjectResult>().Subject.Value!).Data!;

    private static void AssertValidationFailure(IActionResult result)
    {
        BadRequestObjectResult bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        StatusResponseDto<object> body = bad
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Code.Should().Be("VALIDATION_FAILED");
        body.Message.Should().Contain("timezone");
    }

    [Fact]
    public async Task GetSchedule_stamps_the_saved_zone_on_the_schedule_and_every_segment()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, IChannelService channels) =
            Build();
        schedule
            .GetScheduleAsync(Channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleSchedule()));
        channels
            .GetBasicsAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Basics("Europe/Amsterdam")));

        IActionResult result = await controller.GetSchedule(Channel.ToString(), null, 0, default);

        TwitchSchedule body = Data<TwitchSchedule>(result);
        body.Timezone.Should().Be("Europe/Amsterdam");
        body.Segments.Should().HaveCount(2);
        body.Segments.Should().OnlyContain(s => s.Timezone == "Europe/Amsterdam");
        body.Segments[0].StartTime.Should().Be(DateTimeOffset.Parse("2026-10-05T18:00:00Z"));
    }

    [Fact]
    public async Task GetSchedule_leaves_the_zone_null_when_none_is_saved()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, IChannelService channels) =
            Build();
        schedule
            .GetScheduleAsync(Channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleSchedule()));
        channels
            .GetBasicsAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Basics(null)));

        IActionResult result = await controller.GetSchedule(Channel.ToString(), null, 0, default);

        TwitchSchedule body = Data<TwitchSchedule>(result);
        body.Timezone.Should().BeNull();
        body.Segments.Should().OnlyContain(s => s.Timezone == null);
    }

    [Fact]
    public async Task CreateSegment_with_an_invalid_zone_is_refused_and_nothing_goes_to_twitch()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, _) = Build();

        IActionResult result = await controller.CreateScheduleSegment(
            Channel.ToString(),
            new CreateScheduleSegmentRequest(
                DateTimeOffset.Parse("2026-10-05T18:00:00Z"),
                "Mars/Olympus",
                "60"
            ),
            default
        );

        AssertValidationFailure(result);
        await schedule.DidNotReceiveWithAnyArgs().CreateSegmentAsync(default, default!);
    }

    [Fact]
    public async Task CreateSegment_with_a_real_zone_reaches_twitch_unchanged()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, IChannelService channels) =
            Build();
        CreateScheduleSegmentRequest request = new(
            DateTimeOffset.Parse("2026-10-05T18:00:00Z"),
            "Europe/Amsterdam",
            "60"
        );
        schedule
            .CreateSegmentAsync(Channel, request, Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleSchedule()));
        channels
            .GetBasicsAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Basics("Europe/Amsterdam")));

        IActionResult result = await controller.CreateScheduleSegment(
            Channel.ToString(),
            request,
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await schedule
            .Received(1)
            .CreateSegmentAsync(Channel, request, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/AZone")]
    [InlineData("Amsterdam")]
    public async Task UpdateSegment_with_an_invalid_zone_is_refused_and_nothing_goes_to_twitch(
        string zone
    )
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, _) = Build();

        IActionResult result = await controller.UpdateScheduleSegment(
            Channel.ToString(),
            "seg-1",
            new UpdateScheduleSegmentRequest(Title: "x", Timezone: zone),
            default
        );

        AssertValidationFailure(result);
        await schedule.DidNotReceiveWithAnyArgs().UpdateSegmentAsync(default, default!, default!);
    }

    [Fact]
    public async Task UpdateSegment_without_a_zone_is_a_title_only_edit_and_reaches_twitch()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, IChannelService channels) =
            Build();
        UpdateScheduleSegmentRequest request = new(Title: "New title");
        schedule
            .UpdateSegmentAsync(Channel, "seg-1", request, Arg.Any<CancellationToken>())
            .Returns(Result.Success(SampleSchedule()));
        channels
            .GetBasicsAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Basics(null)));

        IActionResult result = await controller.UpdateScheduleSegment(
            Channel.ToString(),
            "seg-1",
            request,
            default
        );

        result.Should().BeOfType<OkObjectResult>();
        await schedule
            .Received(1)
            .UpdateSegmentAsync(Channel, "seg-1", request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateSettings_with_an_invalid_zone_is_refused_and_nothing_goes_to_twitch()
    {
        (LiveOpsController controller, ITwitchScheduleApi schedule, _) = Build();

        IActionResult result = await controller.UpdateScheduleSettings(
            Channel.ToString(),
            new LiveOpsController.UpdateScheduleSettingsDto(true, null, null, "Nowhere/Land"),
            default
        );

        AssertValidationFailure(result);
        await schedule
            .DidNotReceiveWithAnyArgs()
            .UpdateScheduleSettingsAsync(default, default, default, default, default);
    }

    [Theory]
    [InlineData("Europe/Amsterdam", true)]
    [InlineData("America/New_York", true)]
    [InlineData("UTC", true)]
    [InlineData("Mars/Olympus", false)]
    [InlineData("Amsterdam", false)]
    [InlineData("W. Europe Standard Time", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_accepts_only_real_iana_ids(string? zone, bool expected)
    {
        ScheduleTimezone.IsValid(zone).Should().Be(expected);
    }
}
