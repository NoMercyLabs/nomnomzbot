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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Adapters;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Platform;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// End-to-end proof of the chat-decoration options fixes, wiring the REAL <see cref="FeatureService"/> (backed by
/// its own <see cref="ChannelFeature"/> table) and a REAL <see cref="EventBus"/> (with
/// <see cref="ChatDecorationRulesCacheInvalidator"/> registered as its only handler) into a REAL
/// <see cref="ChatMessageDecorator"/> — not a hand-rolled <c>IFeatureService</c> substitute, which would never have
/// caught the audited bug: <c>GetFeaturesAsync</c> used to only project the catalogue's single "custom_code" entry,
/// so a real "use_7tv" <see cref="ChannelFeature"/> row was silently invisible to the decorator regardless of its
/// state. This proves the catalogue registration, the default-aware fallback, the single-call toggle-flip, and the
/// cache-invalidation-on-toggle all work TOGETHER against the real service, not in isolation against a mock.
/// </summary>
public sealed class ChatDecorationFeatureTogglingTests
{
    [Fact]
    public async Task Third_party_emote_defaults_on_then_one_toggle_disables_it_and_the_cache_invalidates_immediately()
    {
        Guid channel = Guid.CreateVersion7();
        FakeCache cache = new();
        await cache.SetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.SevenTv),
            [
                new ChatEmote(
                    EmoteProvider.SevenTv,
                    "7tv-1",
                    "PepeLaugh",
                    new Dictionary<string, string> { ["1"] = "https://cdn.7tv/1x" },
                    Animated: true,
                    ZeroWidth: false
                ),
            ]
        );

        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        EventBus eventBus = BuildEventBus(cache);
        FeatureService features = new(db, TimeProvider.System, eventBus);
        ChatMessageDecorator decorator = new(
            EmoteChain(cache),
            features,
            cache,
            NullLogger<ChatMessageDecorator>.Instance
        );

        // 1) No ChannelFeature row exists yet for this channel — use_7tv must default ON (the catalogue
        // registration + default-aware GetFeaturesAsync fallback), so the emote resolves.
        DecoratedChatMessage beforeToggle = await decorator.DecorateAsync(
            Event(channel, "PepeLaugh hello")
        );
        beforeToggle
            .Fragments.Should()
            .Contain(f => f.Type == "emote" && f.Emote!.Code == "PepeLaugh");

        // 2) ONE toggle call must land it DISABLED (not re-materialize an enabled row that needs a second click).
        // FeatureService saves the row, then publishes ChannelConfigChangedEvent{Domain="features"} through the
        // SAME EventBus that ChatDecorationRulesCacheInvalidator is registered on — evicting the decorator's
        // cached rules for this channel synchronously, before ToggleFeatureAsync returns.
        Result<FeatureStatusDto> toggled = await features.ToggleFeatureAsync(
            channel.ToString(),
            "use_7tv"
        );
        toggled.IsSuccess.Should().BeTrue();
        toggled.Value.IsEnabled.Should().BeFalse();

        // 3) The VERY NEXT decoration read reflects the disabled state — not the 60s-old cached "enabled" set
        // the first DecorateAsync call populated.
        DecoratedChatMessage afterToggle = await decorator.DecorateAsync(
            Event(channel, "PepeLaugh hello")
        );
        afterToggle
            .Fragments.Should()
            .ContainSingle()
            .Which.Should()
            .Match<ChatMessageFragment>(f => f.Type == "text" && f.Text == "PepeLaugh hello");
    }

    [Fact]
    public async Task Link_preview_stays_plain_text_when_use_link_preview_has_no_row_default_off()
    {
        Guid channel = Guid.CreateVersion7();
        FakeCache cache = new();
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        FeatureService features = new(db, TimeProvider.System, BuildEventBus(cache));
        ILinkPreviewService previews = Substitute.For<ILinkPreviewService>();
        ChatMessageDecorator decorator = new(
            [new ExplodeTextAdapter(), new LinkPreviewAdapter(previews), new ImplodeTextAdapter()],
            features,
            cache,
            NullLogger<ChatMessageDecorator>.Instance
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(
            Event(channel, "check https://example.com out", isSubscriber: true)
        );

        result
            .Fragments.Should()
            .ContainSingle()
            .Which.Should()
            .Match<ChatMessageFragment>(f =>
                f.Type == "text" && f.Text == "check https://example.com out"
            );
        await previews.DidNotReceive().FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Link_preview_resolves_once_the_channel_explicitly_enables_it()
    {
        Guid channel = Guid.CreateVersion7();
        FakeCache cache = new();
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        db.ChannelFeatures.Add(
            new ChannelFeature
            {
                BroadcasterId = channel,
                FeatureKey = "use_link_preview",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        FeatureService features = new(db, TimeProvider.System, BuildEventBus(cache));

        ILinkPreviewService previews = Substitute.For<ILinkPreviewService>();
        previews
            .FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<LinkPreview?>(new LinkPreview("example.com", "Example", null, null))
            );
        ChatMessageDecorator decorator = new(
            [new ExplodeTextAdapter(), new LinkPreviewAdapter(previews), new ImplodeTextAdapter()],
            features,
            cache,
            NullLogger<ChatMessageDecorator>.Instance
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(
            Event(channel, "check https://example.com out", isSubscriber: true)
        );

        ChatMessageFragment link = result
            .Fragments.Should()
            .ContainSingle(f => f.Type == "link")
            .Which;
        link.LinkUrl.Should().Be("https://example.com");
        link.LinkPreview!.Title.Should().Be("Example");
    }

    // The real EventBus (Infrastructure.Platform.Eventing), wired with ONLY ChatDecorationRulesCacheInvalidator as
    // its IEventHandler<ChannelConfigChangedEvent> — the exact production seam FeatureService.ToggleFeatureAsync
    // publishes through.
    private static EventBus BuildEventBus(ICacheService cache)
    {
        ServiceCollection services = new();
        services.AddSingleton(cache);
        services.AddScoped<
            IEventHandler<ChannelConfigChangedEvent>,
            ChatDecorationRulesCacheInvalidator
        >();
        ServiceProvider provider = services.BuildServiceProvider();
        return new EventBus(
            provider,
            NullLogger<EventBus>.Instance,
            new EventLogger(NullLogger<EventLogger>.Instance)
        );
    }

    // The real emote-resolving chain (mirrors ChatMessageDecoratorTests.EmoteChain).
    private static IChatDecorationAdapter[] EmoteChain(ICacheService cache) =>
        [
            new ExplodeTextAdapter(),
            new ThirdPartyEmoteAdapter(cache),
            new TwitchEmoteUrlAdapter(),
            new ImplodeTextAdapter(),
        ];

    private static ChatMessageReceivedEvent Event(
        Guid channel,
        string text,
        bool isSubscriber = false
    ) =>
        new()
        {
            BroadcasterId = channel,
            MessageId = "m1",
            TwitchBroadcasterId = "123",
            UserId = "u1",
            UserDisplayName = "Stoney",
            UserLogin = "stoney_eagle",
            Message = text,
            Fragments = [new ChatMessageFragment { Type = "text", Text = text }],
            Badges = [],
            IsSubscriber = isSubscriber,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? value) ? (T?)value : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }
}
