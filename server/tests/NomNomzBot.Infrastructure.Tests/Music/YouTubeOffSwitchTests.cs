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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Music.Playback;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Only a saved "YouTube off" counts as off; a channel that never chose stays as it was.</summary>
public sealed class YouTubeOffSwitchTests
{
    private const string Broadcaster = "0192a000-0000-7000-8000-00000000a771";

    [Fact]
    public async Task A_channel_with_no_saved_config_is_not_switched_off()
    {
        using MusicConfigDbFixture fixture = new();

        (await fixture.Service.IsYouTubeSwitchedOffAsync(Broadcaster)).Should().BeFalse();
    }

    [Fact]
    public async Task A_saved_false_is_off_and_turning_it_back_on_ends_that()
    {
        using MusicConfigDbFixture fixture = new();

        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto { AllowYouTube = false }
        );
        (await fixture.Service.IsYouTubeSwitchedOffAsync(Broadcaster)).Should().BeTrue();

        await fixture.Service.UpdateConfigAsync(
            Broadcaster,
            new UpdateMusicConfigDto { AllowYouTube = true }
        );
        (await fixture.Service.IsYouTubeSwitchedOffAsync(Broadcaster)).Should().BeFalse();
    }

    [Fact]
    public async Task The_director_side_reads_the_scoped_config_service()
    {
        Guid channel = Guid.Parse(Broadcaster);
        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        config.IsYouTubeSwitchedOffAsync(Broadcaster, Arg.Any<CancellationToken>()).Returns(true);

        (await SwitchOver(config).IsOffAsync(channel)).Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_read_counts_as_not_off()
    {
        Guid channel = Guid.Parse(Broadcaster);
        IMusicConfigService config = Substitute.For<IMusicConfigService>();
        config
            .IsYouTubeSwitchedOffAsync(Broadcaster, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("made-up database failure"));

        (await SwitchOver(config).IsOffAsync(channel)).Should().BeFalse();
    }

    private static YouTubeOffSwitch SwitchOver(IMusicConfigService config)
    {
        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => config)
            .BuildServiceProvider();

        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<YouTubeOffSwitch>.Instance
        );
    }
}
