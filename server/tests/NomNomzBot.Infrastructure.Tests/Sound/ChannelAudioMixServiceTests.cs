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
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Sound;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// The channel audio mix is stored on the bot so every streaming PC plays the same balance. A channel with no
/// row plays at 100/100, a write is clamped to 0..100, and one channel's mix never reaches another channel.
/// </summary>
public sealed class ChannelAudioMixServiceTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192c000-0000-7000-8000-00000000e001");
    private static readonly Guid ChannelB = Guid.Parse("0192c000-0000-7000-8000-00000000e002");

    [Fact]
    public async Task A_channel_with_no_row_gets_the_defaults_and_no_row_is_written()
    {
        await using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ChannelAudioMixService service = new(db);

        Result<ChannelAudioMixDto> mix = await service.GetAsync(ChannelA);

        mix.IsSuccess.Should().BeTrue(mix.ErrorMessage);
        mix.Value.MasterVolume.Should().Be(100);
        mix.Value.TtsVolume.Should().Be(100);
        (await db.ChannelAudioMixes.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_update_is_clamped_to_zero_through_one_hundred_and_persisted()
    {
        await using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ChannelAudioMixService service = new(db);

        Result<ChannelAudioMixDto> updated = await service.UpdateAsync(
            ChannelA,
            new UpdateChannelAudioMixRequest(150, -20)
        );

        updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
        updated.Value.Should().Be(new ChannelAudioMixDto(100, 0));
        ChannelAudioMix row = await db.ChannelAudioMixes.SingleAsync();
        row.BroadcasterId.Should().Be(ChannelA);
        row.MasterVolume.Should().Be(100);
        row.TtsVolume.Should().Be(0);
    }

    [Fact]
    public async Task A_second_update_changes_the_same_row()
    {
        await using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ChannelAudioMixService service = new(db);

        await service.UpdateAsync(ChannelA, new(50, 60));
        await service.UpdateAsync(ChannelA, new(70, 80));

        ChannelAudioMix row = await db.ChannelAudioMixes.SingleAsync();
        row.MasterVolume.Should().Be(70);
        row.TtsVolume.Should().Be(80);
    }

    [Fact]
    public async Task One_channels_mix_does_not_change_another_channels_mix()
    {
        await using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        ChannelAudioMixService service = new(db);

        await service.UpdateAsync(ChannelA, new(40, 30));
        await service.UpdateAsync(ChannelB, new(90, 10));

        (await service.GetAsync(ChannelA)).Value.Should().Be(new ChannelAudioMixDto(40, 30));
        (await service.GetAsync(ChannelB)).Value.Should().Be(new ChannelAudioMixDto(90, 10));
    }

    [Theory]
    [InlineData(50, 80, 40)]
    [InlineData(50, 100, 50)]
    [InlineData(100, 80, 80)]
    [InlineData(0, 80, 0)]
    public void The_master_volume_scales_a_clip_volume(int master, int clip, int expected) =>
        new ChannelAudioMixDto(master, 100).ApplyToClipVolume(clip).Should().Be(expected);

    [Theory]
    [InlineData(100, 60, 0.6)]
    [InlineData(50, 60, 0.3)]
    [InlineData(100, 100, 1.0)]
    [InlineData(0, 100, 0.0)]
    public void The_master_and_tts_volumes_multiply_into_a_zero_to_one_gain(
        int master,
        int tts,
        double expected
    ) =>
        new ChannelAudioMixDto(master, tts)
            .TtsPlaybackVolume.Should()
            .BeApproximately(expected, 1e-9);
}
