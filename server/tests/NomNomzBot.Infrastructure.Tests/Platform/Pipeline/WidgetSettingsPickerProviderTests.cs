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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Obs.Pipeline;
using NomNomzBot.Infrastructure.Rewards.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// The two picker kinds a widget settings field can name: <c>twitch_reward</c> offers the Twitch reward id a widget
/// event carries, and <c>obs_audio_input</c> offers only the audio inputs of the connected OBS.
/// </summary>
public sealed class WidgetSettingsPickerProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();
    private static readonly PaginationParams DefaultPage = new(Page: 1, PageSize: 25);

    private static PipelineOptionQuery Search(string? term) => new(term, null, DefaultPage);

    // ── twitch_reward ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Twitch_reward_option_value_is_the_twitch_id_not_the_internal_id()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        Reward reward = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Title = "Crash the stream",
            Cost = 1000,
            IsEnabled = true,
            TwitchRewardId = "9f1c2b6e-0000-4aaa-bbbb-123456789abc",
        };
        db.Rewards.Add(reward);
        await db.SaveChangesAsync();

        Result<PipelineOptionListResult> result = await new TwitchRewardOptionProvider(
            db
        ).GetOptionsAsync(Broadcaster, Search(null));

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("9f1c2b6e-0000-4aaa-bbbb-123456789abc", option.Value);
        Assert.NotEqual(reward.Id.ToString(), option.Value);
        Assert.Equal("Crash the stream", option.Label);
        Assert.Equal("1000 points · active", option.SecondaryText);
    }

    [Fact]
    public async Task Twitch_reward_skips_unsynced_and_other_channels_rewards_and_resolves_a_saved_value()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.Rewards.AddRange(
            new Reward
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Title = "Not synced yet",
                IsEnabled = true,
                TwitchRewardId = null,
            },
            new Reward
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Guid.NewGuid(),
                Title = "Someone else's",
                IsEnabled = true,
                TwitchRewardId = "other-channel",
            },
            new Reward
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Title = "Mine",
                IsEnabled = true,
                TwitchRewardId = "mine-1",
            }
        );
        await db.SaveChangesAsync();
        TwitchRewardOptionProvider provider = new(db);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            Search(null)
        );
        Result<PipelineOptionListResult> lookup = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(null, "mine-1", DefaultPage)
        );

        Assert.Equal("Mine", Assert.Single(all.Value.Items).Label);
        Assert.Equal("mine-1", Assert.Single(lookup.Value.Items).Value);
    }

    // ── obs_audio_input ───────────────────────────────────────────────────────

    private static IObsControlService ObsWith(params ObsInputDto[] inputs)
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetInputsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ObsInputDto>>(inputs));
        return obs;
    }

    [Fact]
    public async Task Obs_audio_input_lists_only_inputs_that_have_a_mute_state()
    {
        IObsControlService obs = ObsWith(
            new ObsInputDto("Mic", "wasapi_input_capture", Muted: false, VolumeDb: -3),
            new ObsInputDto("Desktop", "wasapi_output_capture", Muted: true, VolumeDb: 0),
            new ObsInputDto("Overlay", "browser_source", Muted: null, VolumeDb: null)
        );

        Result<PipelineOptionListResult> result = await new ObsAudioInputOptionProvider(
            obs
        ).GetOptionsAsync(Broadcaster, Search(null));

        Assert.True(result.Value.SourceAvailable);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(["Desktop", "Mic"], result.Value.Items.Select(i => i.Value));
        Assert.Equal("muted", result.Value.Items[0].SecondaryText);
        Assert.Equal("live", result.Value.Items[1].SecondaryText);
    }

    [Fact]
    public async Task Obs_audio_input_search_and_saved_value_narrow_the_list()
    {
        IObsControlService obs = ObsWith(
            new ObsInputDto("Mic", "k", Muted: false, VolumeDb: 0),
            new ObsInputDto("Mic Backup", "k", Muted: false, VolumeDb: 0),
            new ObsInputDto("Desktop", "k", Muted: false, VolumeDb: 0)
        );
        ObsAudioInputOptionProvider provider = new(obs);

        Result<PipelineOptionListResult> search = await provider.GetOptionsAsync(
            Broadcaster,
            Search("mic")
        );
        Result<PipelineOptionListResult> lookup = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(null, "Mic", DefaultPage)
        );

        Assert.Equal(["Mic", "Mic Backup"], search.Value.Items.Select(i => i.Value));
        Assert.Equal("Mic", Assert.Single(lookup.Value.Items).Value);
    }

    [Fact]
    public async Task Obs_audio_input_is_unavailable_with_the_reason_when_obs_is_not_connected()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetInputsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsInputDto>>(
                    "OBS is not connected.",
                    "OBS_NOT_CONNECTED"
                )
            );

        Result<PipelineOptionListResult> result = await new ObsAudioInputOptionProvider(
            obs
        ).GetOptionsAsync(Broadcaster, Search(null));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SourceAvailable);
        Assert.Equal("OBS is not connected.", result.Value.UnavailableReason);
        Assert.Empty(result.Value.Items);
    }
}
