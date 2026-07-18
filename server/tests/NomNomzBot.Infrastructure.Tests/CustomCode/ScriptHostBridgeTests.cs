// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.Sandbox;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the per-execution host bridge (custom-code.md §3.1/§6.2): chat.send dispatches to the channel's Helix
/// chat provider with the tenant Guid (the guest only holds the Guid; the provider resolves the Twitch id);
/// economy.read reads the channel ledger for the trigger user; a granted-but-unwired capability resolves to a no-op.
/// </summary>
public sealed class ScriptHostBridgeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-00000000e0a2");

    private static ScriptHostBridge Build(
        IChatProvider? chat = null,
        ICurrencyAccountService? currency = null,
        IMusicService? music = null,
        IUserService? users = null,
        IHttpClientFactory? http = null,
        IScriptStorageService? storage = null,
        ITtsDispatchService? tts = null,
        IWidgetService? widgets = null,
        IWidgetEventNotifier? overlay = null,
        IRewardService? rewards = null
    ) =>
        BuildFor(
            Channel,
            chat,
            currency,
            music,
            users,
            http,
            storage,
            tts,
            widgets,
            overlay,
            rewards
        );

    // Same wiring, but bound to an arbitrary tenant — the tenant-isolation tests need a channel-B bridge.
    private static ScriptHostBridge BuildFor(
        Guid channel,
        IChatProvider? chat = null,
        ICurrencyAccountService? currency = null,
        IMusicService? music = null,
        IUserService? users = null,
        IHttpClientFactory? http = null,
        IScriptStorageService? storage = null,
        ITtsDispatchService? tts = null,
        IWidgetService? widgets = null,
        IWidgetEventNotifier? overlay = null,
        IRewardService? rewards = null
    ) =>
        new(
            channel,
            Viewer.ToString(),
            chat ?? Substitute.For<IChatProvider>(),
            currency ?? Substitute.For<ICurrencyAccountService>(),
            music ?? Substitute.For<IMusicService>(),
            users ?? Substitute.For<IUserService>(),
            http ?? Substitute.For<IHttpClientFactory>(),
            storage ?? Substitute.For<IScriptStorageService>(),
            tts ?? Substitute.For<ITtsDispatchService>(),
            widgets ?? Substitute.For<IWidgetService>(),
            overlay ?? Substitute.For<IWidgetEventNotifier>(),
            rewards ?? Substitute.For<IRewardService>()
        );

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            );
    }

    [Fact]
    public async Task Chat_send_dispatches_to_the_chat_provider_with_the_tenant_guid()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        ScriptHostBridge bridge = Build(chat);

        string? result = bridge.Resolve("chat.send")(
            "chat.send",
            ["hello world"],
            CancellationToken.None
        );

        result.Should().BeNull();
        await chat.Received()
            .SendMessageAsync(Channel, "hello world", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Economy_read_returns_the_trigger_users_balance()
    {
        ICurrencyAccountService currency = Substitute.For<ICurrencyAccountService>();
        currency
            .GetBalanceAsync(Channel, Viewer, Arg.Any<CancellationToken>())
            .Returns(Result.Success(500L));
        ScriptHostBridge bridge = Build(currency: currency);

        bridge
            .Resolve("economy.read")("economy.read", [], CancellationToken.None)
            .Should()
            .Be("500");
    }

    [Fact]
    public void Music_queue_enqueues_the_request_for_the_channel()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .AddToQueueAsync(
                Channel.ToString(),
                "lofi beats",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ScriptHostBridge bridge = Build(music: music);

        bridge
            .Resolve("music.queue")("music.queue", ["lofi beats"], CancellationToken.None)
            .Should()
            .Be("true");
    }

    [Fact]
    public void Http_fetch_returns_the_capped_response_body()
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory
            .CreateClient(EgressHttpClient.Name)
            .Returns(new HttpClient(new StubHandler("hello from the web")));
        ScriptHostBridge bridge = Build(http: factory);

        bridge
            .Resolve("http.fetch")(
                "http.fetch",
                ["https://example.com/data"],
                CancellationToken.None
            )
            .Should()
            .Be("hello from the web");
    }

    [Fact]
    public void Http_fetch_rejects_a_non_https_url()
    {
        Build()
            .Resolve("http.fetch")("http.fetch", ["http://example.com"], CancellationToken.None)
            .Should()
            .BeNull();
    }

    [Fact]
    public void Music_now_playing_returns_the_current_track_snapshot()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    TrackName: "Money for Nothing",
                    Artist: "Dire Straits",
                    Album: "Brothers in Arms",
                    ImageUrl: null,
                    DurationMs: 502000,
                    ProgressMs: 61000,
                    IsPlaying: true,
                    Volume: 70,
                    RequestedBy: "viewer42",
                    Provider: "spotify"
                )
            );
        ScriptHostBridge bridge = Build(music: music);

        string? json = bridge.Resolve("music.nowPlaying")(
            "music.nowPlaying",
            [],
            CancellationToken.None
        );

        JObject track = JObject.Parse(json!);
        track["track"]!.Value<string>().Should().Be("Money for Nothing");
        track["artist"]!.Value<string>().Should().Be("Dire Straits");
        track["isPlaying"]!.Value<bool>().Should().BeTrue();
        track["provider"]!.Value<string>().Should().Be("spotify");
    }

    [Fact]
    public void Music_now_playing_returns_null_when_nothing_is_playing()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);

        Build(music: music)
            .Resolve("music.nowPlaying")("music.nowPlaying", [], CancellationToken.None)
            .Should()
            .BeNull();
    }

    [Fact]
    public void User_get_returns_the_trigger_users_public_profile_without_pii()
    {
        IUserService users = Substitute.For<IUserService>();
        users
            .GetAsync(Viewer.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new UserDto(
                        Id: Viewer.ToString(),
                        Username: "cooluser",
                        DisplayName: "CoolUser",
                        ProfileImageUrl: "https://cdn.twitch.tv/cooluser.png",
                        Email: "secret@example.com",
                        CreatedAt: DateTime.UtcNow,
                        LastLoginAt: DateTime.UtcNow
                    )
                )
            );
        ScriptHostBridge bridge = Build(users: users);

        string? json = bridge.Resolve("user.get")("user.get", [], CancellationToken.None);

        JObject profile = JObject.Parse(json!);
        profile["displayName"]!.Value<string>().Should().Be("CoolUser");
        profile["username"]!.Value<string>().Should().Be("cooluser");
        profile["avatarUrl"]!.Value<string>().Should().Be("https://cdn.twitch.tv/cooluser.png");
        profile.Should().NotContainKey("email"); // PII is withheld from scripts
    }

    [Fact]
    public void User_get_returns_null_when_the_user_is_not_found()
    {
        IUserService users = Substitute.For<IUserService>();
        users
            .GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserDto>("Not found.", "NOT_FOUND"));

        Build(users: users)
            .Resolve("user.get")("user.get", [], CancellationToken.None)
            .Should()
            .BeNull();
    }

    [Fact]
    public void A_granted_but_unwired_capability_is_a_noop()
    {
        Build()
            .Resolve("moderation.timeout")("moderation.timeout", ["user"], CancellationToken.None)
            .Should()
            .BeNull();
    }

    // ── storage.* — real service over a shared in-memory store (persistence proven, not mocked) ──

    private static ScriptStorageService Storage(string dbName) =>
        new(AuthTestBuilder.NewContext(dbName));

    [Fact]
    public void Storage_set_then_get_round_trips_across_bridge_instances_on_the_same_db()
    {
        string dbName = Guid.NewGuid().ToString();

        ScriptHostBridge writer = Build(storage: Storage(dbName));
        writer
            .Resolve("storage.set")(
                "storage.set",
                ["feather.holder", "nomz_viewer"],
                CancellationToken.None
            )
            .Should()
            .Be("ok");

        // A fresh bridge over a fresh context on the SAME backing store — the analogue of the next run.
        ScriptHostBridge reader = Build(storage: Storage(dbName));
        reader
            .Resolve("storage.get")("storage.get", ["feather.holder"], CancellationToken.None)
            .Should()
            .Be("nomz_viewer");
    }

    [Fact]
    public void Storage_is_tenant_isolated_channel_b_cannot_read_channel_a()
    {
        string dbName = Guid.NewGuid().ToString();
        Guid channelB = Guid.Parse("0192a000-0000-7000-8000-00000000e0b1");

        Build(storage: Storage(dbName))
            .Resolve("storage.set")("storage.set", ["secret", "a-only"], CancellationToken.None)
            .Should()
            .Be("ok");

        BuildFor(channelB, storage: Storage(dbName))
            .Resolve("storage.get")("storage.get", ["secret"], CancellationToken.None)
            .Should()
            .BeNull();
    }

    [Fact]
    public void Storage_set_rejects_a_value_over_the_64kb_cap_and_writes_nothing()
    {
        string dbName = Guid.NewGuid().ToString();
        string oversized = new('x', 64 * 1024 + 1);

        ScriptHostBridge bridge = Build(storage: Storage(dbName));
        bridge
            .Resolve("storage.set")("storage.set", ["big", oversized], CancellationToken.None)
            .Should()
            .BeNull();
        bridge
            .Resolve("storage.get")("storage.get", ["big"], CancellationToken.None)
            .Should()
            .BeNull("the over-cap write must not have persisted anything");
    }

    [Fact]
    public async Task Storage_set_rejects_the_201st_key_but_still_updates_an_existing_key()
    {
        string dbName = Guid.NewGuid().ToString();
        ScriptStorageService service = Storage(dbName);
        for (int i = 0; i < 200; i++)
            (await service.SetAsync(Channel, $"k{i}", "v")).IsSuccess.Should().BeTrue();

        ScriptHostBridge bridge = Build(storage: Storage(dbName));
        bridge
            .Resolve("storage.set")("storage.set", ["k-new", "v"], CancellationToken.None)
            .Should()
            .BeNull("the channel is at its 200-key cap");
        bridge
            .Resolve("storage.set")("storage.set", ["k5", "rewritten"], CancellationToken.None)
            .Should()
            .Be("ok", "rewriting an existing key never counts against the cap");
        bridge
            .Resolve("storage.get")("storage.get", ["k5"], CancellationToken.None)
            .Should()
            .Be("rewritten");
    }

    [Fact]
    public void Storage_list_filters_by_prefix_and_delete_removes_the_key()
    {
        string dbName = Guid.NewGuid().ToString();
        ScriptHostBridge bridge = Build(storage: Storage(dbName));
        bridge.Resolve("storage.set")("storage.set", ["todo.1", "a"], CancellationToken.None);
        bridge.Resolve("storage.set")("storage.set", ["todo.2", "b"], CancellationToken.None);
        bridge.Resolve("storage.set")("storage.set", ["voice.swap", "c"], CancellationToken.None);

        string? listed = bridge.Resolve("storage.list")(
            "storage.list",
            ["todo."],
            CancellationToken.None
        );
        JArray keys = JArray.Parse(listed!);
        keys.Select(k => k.Value<string>()).Should().BeEquivalentTo("todo.1", "todo.2");

        bridge
            .Resolve("storage.delete")("storage.delete", ["todo.1"], CancellationToken.None)
            .Should()
            .Be("ok");
        bridge
            .Resolve("storage.get")("storage.get", ["todo.1"], CancellationToken.None)
            .Should()
            .BeNull();
        // The other keys are untouched.
        bridge
            .Resolve("storage.get")("storage.get", ["todo.2"], CancellationToken.None)
            .Should()
            .Be("b");
    }

    // ── tts.speak ──

    [Fact]
    public void Tts_speak_dispatches_the_resolved_text_and_voice_and_returns_the_outcome_json()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        TtsSpeakRequest? seen = null;
        tts.RequestSpeakAsync(Arg.Do<TtsSpeakRequest>(r => seen = r), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        VoiceId: "en-US-Aria",
                        Provider: "azure",
                        CharacterCount: 12,
                        DurationMs: 900,
                        PlaybackUrl: null
                    )
                )
            );

        string? json = Build(tts: tts)
            .Resolve("tts.speak")(
                "tts.speak",
                ["hello stream", "en-US-Aria"],
                CancellationToken.None
            );

        JObject outcome = JObject.Parse(json!);
        outcome["voiceId"]!.Value<string>().Should().Be("en-US-Aria");
        outcome["characterCount"]!.Value<int>().Should().Be(12);

        // The dispatch request mirrors PlayTtsAction's shape: this tenant, pipeline-style requester.
        seen.Should().NotBeNull();
        seen!.BroadcasterId.Should().Be(Channel);
        seen.RequestedByUserId.Should().Be(Guid.Empty);
        seen.RequestedByTwitchUserId.Should().Be(Viewer.ToString());
        seen.Text.Should().Be("hello stream");
        seen.VoiceIdOverride.Should().Be("en-US-Aria");
        seen.BitsAmount.Should().Be(0);
        seen.CommunityStanding.Should().Be("everyone");
    }

    [Fact]
    public void Tts_speak_returns_null_when_the_gate_refuses()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TtsDispatchOutcome>("TTS is disabled.", "FORBIDDEN"));

        Build(tts: tts)
            .Resolve("tts.speak")("tts.speak", ["hello"], CancellationToken.None)
            .Should()
            .BeNull();
    }

    // ── widget.emit ──

    private static readonly Guid WidgetId = Guid.Parse("0192a000-0000-7000-8000-00000000e0c1");

    private static WidgetDetail Widget(bool enabled = true, string name = "Alert Box") =>
        new(
            Id: WidgetId,
            Name: name,
            Description: null,
            Framework: "vue",
            Source: "custom",
            IsEnabled: enabled,
            OverlayUrl: null,
            ActiveVersionId: null,
            GalleryItemId: null,
            Settings: new Dictionary<string, object?>(),
            EventSubscriptions: [],
            LastRuntimeError: null,
            LastRanAt: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );

    [Fact]
    public async Task Widget_emit_pushes_the_event_with_normalized_clr_data()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .GetAsync(Channel.ToString(), WidgetId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Widget()));
        IWidgetEventNotifier overlay = Substitute.For<IWidgetEventNotifier>();
        object? pushed = null;
        overlay
            .SendWidgetEventAsync(
                Channel,
                WidgetId,
                "confetti",
                Arg.Do<object?>(d => pushed = d),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        string? result = Build(widgets: widgets, overlay: overlay)
            .Resolve("widget.emit")(
                "widget.emit",
                [WidgetId.ToString(), "confetti", "{\"count\":5,\"label\":\"gg\",\"big\":true}"],
                CancellationToken.None
            );

        result.Should().Be("ok");
        await overlay
            .Received(1)
            .SendWidgetEventAsync(
                Channel,
                WidgetId,
                "confetti",
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
        // JsonElement is normalized to a plain CLR graph (hub-transport-safe), values intact.
        Dictionary<string, object?> data = pushed
            .Should()
            .BeOfType<Dictionary<string, object?>>()
            .Subject;
        data["count"].Should().Be(5L);
        data["label"].Should().Be("gg");
        data["big"].Should().Be(true);
    }

    [Fact]
    public async Task Widget_emit_resolves_the_widget_by_case_insensitive_name()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .ListAsync(
                Channel.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<WidgetDetail>([Widget()], 1, 100, 1)));
        IWidgetEventNotifier overlay = Substitute.For<IWidgetEventNotifier>();

        string? result = Build(widgets: widgets, overlay: overlay)
            .Resolve("widget.emit")("widget.emit", ["alert box", "ping"], CancellationToken.None);

        result.Should().Be("ok");
        await overlay
            .Received(1)
            .SendWidgetEventAsync(
                Channel,
                WidgetId,
                "ping",
                Arg.Is<object?>(d => d == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Widget_emit_refuses_an_unknown_widget_without_pushing()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WidgetDetail>("Widget not found.", "NOT_FOUND"));
        IWidgetEventNotifier overlay = Substitute.For<IWidgetEventNotifier>();

        Build(widgets: widgets, overlay: overlay)
            .Resolve("widget.emit")(
                "widget.emit",
                [WidgetId.ToString(), "confetti"],
                CancellationToken.None
            )
            .Should()
            .BeNull();
        await overlay
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task Widget_emit_refuses_a_disabled_widget_without_pushing()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .GetAsync(Channel.ToString(), WidgetId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Widget(enabled: false)));
        IWidgetEventNotifier overlay = Substitute.For<IWidgetEventNotifier>();

        Build(widgets: widgets, overlay: overlay)
            .Resolve("widget.emit")(
                "widget.emit",
                [WidgetId.ToString(), "confetti"],
                CancellationToken.None
            )
            .Should()
            .BeNull();
        await overlay
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default, default, default!, default, default);
    }

    // ── reward.get / reward.update ──

    private static readonly Guid RewardId = Guid.Parse("0192a000-0000-7000-8000-00000000e0d1");

    private static RewardDetail Reward(bool manageable = true, string title = "Lucky Feather") =>
        new(
            Id: RewardId.ToString(),
            Title: title,
            Prompt: "Steal the feather",
            Cost: 500,
            IsEnabled: true,
            IsManageable: manageable,
            IsUserInputRequired: false,
            IsPaused: false,
            BackgroundColor: null,
            ImageUrl: null,
            MaxPerStream: null,
            MaxPerUserPerStream: null,
            GlobalCooldownSeconds: null,
            ActionType: null,
            ActionSettings: null,
            TimerDurationSeconds: null,
            PipelineId: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );

    [Fact]
    public void Reward_get_resolves_by_title_and_returns_the_public_projection()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .ListAsync(
                Channel.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<RewardDetail>([Reward()], 1, 100, 1)));

        string? json = Build(rewards: rewards)
            .Resolve("reward.get")("reward.get", ["lucky feather"], CancellationToken.None);

        JObject reward = JObject.Parse(json!);
        reward["id"]!.Value<string>().Should().Be(RewardId.ToString());
        reward["title"]!.Value<string>().Should().Be("Lucky Feather");
        reward["cost"]!.Value<int>().Should().Be(500);
        reward["prompt"]!.Value<string>().Should().Be("Steal the feather");
        reward["isEnabled"]!.Value<bool>().Should().BeTrue();
        reward["isPaused"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public void Reward_get_returns_null_for_an_unknown_reward()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RewardDetail>("Reward not found.", "NOT_FOUND"));

        Build(rewards: rewards)
            .Resolve("reward.get")("reward.get", [RewardId.ToString()], CancellationToken.None)
            .Should()
            .BeNull();
    }

    [Fact]
    public async Task Reward_update_patches_cost_and_title_through_the_rewards_service()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .GetAsync(Channel.ToString(), RewardId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reward()));
        UpdateRewardRequest? seen = null;
        rewards
            .UpdateAsync(
                Channel.ToString(),
                RewardId.ToString(),
                Arg.Do<UpdateRewardRequest>(r => seen = r),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Reward()));

        string? result = Build(rewards: rewards)
            .Resolve("reward.update")(
                "reward.update",
                [RewardId.ToString(), "{\"cost\":750,\"title\":\"Luckier Feather\"}"],
                CancellationToken.None
            );

        result.Should().Be("ok");
        // The service (the Helix-synced dashboard path) received exactly the declared patch, nothing more.
        seen.Should().NotBeNull();
        seen!.Cost.Should().Be(750);
        seen.Title.Should().Be("Luckier Feather");
        seen.Prompt.Should().BeNull();
        seen.IsEnabled.Should().BeNull();
        seen.IsPaused.Should().BeNull();
        await rewards
            .Received(1)
            .UpdateAsync(
                Channel.ToString(),
                RewardId.ToString(),
                Arg.Any<UpdateRewardRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Reward_update_refuses_a_non_manageable_reward_without_calling_update()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .GetAsync(Channel.ToString(), RewardId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Reward(manageable: false)));

        Build(rewards: rewards)
            .Resolve("reward.update")(
                "reward.update",
                [RewardId.ToString(), "{\"cost\":750}"],
                CancellationToken.None
            )
            .Should()
            .BeNull();
        await rewards.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Reward_update_returns_null_for_an_unknown_reward()
    {
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<RewardDetail>("Reward not found.", "NOT_FOUND"));

        Build(rewards: rewards)
            .Resolve("reward.update")(
                "reward.update",
                [RewardId.ToString(), "{\"cost\":750}"],
                CancellationToken.None
            )
            .Should()
            .BeNull();
        await rewards.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default!, default!, default);
    }
}
