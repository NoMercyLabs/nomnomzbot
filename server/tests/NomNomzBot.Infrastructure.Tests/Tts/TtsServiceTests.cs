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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves S053 (tts.md §6.2): the provider-selection path never prefers a keyless (unconfigured) BYOK provider
/// over the always-usable Edge provider, and the exception-fallback path never bakes in a favourite voice like
/// "en-US-AriaNeural" — it retries the caller's own requested voice, then falls through to whatever the
/// fallback provider's OWN catalogue actually lists. Exercises the real internal seams TtsService uses in
/// production (<see cref="TtsService.ResolveProvider"/>, <see cref="TtsService.RetryThenFirstAvailableAsync"/>),
/// not a reimplementation of them — real concrete Azure/Edge/ElevenLabs providers are constructed (never
/// invoking network I/O) so the type-based routing in <c>ResolveProvider</c> is exercised as written.
/// </summary>
public sealed class TtsServiceTests
{
    private static AzureTtsProvider Azure(string? apiKey) =>
        new(Substitute.For<IHttpClientFactory>(), NullLogger<AzureTtsProvider>.Instance, apiKey);

    private static ElevenLabsTtsProvider ElevenLabs(string? apiKey) =>
        new(
            Substitute.For<IHttpClientFactory>(),
            NullLogger<ElevenLabsTtsProvider>.Instance,
            apiKey
        );

    private static EdgeTtsProvider Edge() =>
        new(
            TimeProvider.System,
            Substitute.For<IHttpClientFactory>(),
            NullLogger<EdgeTtsProvider>.Instance
        );

    [Fact]
    public void ResolveProvider_KeylessAzure_IsNeverPreferredOverEdge()
    {
        // Azure is registered (shared operator config wires it in DI regardless of a key), but has NO api key —
        // exactly the "keyless Azure" shape the old code silently preferred and got empty audio back from.
        AzureTtsProvider keylessAzure = Azure(apiKey: null);
        EdgeTtsProvider edge = Edge();
        TtsService sut = new([keylessAzure, edge], NullLogger<TtsService>.Instance);

        ITtsProvider resolved = sut.ResolveProvider("en-US-AriaNeural");

        resolved
            .Should()
            .BeSameAs(
                edge,
                "a keyless BYOK provider must never win over the working keyless-by-design provider"
            );
    }

    [Fact]
    public void ResolveProvider_ConfiguredAzure_IsPreferredOverEdge()
    {
        // The opposite shape: Azure genuinely has a key — it's the streamer's own configured choice and should
        // still be selected (this proves the fix gates on configuration, it does not just ban Azure outright).
        AzureTtsProvider configuredAzure = Azure(apiKey: "real-key");
        EdgeTtsProvider edge = Edge();
        TtsService sut = new([configuredAzure, edge], NullLogger<TtsService>.Instance);

        ITtsProvider resolved = sut.ResolveProvider("en-US-AriaNeural");

        resolved.Should().BeSameAs(configuredAzure);
    }

    [Fact]
    public void ResolveProvider_KeylessElevenLabs_IsNeverPreferredForAUuidVoiceId()
    {
        ElevenLabsTtsProvider keylessElevenLabs = ElevenLabs(apiKey: null);
        AzureTtsProvider configuredAzure = Azure(apiKey: "real-key");
        TtsService sut = new([keylessElevenLabs, configuredAzure], NullLogger<TtsService>.Instance);

        ITtsProvider resolved = sut.ResolveProvider(Guid.NewGuid().ToString());

        resolved
            .Should()
            .BeSameAs(
                configuredAzure,
                "an unconfigured ElevenLabs must fall through to the next real provider"
            );
    }

    [Fact]
    public async Task RetryThenFirstAvailable_RetriesTheRequestedVoiceId_BeforeFallingThrough()
    {
        ITtsProvider fallback = Substitute.For<ITtsProvider>();
        fallback
            .SynthesizeAsync("hello", "en-GB-SoniaNeural", Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new TtsSynthesisResult
                    {
                        AudioData = [1, 2, 3],
                        DurationMs = 500,
                        Provider = "edge",
                        VoiceId = "en-GB-SoniaNeural",
                        ContentHash = "hash",
                    }
                )
            );

        TtsSynthesisResult result = await TtsService.RetryThenFirstAvailableAsync(
            fallback,
            "hello",
            "en-GB-SoniaNeural",
            CancellationToken.None
        );

        result
            .VoiceId.Should()
            .Be(
                "en-GB-SoniaNeural",
                "the originally requested voice worked on the fallback provider — no need to substitute anything"
            );
        await fallback.DidNotReceive().GetVoicesAsync(Arg.Any<CancellationToken>());
    }

    // S053: proves there is no hardcoded "Aria" (or any other baked-in favourite) left in the fallback path —
    // when the requested voice fails even on the fallback provider, it uses whatever THAT provider's own
    // catalogue lists first, which here is deliberately NOT an Azure/Edge Aria-style id.
    [Fact]
    public async Task RetryThenFirstAvailable_FallsThroughToTheProvidersOwnFirstVoice_NeverAHardcodedFavourite()
    {
        ITtsProvider fallback = Substitute.For<ITtsProvider>();
        fallback
            .SynthesizeAsync("hello", "missing-voice", Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new TtsSynthesisResult
                    {
                        AudioData = [],
                        DurationMs = 0,
                        Provider = "edge",
                        VoiceId = "missing-voice",
                        ContentHash = "",
                    }
                )
            );
        fallback
            .GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<IReadOnlyList<TtsVoiceInfo>>([
                    new()
                    {
                        Id = "zz-Zephyr",
                        Name = "Zephyr",
                        DisplayName = "Zephyr",
                        Locale = "zz-ZZ",
                        Gender = "Neutral",
                        Provider = "edge",
                    },
                ])
            );
        fallback
            .SynthesizeAsync("hello", "zz-Zephyr", Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new TtsSynthesisResult
                    {
                        AudioData = [9, 9],
                        DurationMs = 200,
                        Provider = "edge",
                        VoiceId = "zz-Zephyr",
                        ContentHash = "hash",
                    }
                )
            );

        TtsSynthesisResult result = await TtsService.RetryThenFirstAvailableAsync(
            fallback,
            "hello",
            "missing-voice",
            CancellationToken.None
        );

        result
            .VoiceId.Should()
            .Be(
                "zz-Zephyr",
                "the fallback provider's own first catalogue voice was used — never a hardcoded id"
            );
        result.VoiceId.Should().NotBe("en-US-AriaNeural");
        await fallback.Received(1).GetVoicesAsync(Arg.Any<CancellationToken>());
    }
}
