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
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Infrastructure.Obs.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs.PipelineOptions;

/// <summary>
/// The OBS name pickers: scene, scene item, filter, transition, input and hotkey. Each one lists what the channel's
/// connected OBS reports, says why when OBS is not connected, and narrows by the sibling field the step saved.
/// </summary>
public sealed class ObsNamePickerProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();
    private static readonly PaginationParams FirstPage = new(Page: 1, PageSize: 25);

    private static PipelineOptionQuery Ask(
        string? search = null,
        string? value = null,
        PaginationParams? page = null,
        params (string Name, string Value)[] context
    ) =>
        new(
            search,
            value,
            page ?? FirstPage,
            context.Length == 0 ? null : context.ToDictionary(c => c.Name, c => c.Value)
        );

    private static Result<IReadOnlyList<T>> Ok<T>(params T[] items) =>
        Result.Success<IReadOnlyList<T>>(items);

    private static Result<IReadOnlyList<T>> NotConnected<T>() =>
        Result.Failure<IReadOnlyList<T>>("OBS is not connected.", "OBS_NOT_CONNECTED");

    private static void AssertUnavailable(Result<PipelineOptionListResult> result)
    {
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SourceAvailable);
        Assert.Equal("OBS is not connected.", result.Value.UnavailableReason);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    private static string[] Values(Result<PipelineOptionListResult> result) =>
        [.. result.Value.Items.Select(i => i.Value)];

    // ── kinds ────────────────────────────────────────────────────────────────

    [Fact]
    public void Each_provider_serves_its_own_kind()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();

        Assert.Equal(PipelineActionFieldKind.ObsScene, new ObsSceneOptionProvider(obs).Kind);
        Assert.Equal(
            PipelineActionFieldKind.ObsSceneItem,
            new ObsSceneItemOptionProvider(obs).Kind
        );
        Assert.Equal(PipelineActionFieldKind.ObsFilter, new ObsFilterOptionProvider(obs).Kind);
        Assert.Equal(
            PipelineActionFieldKind.ObsTransition,
            new ObsTransitionOptionProvider(obs).Kind
        );
        Assert.Equal(PipelineActionFieldKind.ObsInput, new ObsInputOptionProvider(obs).Kind);
        Assert.Equal(PipelineActionFieldKind.ObsHotkey, new ObsHotkeyOptionProvider(obs).Kind);
    }

    // ── scene ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scene_lists_every_scene_and_marks_the_live_one()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetScenesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Ok(new ObsSceneDto("Starting", false), new ObsSceneDto("Gameplay", true)));

        Result<PipelineOptionListResult> result = await new ObsSceneOptionProvider(
            obs
        ).GetOptionsAsync(Broadcaster, Ask());

        Assert.Equal(["Gameplay", "Starting"], Values(result));
        PipelineOption live = result.Value.Items[0];
        Assert.Equal("Gameplay", live.Label);
        Assert.Equal("live", live.SecondaryText);
        Assert.Null(result.Value.Items[1].SecondaryText);
        Assert.Equal(PipelineOptionState.Selectable, live.State);
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task Scene_search_value_lookup_and_paging_narrow_the_list()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetScenesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Ok(
                    new ObsSceneDto("Intro", false),
                    new ObsSceneDto("Intermission", false),
                    new ObsSceneDto("Outro", false)
                )
            );
        ObsSceneOptionProvider provider = new(obs);

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(search: "inter")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(search: "ignored", value: "Outro")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(["Intermission"], Values(searched));
        Assert.Equal(["Outro"], Values(looked));
        Assert.Equal(["Outro"], Values(second));
        Assert.Equal(3, second.Value.TotalCount);
    }

    [Fact]
    public async Task Scene_is_unavailable_with_the_reason_when_obs_is_not_connected()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetScenesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(NotConnected<ObsSceneDto>());

        AssertUnavailable(
            await new ObsSceneOptionProvider(obs).GetOptionsAsync(Broadcaster, Ask())
        );
    }

    // ── scene item (source) ──────────────────────────────────────────────────

    private static IObsControlService TwoScenes()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetScenesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Ok(new ObsSceneDto("Gameplay", true), new ObsSceneDto("Chatting", false)));
        obs.GetSceneItemListAsync(Broadcaster, "Gameplay", Arg.Any<CancellationToken>())
            .Returns(
                Ok(
                    new ObsSceneItemDto(1, "Webcam", Enabled: true, Locked: false),
                    new ObsSceneItemDto(2, "Alerts", Enabled: false, Locked: false)
                )
            );
        obs.GetSceneItemListAsync(Broadcaster, "Chatting", Arg.Any<CancellationToken>())
            .Returns(
                Ok(
                    new ObsSceneItemDto(1, "Webcam", Enabled: true, Locked: false),
                    new ObsSceneItemDto(2, "Chat box", Enabled: true, Locked: false)
                )
            );
        return obs;
    }

    [Fact]
    public async Task Scene_item_narrows_to_the_scene_the_step_saved_and_shows_visibility()
    {
        Result<PipelineOptionListResult> result = await new ObsSceneItemOptionProvider(
            TwoScenes()
        ).GetOptionsAsync(Broadcaster, Ask(context: ("scene", "Gameplay")));

        Assert.Equal(["Alerts", "Webcam"], Values(result));
        Assert.Equal("hidden", result.Value.Items[0].SecondaryText);
        Assert.Equal("visible", result.Value.Items[1].SecondaryText);
    }

    [Fact]
    public async Task Scene_item_without_a_scene_lists_each_source_of_every_scene_once()
    {
        Result<PipelineOptionListResult> result = await new ObsSceneItemOptionProvider(
            TwoScenes()
        ).GetOptionsAsync(Broadcaster, Ask());

        Assert.Equal(["Alerts", "Chat box", "Webcam"], Values(result));
        Assert.Equal(3, result.Value.TotalCount);
        Assert.All(result.Value.Items, i => Assert.Null(i.SecondaryText));
    }

    [Fact]
    public async Task Scene_item_search_and_value_lookup_work_across_scenes()
    {
        ObsSceneItemOptionProvider provider = new(TwoScenes());

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(search: "chat")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(value: "Webcam")
        );

        Assert.Equal(["Chat box"], Values(searched));
        Assert.Equal(["Webcam"], Values(looked));
    }

    [Fact]
    public async Task Scene_item_is_unavailable_when_obs_is_not_connected_with_or_without_a_scene()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetScenesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(NotConnected<ObsSceneDto>());
        obs.GetSceneItemListAsync(Broadcaster, "Gameplay", Arg.Any<CancellationToken>())
            .Returns(NotConnected<ObsSceneItemDto>());
        ObsSceneItemOptionProvider provider = new(obs);

        AssertUnavailable(await provider.GetOptionsAsync(Broadcaster, Ask()));
        AssertUnavailable(
            await provider.GetOptionsAsync(Broadcaster, Ask(context: ("scene", "Gameplay")))
        );
    }

    // ── filter ───────────────────────────────────────────────────────────────

    private static IObsControlService WithFilters()
    {
        IObsControlService obs = TwoScenes();
        obs.GetSourceFilterListAsync(Broadcaster, "Webcam", Arg.Any<CancellationToken>())
            .Returns(
                Ok(
                    new ObsFilterDto("Chroma Key", "chroma_key_filter_v2", Enabled: true, Index: 0),
                    new ObsFilterDto("Blur", "obs_stinger_filter", Enabled: false, Index: 1)
                )
            );
        obs.GetSourceFilterListAsync(Broadcaster, "Alerts", Arg.Any<CancellationToken>())
            .Returns(Ok(new ObsFilterDto("Blur", "obs_stinger_filter", Enabled: true, Index: 0)));
        obs.GetSourceFilterListAsync(Broadcaster, "Chat box", Arg.Any<CancellationToken>())
            .Returns(NotConnected<ObsFilterDto>());
        return obs;
    }

    [Fact]
    public async Task Filter_narrows_to_the_source_the_step_saved_and_shows_the_filter_kind()
    {
        Result<PipelineOptionListResult> result = await new ObsFilterOptionProvider(
            WithFilters()
        ).GetOptionsAsync(Broadcaster, Ask(context: ("source", "Webcam")));

        Assert.Equal(["Blur", "Chroma Key"], Values(result));
        Assert.Equal("obs_stinger_filter", result.Value.Items[0].SecondaryText);
        Assert.Equal("chroma_key_filter_v2", result.Value.Items[1].SecondaryText);
    }

    [Fact]
    public async Task Filter_without_a_source_lists_each_filter_name_of_every_scene_source_once()
    {
        Result<PipelineOptionListResult> result = await new ObsFilterOptionProvider(
            WithFilters()
        ).GetOptionsAsync(Broadcaster, Ask());

        Assert.Equal(["Blur", "Chroma Key"], Values(result));
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task Filter_search_and_value_lookup_work()
    {
        ObsFilterOptionProvider provider = new(WithFilters());

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(search: "chro", context: ("source", "Webcam"))
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(value: "Blur", context: ("source", "Webcam"))
        );

        Assert.Equal(["Chroma Key"], Values(searched));
        Assert.Equal(["Blur"], Values(looked));
    }

    [Fact]
    public async Task Filter_is_unavailable_when_obs_is_not_connected()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetSourceFilterListAsync(Broadcaster, "Webcam", Arg.Any<CancellationToken>())
            .Returns(NotConnected<ObsFilterDto>());
        obs.GetScenesAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(NotConnected<ObsSceneDto>());
        ObsFilterOptionProvider provider = new(obs);

        AssertUnavailable(
            await provider.GetOptionsAsync(Broadcaster, Ask(context: ("source", "Webcam")))
        );
        AssertUnavailable(await provider.GetOptionsAsync(Broadcaster, Ask()));
    }

    // ── transition ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Transition_lists_transitions_marks_the_current_and_honors_search()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetSceneTransitionListAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Ok(
                    new ObsTransitionDto("Cut", false),
                    new ObsTransitionDto("Fade", true),
                    new ObsTransitionDto("Stinger", false)
                )
            );
        ObsTransitionOptionProvider provider = new(obs);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(Broadcaster, Ask());
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(search: "sti")
        );

        Assert.Equal(["Cut", "Fade", "Stinger"], Values(all));
        Assert.Equal("current", all.Value.Items[1].SecondaryText);
        Assert.Null(all.Value.Items[0].SecondaryText);
        Assert.Equal(["Stinger"], Values(searched));
    }

    [Fact]
    public async Task Transition_is_unavailable_when_obs_is_not_connected()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetSceneTransitionListAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(NotConnected<ObsTransitionDto>());

        AssertUnavailable(
            await new ObsTransitionOptionProvider(obs).GetOptionsAsync(Broadcaster, Ask())
        );
    }

    // ── input ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Input_lists_every_input_with_its_kind_including_ones_without_audio()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetInputsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Ok(
                    new ObsInputDto("Mic", "wasapi_input_capture", Muted: false, VolumeDb: 0),
                    new ObsInputDto("Overlay", "browser_source", Muted: null, VolumeDb: null),
                    new ObsInputDto("Intro clip", "ffmpeg_source", Muted: null, VolumeDb: null)
                )
            );
        ObsInputOptionProvider provider = new(obs);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(Broadcaster, Ask());
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(value: "Overlay")
        );

        Assert.Equal(["Intro clip", "Mic", "Overlay"], Values(all));
        Assert.Equal("ffmpeg_source", all.Value.Items[0].SecondaryText);
        Assert.Equal(["Overlay"], Values(looked));
        Assert.Equal("browser_source", looked.Value.Items[0].SecondaryText);
    }

    [Fact]
    public async Task Input_is_unavailable_when_obs_is_not_connected()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetInputsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(NotConnected<ObsInputDto>());

        AssertUnavailable(
            await new ObsInputOptionProvider(obs).GetOptionsAsync(Broadcaster, Ask())
        );
    }

    // ── hotkey ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hotkey_lists_the_hotkey_names_and_honors_search_and_paging()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetHotkeyListAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Ok("OBSBasic.StartStreaming", "OBSBasic.StopStreaming", "libobs.mute"));
        ObsHotkeyOptionProvider provider = new(obs);

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(search: "stop")
        );
        Result<PipelineOptionListResult> paged = await provider.GetOptionsAsync(
            Broadcaster,
            Ask(page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(["OBSBasic.StopStreaming"], Values(searched));
        Assert.Equal("OBSBasic.StopStreaming", searched.Value.Items[0].Label);
        Assert.Equal(["OBSBasic.StopStreaming"], Values(paged));
        Assert.Equal(3, paged.Value.TotalCount);
    }

    [Fact]
    public async Task Hotkey_is_unavailable_when_obs_is_not_connected()
    {
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.GetHotkeyListAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(NotConnected<string>());

        AssertUnavailable(
            await new ObsHotkeyOptionProvider(obs).GetOptionsAsync(Broadcaster, Ask())
        );
    }
}
