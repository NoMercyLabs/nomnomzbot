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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// TTS plays only on the TTS Caption page. A channel with overlay pages open but no TTS Caption page hears no
/// TTS at all, so the inbox must say so, and the item must be gone once a TTS Caption page connects.
/// </summary>
public sealed class TtsSourceMissingSourceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-000000000c05");
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakePresence(bool overlay, bool ttsSource) : IOverlayPresenceRegistry
    {
        public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId) => overlay;

        public bool IsOverlayConnected(Guid broadcasterId) => broadcasterId == Channel && overlay;

        public string? GetAudioTarget(Guid broadcasterId) => overlay ? "audio-conn" : null;

        public bool IsAudioSourceConnected(Guid broadcasterId) => overlay;

        public string? GetTtsTarget(Guid broadcasterId) =>
            broadcasterId == Channel && ttsSource ? "tts-conn" : null;
    }

    private static TtsSourceMissingSource NewSource(bool overlay, bool ttsSource) =>
        new(new FakePresence(overlay, ttsSource), new FakeTimeProvider(Now));

    private static async Task<List<ActionRequiredItemDto>> ItemsAsync(
        bool overlay,
        bool ttsSource,
        string[]? dismissed = null
    )
    {
        Result<List<ActionRequiredItemDto>> result = await NewSource(overlay, ttsSource)
            .GetItemsAsync(Channel, new HashSet<string>(dismissed ?? []));
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task An_overlay_page_without_a_tts_caption_page_raises_one_warning()
    {
        List<ActionRequiredItemDto> items = await ItemsAsync(overlay: true, ttsSource: false);

        ActionRequiredItemDto item = items.Should().ContainSingle().Subject;
        item.Id.Should().Be("tts-source-missing");
        item.Kind.Should().Be("tts_source_missing");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_tts_source_missing_title");
        item.MessageKey.Should().Be("attention_tts_source_missing_message");
        item.DeepLinkRoute.Should().Be("overlays");
        item.DetectedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task A_connected_tts_caption_page_raises_nothing()
    {
        (await ItemsAsync(overlay: true, ttsSource: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task No_overlay_page_at_all_raises_nothing()
    {
        (await ItemsAsync(overlay: false, ttsSource: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_dismissed_item_is_not_raised_again()
    {
        (await ItemsAsync(overlay: true, ttsSource: false, dismissed: ["tts-source-missing"]))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task The_item_dismisses_under_its_own_key()
    {
        TtsSourceMissingSource source = NewSource(true, false);

        Result<List<string>> keys = await source.ResolveDismissalKeysAsync(
            Channel,
            "tts-source-missing"
        );

        keys.Value.Should().Equal("tts-source-missing");
        source.KeyPrefixes.Should().Equal("tts-source-missing");
        source.InvalidatingEventTypes.Should().BeEmpty();
    }
}
