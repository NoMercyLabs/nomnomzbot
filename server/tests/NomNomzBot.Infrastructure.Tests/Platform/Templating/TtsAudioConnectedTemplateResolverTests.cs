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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves <c>{tts.audioconnected}</c> reads the live overlay presence of the channel it renders for:
/// "true" only while a TTS Caption page is open for that channel, so a pipeline can refund a TTS
/// reward when nobody can hear it.
/// </summary>
public sealed class TtsAudioConnectedTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000f401");
    private static readonly Guid OtherChannel = Guid.Parse("0192b400-0000-7000-9000-00000000f402");

    private sealed class FakePresence : IOverlayPresenceRegistry
    {
        public HashSet<Guid> AudioSources { get; } = [];

        public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId) => false;

        public bool IsOverlayConnected(Guid broadcasterId) => AudioSources.Contains(broadcasterId);

        public string? GetAudioTarget(Guid broadcasterId) => null;

        public HashSet<Guid> TtsSources { get; } = [];

        public bool IsAudioSourceConnected(Guid broadcasterId) =>
            AudioSources.Contains(broadcasterId);

        public bool IsTtsSourceConnected(Guid broadcasterId) => TtsSources.Contains(broadcasterId);
    }

    private readonly FakePresence _presence = new();
    private readonly TemplateResolver _resolver;

    public TtsAudioConnectedTemplateResolverTests()
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(PronounGrammarTestDbContext.New());
        services.AddSingleton<IOverlayPresenceRegistry>(_presence);
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private Task<string> ResolveAsync(Guid channel) =>
        _resolver.ResolveAsync("{tts.audioconnected}", new Dictionary<string, string>(), channel);

    [Fact]
    public async Task Tts_audioconnected_is_true_only_while_a_tts_caption_page_is_open()
    {
        _presence.AudioSources.Add(Channel);
        string audioOnly = await ResolveAsync(Channel);
        _presence.TtsSources.Add(Channel);
        string whileOpen = await ResolveAsync(Channel);
        _presence.TtsSources.Remove(Channel);
        string after = await ResolveAsync(Channel);

        audioOnly.Should().Be("false");
        whileOpen.Should().Be("true");
        after.Should().Be("false");
    }

    [Fact]
    public async Task A_tts_caption_page_of_another_channel_does_not_count()
    {
        _presence.TtsSources.Add(OtherChannel);

        string resolved = await ResolveAsync(Channel);

        resolved.Should().Be("false");
    }
}
