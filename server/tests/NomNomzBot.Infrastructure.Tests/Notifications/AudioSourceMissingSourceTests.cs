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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// A channel with overlay pages open but no Audio Source page hears its sound and TTS from some other page.
/// The inbox must say so, and the item must be gone once an Audio Source page is connected.
/// </summary>
public sealed class AudioSourceMissingSourceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-000000000b01");
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakePresence(bool overlay, bool audioSource) : IOverlayPresenceRegistry
    {
        public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId) => overlay;

        public bool IsOverlayConnected(Guid broadcasterId) => broadcasterId == Channel && overlay;

        public string? GetAudioTarget(Guid broadcasterId) => overlay ? "conn" : null;

        public bool IsAudioSourceConnected(Guid broadcasterId) =>
            broadcasterId == Channel && audioSource;
    }

    private static async Task<List<ActionRequiredItemDto>> ItemsAsync(
        bool overlay,
        bool audioSource,
        params string[] dismissed
    )
    {
        AudioSourceMissingSource source = new(
            new FakePresence(overlay, audioSource),
            new FakeTimeProvider(Now)
        );
        Result<List<ActionRequiredItemDto>> result = await source.GetItemsAsync(
            Channel,
            new HashSet<string>(dismissed)
        );
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task An_overlay_page_without_an_audio_source_raises_one_item()
    {
        List<ActionRequiredItemDto> items = await ItemsAsync(overlay: true, audioSource: false);

        ActionRequiredItemDto item = items.Should().ContainSingle().Subject;
        item.Id.Should().Be("audio-source-missing");
        item.Kind.Should().Be("audio_source_missing");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_audio_source_missing_title");
        item.MessageKey.Should().Be("attention_audio_source_missing_message");
        item.DeepLinkRoute.Should().Be("widgets");
        item.DetectedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task A_connected_audio_source_raises_nothing()
    {
        (await ItemsAsync(overlay: true, audioSource: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task No_overlay_page_at_all_raises_nothing()
    {
        (await ItemsAsync(overlay: false, audioSource: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_dismissed_item_is_not_raised_again()
    {
        (await ItemsAsync(overlay: true, audioSource: false, "audio-source-missing"))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task The_item_dismisses_under_its_own_key()
    {
        AudioSourceMissingSource source = new(
            new FakePresence(true, false),
            new FakeTimeProvider(Now)
        );

        Result<List<string>> keys = await source.ResolveDismissalKeysAsync(
            Channel,
            "audio-source-missing"
        );

        keys.Value.Should().Equal("audio-source-missing");
        source.KeyPrefixes.Should().Contain("audio-source-missing");
    }
}
