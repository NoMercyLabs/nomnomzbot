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
}
