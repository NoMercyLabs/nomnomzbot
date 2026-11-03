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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Music.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>
/// The music pickers (playback device, track search). A channel with no music provider, or one whose provider
/// connection is broken, gets an unavailable list with the reason, never an empty one.
/// </summary>
public sealed class MusicPickerProviderTests
{
    private static readonly string Broadcaster = Guid.NewGuid().ToString();
    private static readonly Guid BroadcasterId = Guid.Parse(Broadcaster);

    private static IMusicService Spotify()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns("spotify");
        music
            .GetActiveProviderAuthStatusAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        return music;
    }

    [Fact]
    public void Each_provider_serves_its_own_kind()
    {
        IMusicService music = Substitute.For<IMusicService>();

        Assert.Equal(
            PipelineActionFieldKind.MusicDevice,
            new MusicDeviceOptionProvider(music).Kind
        );
        Assert.Equal(PipelineActionFieldKind.MusicTrack, new MusicTrackOptionProvider(music).Kind);
    }

    [Fact]
    public async Task Device_lists_the_playback_devices_with_the_device_id_saved()
    {
        IMusicService music = Spotify();
        music
            .GetDevicesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns([
                new("d-2", "Phone", "Smartphone", false, 40),
                new("d-1", "Desk", "Computer", true, 70),
            ]);

        Result<PipelineOptionListResult> result = await new MusicDeviceOptionProvider(
            music
        ).GetOptionsAsync(BroadcasterId, PickerAsk.Of());

        Assert.Equal(["d-1", "d-2"], PickerAsk.Values(result));
        Assert.Equal("Desk", result.Value.Items[0].Label);
        Assert.Equal("Computer, active", result.Value.Items[0].SecondaryText);
        Assert.Equal("Smartphone", result.Value.Items[1].SecondaryText);
    }

    [Fact]
    public async Task Device_search_value_lookup_and_paging_narrow_the_list()
    {
        IMusicService music = Spotify();
        music
            .GetDevicesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns([
                new("d-2", "Phone", "Smartphone", false, 40),
                new("d-1", "Desk", "Computer", true, 70),
            ]);
        MusicDeviceOptionProvider provider = new(music);

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            BroadcasterId,
            PickerAsk.Of(search: "pho")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            BroadcasterId,
            PickerAsk.Of(value: "d-1")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            BroadcasterId,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );

        Assert.Equal(["d-2"], PickerAsk.Values(searched));
        Assert.Equal(["d-1"], PickerAsk.Values(looked));
        Assert.Equal(["d-2"], PickerAsk.Values(second));
        Assert.Equal(2, second.Value.TotalCount);
    }

    [Fact]
    public async Task No_connected_music_provider_is_unavailable_with_a_reason_for_both_kinds()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns((string?)null);
        const string reason = "No music provider is connected.";

        PickerAsk.AssertUnavailable(
            await new MusicDeviceOptionProvider(music).GetOptionsAsync(
                BroadcasterId,
                PickerAsk.Of()
            ),
            reason
        );
        PickerAsk.AssertUnavailable(
            await new MusicTrackOptionProvider(music).GetOptionsAsync(
                BroadcasterId,
                PickerAsk.Of(search: "song")
            ),
            reason
        );
        await music
            .DidNotReceive()
            .GetDevicesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("needs_reauth", "The music provider connection has expired. Connect it again.")]
    [InlineData("forbidden", "The music provider refused access. Check the granted permissions.")]
    public async Task A_broken_provider_connection_is_unavailable_with_its_own_reason(
        string status,
        string reason
    )
    {
        IMusicService music = Spotify();
        music
            .GetActiveProviderAuthStatusAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(status);

        PickerAsk.AssertUnavailable(
            await new MusicDeviceOptionProvider(music).GetOptionsAsync(
                BroadcasterId,
                PickerAsk.Of()
            ),
            reason
        );
    }

    [Fact]
    public async Task Track_search_returns_the_uri_as_the_saved_value_with_artist_and_cover()
    {
        IMusicService music = Spotify();
        music
            .SearchAsync(Broadcaster, "intro", 25, Arg.Any<CancellationToken>())
            .Returns([
                new(
                    "spotify:track:abc",
                    "Intro Song",
                    "The Band",
                    "First Album",
                    "https://img/abc.jpg",
                    180000,
                    "spotify"
                ),
            ]);

        Result<PipelineOptionListResult> result = await new MusicTrackOptionProvider(
            music
        ).GetOptionsAsync(BroadcasterId, PickerAsk.Of(search: "intro"));

        PipelineOption track = Assert.Single(result.Value.Items);
        Assert.Equal("spotify:track:abc", track.Value);
        Assert.Equal("Intro Song", track.Label);
        Assert.Equal("The Band - First Album", track.SecondaryText);
        Assert.Equal("https://img/abc.jpg", track.ImageUrl);
    }

    [Fact]
    public async Task Track_search_pages_through_the_results_the_source_returned()
    {
        IMusicService music = Spotify();
        music
            .SearchAsync(Broadcaster, "x", 4, Arg.Any<CancellationToken>())
            .Returns([
                new("u1", "One", "A", null, null, 1, "spotify"),
                new("u2", "Two", "A", null, null, 1, "spotify"),
                new("u3", "Three", "A", null, null, 1, "spotify"),
                new("u4", "Four", "A", null, null, 1, "spotify"),
            ]);

        Result<PipelineOptionListResult> result = await new MusicTrackOptionProvider(
            music
        ).GetOptionsAsync(
            BroadcasterId,
            PickerAsk.Of(search: "x", page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(["u3", "u4"], PickerAsk.Values(result));
        Assert.Equal("A", result.Value.Items[0].SecondaryText);
        Assert.Equal(4, result.Value.TotalCount);
    }

    [Fact]
    public async Task Track_with_no_search_text_is_an_available_empty_list_and_asks_the_source_nothing()
    {
        IMusicService music = Spotify();

        Result<PipelineOptionListResult> result = await new MusicTrackOptionProvider(
            music
        ).GetOptionsAsync(BroadcasterId, PickerAsk.Of());

        Assert.True(result.Value.SourceAvailable);
        Assert.Empty(result.Value.Items);
        await music
            .DidNotReceive()
            .SearchAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Another_channels_devices_never_appear()
    {
        IMusicService music = Spotify();
        string other = Guid.NewGuid().ToString();
        music
            .GetDevicesAsync(other, Arg.Any<CancellationToken>())
            .Returns([new("other-device", "Other", "Computer", false, 10)]);
        music
            .GetDevicesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns([new("mine", "Mine", "Computer", false, 10)]);

        Result<PipelineOptionListResult> result = await new MusicDeviceOptionProvider(
            music
        ).GetOptionsAsync(BroadcasterId, PickerAsk.Of());

        Assert.Equal(["mine"], PickerAsk.Values(result));
    }
}
