// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>The combined stream-info PUT sets the stream language on the platform with the rest of the info.</summary>
public sealed class StreamControllerUpdateStreamInfoTests
{
    private static readonly Guid Channel = Guid.Parse("019f2d00-2222-7000-8000-000000000001");

    private readonly IChannelService _channelService = Substitute.For<IChannelService>();
    private readonly IPlatformChannelApi _platformApi = Substitute.For<IPlatformChannelApi>();
    private readonly List<PlatformStreamInfoUpdate> _sent = [];

    private StreamController Build()
    {
        ChannelDto channel = new(
            Channel.ToString(),
            "stoney_eagle",
            "Stoney_Eagle",
            null,
            false,
            true,
            "Old title",
            "Old game",
            null,
            null,
            "free",
            "en",
            DateTime.UtcNow
        );
        _channelService
            .GetAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result<ChannelDto>.Success(channel));
        _platformApi
            .UpdateStreamInfoAsync(
                Channel,
                Arg.Do<PlatformStreamInfoUpdate>(_sent.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<PlatformStreamInfoApplied>.Success(new("New title", "New game", null)));

        return new(
            _channelService,
            Substitute.For<IChannelRegistry>(),
            Substitute.For<ITwitchChannelsApi>(),
            Substitute.For<ITwitchStreamsApi>(),
            Substitute.For<ITwitchSearchApi>(),
            _platformApi,
            Substitute.For<IApplicationDbContext>()
        );
    }

    [Fact]
    public async Task A_language_is_trimmed_lower_cased_sent_to_the_platform_and_returned()
    {
        StreamController controller = Build();

        IActionResult result = await controller.UpdateStreamInfo(
            Channel.ToString(),
            new("New title", "New game", ["coding"], Language: "  NL "),
            CancellationToken.None
        );

        _sent
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new PlatformStreamInfoUpdate(
                    Title: "New title",
                    CategoryName: "New game",
                    Tags: ["coding"],
                    ContentLabels: null,
                    IsBrandedContent: null,
                    Language: "nl"
                )
            );
        result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<StreamController.StreamInfoDto>>()
            .Which.Data!.Language.Should()
            .Be("nl");
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("bg")]
    [InlineData("ca")]
    [InlineData("cs")]
    [InlineData("da")]
    [InlineData("de")]
    [InlineData("el")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fi")]
    [InlineData("fr")]
    [InlineData("hi")]
    [InlineData("hu")]
    [InlineData("id")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("ms")]
    [InlineData("nl")]
    [InlineData("no")]
    [InlineData("pl")]
    [InlineData("pt")]
    [InlineData("ro")]
    [InlineData("ru")]
    [InlineData("sk")]
    [InlineData("sv")]
    [InlineData("th")]
    [InlineData("tl")]
    [InlineData("tr")]
    [InlineData("uk")]
    [InlineData("vi")]
    [InlineData("zh")]
    public async Task Every_language_the_dashboard_offers_is_accepted(string language)
    {
        StreamController controller = Build();

        await controller.UpdateStreamInfo(
            Channel.ToString(),
            new(null, null, null, Language: language),
            CancellationToken.None
        );

        _sent.Should().ContainSingle().Which.Language.Should().Be(language);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("asl")]
    [InlineData("zh-hk")]
    public async Task The_Twitch_only_values_are_accepted(string language)
    {
        StreamController controller = Build();

        await controller.UpdateStreamInfo(
            Channel.ToString(),
            new(null, null, null, Language: language),
            CancellationToken.None
        );

        _sent.Should().ContainSingle().Which.Language.Should().Be(language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_language_leaves_the_language_unchanged(string? blank)
    {
        StreamController controller = Build();

        IActionResult result = await controller.UpdateStreamInfo(
            Channel.ToString(),
            new("New title", null, null, Language: blank),
            CancellationToken.None
        );

        _sent.Should().ContainSingle().Which.Language.Should().BeNull();
        result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<StreamController.StreamInfoDto>>()
            .Which.Data!.Language.Should()
            .BeNull();
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("e")]
    [InlineData("e1")]
    [InlineData("Others")]
    [InlineData("zz")]
    [InlineData("zh-tw")]
    public async Task A_bad_language_is_a_400_before_any_platform_call(string language)
    {
        StreamController controller = Build();

        IActionResult result = await controller.UpdateStreamInfo(
            Channel.ToString(),
            new("New title", null, null, Language: language),
            CancellationToken.None
        );

        result.Should().BeOfType<BadRequestObjectResult>();
        _sent.Should().BeEmpty();
        await _platformApi.DidNotReceiveWithAnyArgs().UpdateStreamInfoAsync(default, default!);
    }
}
