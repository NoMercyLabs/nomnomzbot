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
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

public sealed class TtsConfigServiceTestVoiceTests
{
    [Theory]
    [InlineData(false, "hello Leet Hacker")]
    [InlineData(true, "hello Prime Coder")]
    public async Task TestVoiceAsync_UsesPronunciationBeforeAutomaticNameFormatting(
        bool hasOverride,
        string expected
    )
    {
        Guid channel = Guid.NewGuid();
        TtsTestDbContext db = TtsTestDbContext.New();
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(Arg.Any<string>(), "voice", ct: Arg.Any<CancellationToken>())
            .Returns(new TtsResult([1], 100, "voice", "edge"));
        ITtsLexiconService lexicon = Substitute.For<ITtsLexiconService>();
        lexicon
            .ApplyAsync(channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                Task.FromResult(hasOverride ? "hello Prime Coder" : ci.ArgAt<string>(1))
            );
        TtsConfigService sut = new(
            db,
            tts,
            Substitute.For<IEventBus>(),
            Substitute.For<ISubjectKeyService>(),
            Substitute.For<NomNomzBot.Application.Identity.Services.IUserService>(),
            new PlatformTtsVoiceDefault(db),
            lexicon,
            new SpokenNameFormatter()
        );

        Result<TtsTestResultDto> result = await sut.TestVoiceAsync(
            channel,
            new() { Text = "hello @L33tH4x0r", VoiceId = "voice" }
        );

        result.IsSuccess.Should().BeTrue();
        await tts.Received(1).SynthesizeAsync(expected, "voice", ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestVoiceAsync_WithoutText_SpeaksTheComposedIntroOfTheCatalogueVoice()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsVoices.Add(
            new()
            {
                Id = "nl-NL-ColetteNeural",
                Name = "Colette",
                DisplayName = "Colette (NL)",
                Locale = "nl-NL",
                Gender = "Female",
                Provider = "edge",
            }
        );
        await db.SaveChangesAsync();
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(
                Arg.Any<string>(),
                "nl-NL-ColetteNeural",
                ct: Arg.Any<CancellationToken>()
            )
            .Returns(new TtsResult([1], 100, "nl-NL-ColetteNeural", "edge"));
        TtsConfigService sut = NewSut(db, tts);

        Result<TtsTestResultDto> result = await sut.TestVoiceAsync(
            Guid.NewGuid(),
            new() { Text = null, VoiceId = "nl-NL-ColetteNeural" }
        );

        result.IsSuccess.Should().BeTrue();
        await tts.Received(1)
            .SynthesizeAsync(
                "Hello chat, I'm Colette, and this is how I sound. Hoi chat, ik ben Colette, en zo klink ik.",
                "nl-NL-ColetteNeural",
                ct: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TestVoiceAsync_WithText_StillSpeaksTheSentText()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(Arg.Any<string>(), "voice", ct: Arg.Any<CancellationToken>())
            .Returns(new TtsResult([1], 100, "voice", "edge"));
        TtsConfigService sut = NewSut(db, tts);

        await sut.TestVoiceAsync(Guid.NewGuid(), new() { Text = "my own line", VoiceId = "voice" });

        await tts.Received(1)
            .SynthesizeAsync("my own line", "voice", ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestVoiceAsync_BlankTextForUnknownVoice_SpeaksEnglishIntroOnly()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        ITtsService tts = Substitute.For<ITtsService>();
        tts.SynthesizeAsync(Arg.Any<string>(), "Mystery", ct: Arg.Any<CancellationToken>())
            .Returns(new TtsResult([1], 100, "Mystery", "edge"));
        TtsConfigService sut = NewSut(db, tts);

        await sut.TestVoiceAsync(Guid.NewGuid(), new() { Text = "  ", VoiceId = "Mystery" });

        await tts.Received(1)
            .SynthesizeAsync(
                "Hello chat, I'm Mystery, and this is how I sound.",
                "Mystery",
                ct: Arg.Any<CancellationToken>()
            );
    }

    private static TtsConfigService NewSut(TtsTestDbContext db, ITtsService tts)
    {
        ITtsLexiconService lexicon = Substitute.For<ITtsLexiconService>();
        lexicon
            .ApplyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.ArgAt<string>(1)));
        return new(
            db,
            tts,
            Substitute.For<IEventBus>(),
            Substitute.For<ISubjectKeyService>(),
            Substitute.For<NomNomzBot.Application.Identity.Services.IUserService>(),
            new PlatformTtsVoiceDefault(db),
            lexicon,
            new SpokenNameFormatter()
        );
    }
}
