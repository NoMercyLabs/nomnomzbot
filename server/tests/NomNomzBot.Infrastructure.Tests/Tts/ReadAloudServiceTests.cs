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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// The public read-aloud voice (Stoney 2026-10-23: "i want a proper voice that knows how to speak"): the legal
/// pages are read in the bot's own neural voice, not the browser's. The service is open to anyone, so it keeps
/// to short text, one voice per page language, and never reaches the provider for text it refuses.
/// </summary>
public sealed class ReadAloudServiceTests
{
    private static readonly TtsResult Clip = new([1, 2, 3], 1200, "en-US-AriaNeural", "edge");

    private static async Task<(ReadAloudService Service, ITtsService Tts)> BuildAsync()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsVoices.AddRange(
            Voice("en-US-AriaNeural", "en-US", isDefault: true),
            Voice("nl-NL-FennaNeural", "nl-NL"),
            Voice("nl-NL-ColetteNeural", "nl-NL"),
            Voice("nl-BE-ArnaudNeural", "nl-BE")
        );
        await db.SaveChangesAsync();

        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<double?>(),
                Arg.Any<double?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Clip);

        return (new ReadAloudService(db, tts, new PlatformTtsVoiceDefault(db)), tts);
    }

    private static TtsVoice Voice(string id, string locale, bool isDefault = false) =>
        new()
        {
            Id = id,
            Name = id,
            DisplayName = id,
            Locale = locale,
            Gender = "Female",
            Provider = "edge",
            IsDefault = isDefault,
        };

    [Fact]
    public async Task English_IsSpokenInTheBotsDefaultVoice()
    {
        (ReadAloudService service, ITtsService tts) = await BuildAsync();

        Result<TtsResult> result = await service.SynthesizeAsync("  Hello there.  ", "en");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be(Clip);
        await tts.Received(1)
            .SynthesizeAsync(
                "Hello there.",
                "en-US-AriaNeural",
                null,
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("nl")]
    [InlineData("nl-NL")]
    public async Task Dutch_IsSpokenInTheNetherlandsDutchVoice_ChosenTheSameWayEveryTime(
        string locale
    )
    {
        (ReadAloudService service, ITtsService tts) = await BuildAsync();

        await service.SynthesizeAsync("Hallo daar.", locale);

        await tts.Received(1)
            .SynthesizeAsync(
                "Hallo daar.",
                "nl-NL-ColetteNeural",
                null,
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ALanguageWithNoCatalogueVoice_FallsBackToTheDefaultVoice()
    {
        (ReadAloudService service, ITtsService tts) = await BuildAsync();

        await service.SynthesizeAsync("Bonjour.", "fr");

        await tts.Received(1)
            .SynthesizeAsync(
                "Bonjour.",
                "en-US-AriaNeural",
                null,
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankText_IsRefused_AndNeverReachesTheProvider(string text)
    {
        (ReadAloudService service, ITtsService tts) = await BuildAsync();

        Result<TtsResult> result = await service.SynthesizeAsync(text, "en");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await tts.DidNotReceiveWithAnyArgs().SynthesizeAsync(default!, default!);
    }

    [Fact]
    public async Task TextOverTheLimit_IsRefused_AndNeverReachesTheProvider()
    {
        (ReadAloudService service, ITtsService tts) = await BuildAsync();

        Result<TtsResult> result = await service.SynthesizeAsync(
            new string('a', ReadAloudService.MaxTextLength + 1),
            "en"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await tts.DidNotReceiveWithAnyArgs().SynthesizeAsync(default!, default!);
    }

    [Fact]
    public async Task AProviderFailure_BecomesATtsUnavailableResult_NotAnException()
    {
        (ReadAloudService service, ITtsService tts) = await BuildAsync();
        tts.SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<double?>(),
                Arg.Any<double?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<TtsResult>(_ => throw new HttpRequestException("403"));

        Result<TtsResult> result = await service.SynthesizeAsync("Hello.", "en");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TTS_UNAVAILABLE");
    }

    // Stoney 2026-10-29: the public voice must not be usable without limit. Run in parallel, the voice provider
    // answered some requests with an empty clip, and a clip that is empty must never reach the cache.
    [Fact]
    public async Task AnEmptyClip_IsATtsUnavailableResult_SoItIsNeverServedOrCached()
    {
        (ReadAloudService service, ITtsService tts) = await BuildAsync();
        tts.SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<double?>(),
                Arg.Any<double?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new TtsResult([], 0, "en-US-AriaNeural", "edge"));

        Result<TtsResult> result = await service.SynthesizeAsync("Hello.", "en");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TTS_UNAVAILABLE");
    }

    [Fact]
    public async Task ManyCallsAtOnce_NeverRunMoreThanTheCapOfSynthesesTogether_AndAllAreAnswered()
    {
        int running = 0;
        int mostAtOnce = 0;
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<double?>(),
                Arg.Any<double?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async _ =>
            {
                int now = Interlocked.Increment(ref running);
                int seen;
                while (now > (seen = Volatile.Read(ref mostAtOnce)))
                    Interlocked.CompareExchange(ref mostAtOnce, now, seen);

                await Task.Delay(60);
                Interlocked.Decrement(ref running);
                return Clip;
            });

        List<Task<Result<TtsResult>>> calls = [];
        for (int i = 0; i < 8; i++)
        {
            // Each caller has its own request scope, so its own database context; the cap is shared.
            TtsTestDbContext db = TtsTestDbContext.New();
            db.TtsVoices.Add(Voice("en-US-AriaNeural", "en-US", isDefault: true));
            await db.SaveChangesAsync();
            ReadAloudService service = new(db, tts, new PlatformTtsVoiceDefault(db));
            calls.Add(service.SynthesizeAsync($"Sentence number {i}.", "en"));
        }

        Result<TtsResult>[] results = await Task.WhenAll(calls);

        results.Should().AllSatisfy(result => result.IsSuccess.Should().BeTrue());
        mostAtOnce.Should().BeLessThanOrEqualTo(ReadAloudService.MaxSynthesesAtOnce);
        mostAtOnce.Should().BeGreaterThan(0);
    }
}
