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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.TestRun;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// A test run reports the same <c>nnz.lastError</c> as a live run for every captured write: the same bad call on
/// the live bridge and on the capture bridge gives the same return and the same error, and the sink records
/// nothing for a call the live bridge refuses.
/// </summary>
public sealed class CaptureScriptHostBridgeValidationTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
    private static readonly Guid WidgetId = Guid.Parse("0192a000-0000-7000-8000-00000000f0c1");
    private static readonly Guid RewardId = Guid.Parse("0192a000-0000-7000-8000-00000000f0d1");

    private static string LongKey => new('k', IScriptStorageService.MaxKeyLength + 1);

    public static TheoryData<string, string[]> ArgumentFailures =>
        new()
        {
            { "chat.send", [""] },
            { "chat.send", [] },
            { "chat.reply", ["   "] },
            { "music.queue", [""] },
            { "storage.set", ["", "v"] },
            { "storage.set", ["key"] },
            { "storage.set", [LongKey, "v"] },
            { "storage.set", ["key", new string('v', IScriptStorageService.MaxValueBytes + 1)] },
            { "storage.delete", [""] },
            { "storage.delete", [LongKey] },
            { "tts.speak", [""] },
            { "tts.voice.set", [""] },
            { "tts.voice.set", ["nobody-by-that-name", "voice-1"] },
            { "widget.emit", ["", "confetti"] },
            { "widget.emit", [WidgetId.ToString(), ""] },
            { "reward.update", ["", "{}"] },
            { "reward.update", [RewardId.ToString()] },
            { "schedule.pipeline", ["", "5"] },
            { "schedule.pipeline", ["revert", "soon"] },
            { "schedule.pipeline", ["revert", "5", "not json"] },
        };

    private static ScriptHostBridge BuildLive(
        WidgetDetail? widget = null,
        RewardDetail? reward = null,
        IWidgetEventNotifier? notifier = null
    )
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                widget is null
                    ? Result.Failure<WidgetDetail>("Widget not found.", "NOT_FOUND")
                    : Result.Success(widget)
            );
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                reward is null
                    ? Result.Failure<RewardDetail>("Reward not found.", "NOT_FOUND")
                    : Result.Success(reward)
            );
        return new(
            Channel,
            Guid.NewGuid().ToString(),
            null,
            Substitute.For<IChatProvider>(),
            Substitute.For<ICurrencyAccountService>(),
            Substitute.For<IMusicService>(),
            Substitute.For<IHttpClientFactory>(),
            Substitute.For<IScriptStorageService>(),
            Substitute.For<ITtsDispatchService>(),
            widgets,
            notifier ?? Substitute.For<IWidgetEventNotifier>(),
            rewards,
            Substitute.For<IViewerAnalyticsService>(),
            Substitute.For<ITtsConfigService>(),
            Substitute.For<IScheduledPipelineService>(),
            AuthTestBuilder.NewContext(),
            Substitute.For<ISevenTvUserPaintResolver>(),
            Substitute.For<IOwnerActionService>(),
            Substitute.For<ITwitchUsersApi>()
        );
    }

    private static CaptureScriptHostBridge Capture(ScriptHostBridge inner, CaptureSink sink) =>
        new(inner, sink, (_, _) => "voice-1");

    private static WidgetDetail Widget(bool isEnabled, bool isAttached) =>
        new(
            Id: WidgetId,
            Name: "Alert Box",
            Description: null,
            Framework: "vue",
            Source: "custom",
            IsEnabled: isEnabled,
            OverlayUrl: null,
            ActiveVersionId: null,
            GalleryItemId: null,
            Settings: new(),
            EventSubscriptions: [],
            LastRuntimeError: null,
            LastRanAt: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            GalleryUpdateAvailable: false,
            IsAttached: isAttached,
            IsCustomized: false
        );

    private static RewardDetail Reward(bool isManageable) =>
        new(
            Id: RewardId.ToString(),
            Title: "Hydrate",
            Prompt: null,
            Response: null,
            Cost: 100,
            IsEnabled: true,
            IsManageable: isManageable,
            IsUserInputRequired: false,
            IsPaused: false,
            IsMigrationPending: false,
            BackgroundColor: null,
            ImageUrl: null,
            MaxPerStream: null,
            MaxPerUserPerStream: null,
            GlobalCooldownSeconds: null,
            TimerDurationSeconds: null,
            PipelineId: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );

    private static string? Call(IScriptHostBridge bridge, string key, string[] args) =>
        bridge.Resolve(key)(key, args, CancellationToken.None);

    private static string? LastError(IScriptHostBridge bridge) =>
        Call(bridge, ScriptHostErrorCodes.LastErrorKey, []);

    private static void ShouldMatchTheLiveRun(
        string key,
        string[] args,
        ScriptHostBridge live,
        ScriptHostBridge insideCapture
    )
    {
        CaptureSink sink = new();
        CaptureScriptHostBridge capture = Capture(insideCapture, sink);

        string? liveReturn = Call(live, key, args);
        string? liveError = LastError(live);
        string? testReturn = Call(capture, key, args);
        string? testError = LastError(capture);

        liveError
            .Should()
            .NotBeNull("the live call refuses this write, so there is an error to match");
        testError.Should().Be(liveError);
        testReturn.Should().Be(liveReturn);
        sink.Effects.Should().BeEmpty("a write the live call refuses is not recorded as done");
        sink.ChatOutput.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ArgumentFailures))]
    public void A_bad_argument_gives_the_same_last_error_as_the_live_call(
        string key,
        string[] args
    ) => ShouldMatchTheLiveRun(key, args, BuildLive(), BuildLive());

    [Fact]
    public void A_widget_that_does_not_exist_gives_the_same_last_error_as_the_live_call() =>
        ShouldMatchTheLiveRun(
            "widget.emit",
            [WidgetId.ToString(), "confetti"],
            BuildLive(),
            BuildLive()
        );

    [Fact]
    public void A_widget_that_is_turned_off_gives_the_same_last_error_as_the_live_call() =>
        ShouldMatchTheLiveRun(
            "widget.emit",
            [WidgetId.ToString(), "confetti"],
            BuildLive(Widget(isEnabled: false, isAttached: true)),
            BuildLive(Widget(isEnabled: false, isAttached: true))
        );

    [Fact]
    public void A_widget_open_in_no_browser_source_gives_the_same_last_error_as_the_live_call() =>
        ShouldMatchTheLiveRun(
            "widget.emit",
            [WidgetId.ToString(), "confetti"],
            BuildLive(Widget(isEnabled: true, isAttached: false)),
            BuildLive(Widget(isEnabled: true, isAttached: false))
        );

    [Fact]
    public void A_widget_payload_that_is_not_json_gives_the_same_last_error_as_the_live_call() =>
        ShouldMatchTheLiveRun(
            "widget.emit",
            [WidgetId.ToString(), "confetti", "{not json"],
            BuildLive(Widget(isEnabled: true, isAttached: true)),
            BuildLive(Widget(isEnabled: true, isAttached: true))
        );

    [Fact]
    public void A_reward_that_does_not_exist_gives_the_same_last_error_as_the_live_call() =>
        ShouldMatchTheLiveRun(
            "reward.update",
            [RewardId.ToString(), "{}"],
            BuildLive(),
            BuildLive()
        );

    [Fact]
    public void A_reward_the_bot_does_not_manage_gives_the_same_last_error_as_the_live_call() =>
        ShouldMatchTheLiveRun(
            "reward.update",
            [RewardId.ToString(), "{}"],
            BuildLive(reward: Reward(isManageable: false)),
            BuildLive(reward: Reward(isManageable: false))
        );

    [Fact]
    public void A_reward_patch_that_is_not_json_gives_the_same_last_error_as_the_live_call() =>
        ShouldMatchTheLiveRun(
            "reward.update",
            [RewardId.ToString(), "not json"],
            BuildLive(reward: Reward(isManageable: true)),
            BuildLive(reward: Reward(isManageable: true))
        );

    [Theory]
    [InlineData("chat.send", new[] { "Hello chat" }, null)]
    [InlineData("storage.set", new[] { "key", "value" }, "ok")]
    [InlineData("music.queue", new[] { "some song" }, "true")]
    public void A_valid_write_is_still_recorded_and_leaves_no_last_error(
        string key,
        string[] args,
        string? expectedReturn
    )
    {
        CaptureSink sink = new();
        CaptureScriptHostBridge capture = Capture(BuildLive(), sink);

        string? returned = Call(capture, key, args);

        returned.Should().Be(expectedReturn);
        LastError(capture).Should().BeNull();
        sink.Effects.Should().ContainSingle();
        sink.Effects[0].Name.Should().Be(key);
        sink.Effects[0].ArgsPreview.Should().Be(string.Join(" | ", args));
    }

    [Fact]
    public void A_valid_widget_emit_is_recorded_and_the_overlay_is_not_called()
    {
        IWidgetEventNotifier notifier = Substitute.For<IWidgetEventNotifier>();
        ScriptHostBridge inner = BuildLive(
            Widget(isEnabled: true, isAttached: true),
            notifier: notifier
        );
        CaptureSink sink = new();
        CaptureScriptHostBridge capture = Capture(inner, sink);

        string? returned = Call(capture, "widget.emit", [WidgetId.ToString(), "confetti"]);

        returned.Should().Be("ok");
        LastError(capture).Should().BeNull();
        sink.Effects.Should().ContainSingle();
        sink.Effects[0].Name.Should().Be("widget.emit");
        notifier.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void An_action_invoke_is_recorded_because_the_live_bridge_has_no_argument_check_for_it()
    {
        CaptureSink sink = new();
        CaptureScriptHostBridge capture = Capture(BuildLive(), sink);

        string? returned = Call(capture, "actions.invoke:send_message", ["Hello"]);

        returned.Should().Be(ScriptActionInvoker.CapturedResultJson);
        LastError(capture).Should().BeNull();
        sink.Effects.Should().ContainSingle();
        sink.Effects[0].Name.Should().Be("actions.invoke:send_message");
    }
}
