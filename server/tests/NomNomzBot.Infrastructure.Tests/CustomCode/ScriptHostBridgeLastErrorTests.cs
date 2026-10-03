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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the host-side "last error" slot: a failing host call records a typed {code,message} the guest reads
/// through the <c>last.error</c> host call, and every host call (success included) clears the previous one.
/// </summary>
public sealed class ScriptHostBridgeLastErrorTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-00000000e0a2");

    private static ScriptHostBridge Build(
        IScriptStorageService? storage = null,
        ITtsDispatchService? tts = null,
        IRewardService? rewards = null,
        IWidgetService? widgets = null,
        IMusicService? music = null
    ) =>
        new(
            Channel,
            Viewer.ToString(),
            null,
            Substitute.For<IChatProvider>(),
            Substitute.For<ICurrencyAccountService>(),
            music ?? Substitute.For<IMusicService>(),
            Substitute.For<IHttpClientFactory>(),
            storage ?? Substitute.For<IScriptStorageService>(),
            tts ?? Substitute.For<ITtsDispatchService>(),
            widgets ?? Substitute.For<IWidgetService>(),
            Substitute.For<IWidgetEventNotifier>(),
            rewards ?? Substitute.For<IRewardService>(),
            Substitute.For<IViewerAnalyticsService>(),
            Substitute.For<ITtsConfigService>(),
            Substitute.For<IScheduledPipelineService>(),
            AuthTestBuilder.NewContext(),
            Substitute.For<ISevenTvUserPaintResolver>(),
            Substitute.For<IOwnerActionService>()
        );

    private static string? Call(ScriptHostBridge bridge, string key, params string[] args) =>
        bridge.Resolve(key)(key, args, CancellationToken.None);

    private static (string Code, string Message) LastError(ScriptHostBridge bridge)
    {
        string? json = Call(bridge, "last.error");
        json.Should().NotBeNull("the previous call failed, so there is an error to read");
        JObject error = JObject.Parse(json!);
        error.Properties().Select(p => p.Name).Should().BeEquivalentTo("code", "message");
        return (error["code"]!.Value<string>()!, error["message"]!.Value<string>()!);
    }

    [Fact]
    public void A_fresh_bridge_has_no_last_error()
    {
        Call(Build(), "last.error").Should().BeNull();
    }

    [Fact]
    public void A_missing_argument_records_invalid_argument()
    {
        ScriptHostBridge bridge = Build();

        Call(bridge, "chat.send", " ").Should().BeNull();

        (string code, string message) = LastError(bridge);
        code.Should().Be("invalid_argument");
        message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_non_https_fetch_records_invalid_argument()
    {
        ScriptHostBridge bridge = Build();

        Call(bridge, "http.fetch", "http://example.com").Should().BeNull();

        LastError(bridge).Code.Should().Be("invalid_argument");
    }

    [Fact]
    public void An_unknown_user_records_not_found()
    {
        ScriptHostBridge bridge = Build();

        Call(bridge, "user.get", "@nobody").Should().BeNull();

        LastError(bridge).Code.Should().Be("not_found");
    }

    [Fact]
    public void An_unknown_reward_records_not_found()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PagedList<RewardDetail>([], 0, 1, 100)));
        ScriptHostBridge bridge = Build(rewards: rewards);

        Call(bridge, "reward.get", "Nope").Should().BeNull();

        LastError(bridge).Code.Should().Be("not_found");
    }

    [Fact]
    public void A_storage_cap_refusal_keeps_the_services_message_and_maps_the_code()
    {
        IScriptStorageService storage = Substitute.For<IScriptStorageService>();
        storage
            .SetAsync(Channel, "k", "v", Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Storage is full: 200 keys.", "LIMIT_EXCEEDED"));
        ScriptHostBridge bridge = Build(storage: storage);

        Call(bridge, "storage.set", "k", "v").Should().BeNull();

        (string code, string message) = LastError(bridge);
        code.Should().Be("limit_exceeded");
        message.Should().Be("Storage is full: 200 keys.");
    }

    [Fact]
    public void A_refused_tts_gate_records_refused_with_the_gates_message()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TtsDispatchOutcome>("TTS is disabled.", "FORBIDDEN"));
        ScriptHostBridge bridge = Build(tts: tts);

        Call(bridge, "tts.speak", "hello").Should().BeNull();

        (string code, string message) = LastError(bridge);
        code.Should().Be("refused");
        message.Should().Be("TTS is disabled.");
    }

    [Fact]
    public void A_failed_music_request_still_returns_false_and_records_the_reason()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .RequestTrackAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(Result.Failure<MusicTrack>("No provider connected.", "NOT_FOUND"));
        ScriptHostBridge bridge = Build(music: music);

        Call(bridge, "music.queue", "lofi").Should().Be("false");

        (string code, string message) = LastError(bridge);
        code.Should().Be("not_found");
        message.Should().Be("No provider connected.");
    }

    [Fact]
    public void A_successful_call_clears_the_previous_error()
    {
        ScriptHostBridge bridge = Build();
        Call(bridge, "chat.send", " ");
        LastError(bridge).Code.Should().Be("invalid_argument");

        Call(bridge, "chat.send", "hello").Should().BeNull();

        Call(bridge, "last.error").Should().BeNull();
    }

    [Fact]
    public void Reading_the_last_error_does_not_clear_it()
    {
        ScriptHostBridge bridge = Build();
        Call(bridge, "chat.send", " ");

        LastError(bridge);

        LastError(bridge).Code.Should().Be("invalid_argument");
    }

    [Fact]
    public void The_slot_describes_only_the_latest_call()
    {
        ScriptHostBridge bridge = Build();
        Call(bridge, "chat.send", " ");
        Call(bridge, "user.get", "@nobody");

        LastError(bridge).Code.Should().Be("not_found");
    }

    [Fact]
    public void The_test_run_bridge_reports_the_inner_error_and_a_captured_write_clears_it()
    {
        ScriptHostBridge inner = Build();
        CaptureScriptHostBridge capture = new(inner, new(), (voice, _) => voice);

        capture.Resolve("user.get")("user.get", ["@nobody"], CancellationToken.None);
        JObject.Parse(capture.Resolve("last.error")("last.error", [], CancellationToken.None)!)[
            "code"
        ]!
            .Value<string>()
            .Should()
            .Be("not_found");

        capture.Resolve("chat.send")("chat.send", ["hi"], CancellationToken.None);

        capture.Resolve("last.error")("last.error", [], CancellationToken.None).Should().BeNull();
    }

    [Fact]
    public void A_disabled_widget_records_refused()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        Guid widgetId = Guid.Parse("0192a000-0000-7000-8000-00000000e0c1");
        widgets
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetDetail(
                        Id: widgetId,
                        Name: "Alert Box",
                        Description: null,
                        Framework: "vue",
                        Source: "custom",
                        IsEnabled: false,
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
                        IsAttached: false,
                        IsCustomized: false
                    )
                )
            );
        ScriptHostBridge bridge = Build(widgets: widgets);

        Call(bridge, "widget.emit", widgetId.ToString(), "confetti").Should().BeNull();

        LastError(bridge).Code.Should().Be("refused");
    }
}
