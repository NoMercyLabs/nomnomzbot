// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the <c>tts_synthesize</c> pipeline action: it resolves the <c>text</c> template, synthesizes
/// audio without touching the gate/censor/ledger pipeline, stores the clip, and exposes
/// <c>{{tts.audioUrl}}</c>/<c>{{tts.durationMs}}</c>/<c>{{tts.voiceId}}</c> to later steps. Every failure
/// path (no text, no voice configured, empty synth, store failure, URL failure) must fail loudly without
/// partial variable writes.
/// </summary>
public sealed class TtsSynthesizeActionTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-2222-7000-8000-000000000002");

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "streamer-1",
            TriggeredByDisplayName = "streamer",
            MessageId = "m1",
            RawMessage = "!bsod",
            CancellationToken = default,
        };

    private static ActionDefinition Action(params (string Key, object Value)[] p) =>
        new()
        {
            Type = "tts_synthesize",
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    private static (
        TtsSynthesizeAction Action,
        ITtsService Tts,
        ITtsConfigService Config,
        ISoundClipStore Store
    ) Build(string resolvedText)
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(resolvedText));

        ITtsService tts = Substitute.For<ITtsService>();
        ITtsConfigService config = Substitute.For<ITtsConfigService>();
        ISoundClipStore store = Substitute.For<ISoundClipStore>();

        return (new(resolver, tts, config, store), tts, config, store);
    }

    private static TtsConfigDto DefaultConfig(string? defaultVoiceId) =>
        new(
            IsEnabled: true,
            Mode: "auto",
            DefaultProvider: "azure",
            DefaultVoiceId: defaultVoiceId,
            MaxCharacters: 200,
            MinPermission: "everyone",
            SkipBotMessages: true,
            ReadUsernames: true,
            ProfanityCensorEnabled: false,
            ModApprovalRequired: false,
            MinBitsToTts: null
        );

    [Fact]
    public async Task ExecuteAsync_ExplicitVoice_SynthesizesStoresAndExposesVariables()
    {
        (
            TtsSynthesizeAction action,
            ITtsService tts,
            ITtsConfigService config,
            ISoundClipStore store
        ) = Build("BSOD detected, rebooting.");

        tts.SynthesizeAsync("BSOD detected, rebooting.", "en-US-Guy", Arg.Any<CancellationToken>())
            .Returns(new TtsResult([1, 2, 3], 4200, "en-US-Guy", "azure"));

        store
            .PutAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<System.IO.Stream>(),
                "audio/mpeg",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success("clips/tts-abc.mp3"));
        store
            .GetPlaybackUrlAsync("clips/tts-abc.mp3", Arg.Any<CancellationToken>())
            .Returns(Result.Success("https://bot.local/sounds/tts-abc.mp3"));

        PipelineExecutionContext ctx = Context();
        ActionResult result = await action.ExecuteAsync(
            ctx,
            Action(("text", "{{args}}"), ("voice", "en-US-Guy"))
        );

        result.Succeeded.Should().BeTrue();
        ctx.Variables["tts.audioUrl"].Should().Be("https://bot.local/sounds/tts-abc.mp3");
        ctx.Variables["tts.durationMs"].Should().Be("4200");
        ctx.Variables["tts.voiceId"].Should().Be("en-US-Guy");
        await config.DidNotReceive().GetConfigAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoVoiceParam_FallsBackToChannelDefaultVoice()
    {
        (
            TtsSynthesizeAction action,
            ITtsService tts,
            ITtsConfigService config,
            ISoundClipStore store
        ) = Build("hi there");

        config
            .GetConfigAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(DefaultConfig("nl-NL-Colette")));
        tts.SynthesizeAsync("hi there", "nl-NL-Colette", Arg.Any<CancellationToken>())
            .Returns(new TtsResult([9], 900, "nl-NL-Colette", "azure"));
        store
            .PutAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<System.IO.Stream>(),
                "audio/mpeg",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success("clips/tts-xyz.mp3"));
        store
            .GetPlaybackUrlAsync("clips/tts-xyz.mp3", Arg.Any<CancellationToken>())
            .Returns(Result.Success("https://bot.local/sounds/tts-xyz.mp3"));

        ActionResult result = await action.ExecuteAsync(Context(), Action(("text", "hi there")));

        result.Succeeded.Should().BeTrue();
        await tts.Received(1)
            .SynthesizeAsync("hi there", "nl-NL-Colette", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingText_FailsWithoutSynthesizing()
    {
        (TtsSynthesizeAction action, ITtsService tts, _, _) = Build("unused");

        ActionResult result = await action.ExecuteAsync(Context(), Action());

        result.Succeeded.Should().BeFalse();
        await tts.DidNotReceive()
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoVoiceConfigured_FailsWithoutSynthesizing()
    {
        (TtsSynthesizeAction action, ITtsService tts, ITtsConfigService config, _) = Build(
            "hi there"
        );

        config
            .GetConfigAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(Result.Success(DefaultConfig(defaultVoiceId: null)));

        ActionResult result = await action.ExecuteAsync(Context(), Action(("text", "hi there")));

        result.Succeeded.Should().BeFalse();
        await tts.DidNotReceive()
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ProviderReturnsNoAudio_FailsWithoutStoring()
    {
        (TtsSynthesizeAction action, ITtsService tts, _, ISoundClipStore store) = Build("hi there");

        tts.SynthesizeAsync("hi there", "en-US-Guy", Arg.Any<CancellationToken>())
            .Returns(new TtsResult([], 0, "en-US-Guy", "azure"));

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("text", "hi there"), ("voice", "en-US-Guy"))
        );

        result.Succeeded.Should().BeFalse();
        await store
            .DidNotReceive()
            .PutAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<System.IO.Stream>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_StoreFails_SurfacesTheReason()
    {
        (TtsSynthesizeAction action, ITtsService tts, _, ISoundClipStore store) = Build("hi there");

        tts.SynthesizeAsync("hi there", "en-US-Guy", Arg.Any<CancellationToken>())
            .Returns(new TtsResult([1], 500, "en-US-Guy", "azure"));
        store
            .PutAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<System.IO.Stream>(),
                "audio/mpeg",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<string>("disk full"));

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("text", "hi there"), ("voice", "en-US-Guy"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("disk full");
    }

    [Fact]
    public async Task ExecuteAsync_PlaybackUrlResolutionFails_SurfacesTheReason()
    {
        (TtsSynthesizeAction action, ITtsService tts, _, ISoundClipStore store) = Build("hi there");

        tts.SynthesizeAsync("hi there", "en-US-Guy", Arg.Any<CancellationToken>())
            .Returns(new TtsResult([1], 500, "en-US-Guy", "azure"));
        store
            .PutAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<System.IO.Stream>(),
                "audio/mpeg",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success("clips/tts-abc.mp3"));
        store
            .GetPlaybackUrlAsync("clips/tts-abc.mp3", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>("key not found"));

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("text", "hi there"), ("voice", "en-US-Guy"))
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("key not found");
    }
}
