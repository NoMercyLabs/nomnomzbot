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
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

public sealed class TtsVoiceSampleComposerTests
{
    [Fact]
    public void Compose_EnglishVoice_SpeaksEnglishOnly()
    {
        string text = TtsVoiceSampleComposer.Compose("Aria", "en-US", false);

        text.Should().Be("Hello chat, I'm Aria, and this is how I sound.");
    }

    [Fact]
    public void Compose_DutchVoice_SpeaksEnglishThenDutch()
    {
        string text = TtsVoiceSampleComposer.Compose("Colette", "nl-NL", false);

        text.Should()
            .Be(
                "Hello chat, I'm Colette, and this is how I sound. "
                    + "Hoi chat, ik ben Colette, en zo klink ik."
            );
    }

    [Fact]
    public void Compose_MultilingualVoice_SpeaksEnglishThenAHandfulOfTranslations()
    {
        string text = TtsVoiceSampleComposer.Compose("Emma Multilingual", "en-US", true);

        text.Should()
            .Be(
                "Hello chat, I'm Emma, and this is how I sound. "
                    + "Hoi chat, ik ben Emma, en zo klink ik. "
                    + "Hallo Chat, ich bin Emma, und so klinge ich. "
                    + "Salut le chat, moi c'est Emma, et voilà ma voix. "
                    + "Hola chat, soy Emma, y así sueno. "
                    + "チャットのみなさん、Emmaです。これが私の声です。"
            );
    }

    [Fact]
    public void Compose_UnknownLanguage_FallsBackToEnglishOnly()
    {
        string text = TtsVoiceSampleComposer.Compose("Taqqiq", "iu-Cans-CA", false);

        text.Should().Be("Hello chat, I'm Taqqiq, and this is how I sound.");
    }

    [Theory]
    [InlineData("pt-BR", "Olá chat, eu sou Zed, e é assim que eu soo.")]
    [InlineData("ja-JP", "チャットのみなさん、Zedです。これが私の声です。")]
    [InlineData("zh-CN", "大家好，我是Zed，这就是我的声音。")]
    [InlineData("ar-EG", "مرحبا يا شات، أنا Zed، وهذا هو صوتي.")]
    public void Compose_NativeLanguage_AppendsTheNativeSentence(string locale, string native)
    {
        string text = TtsVoiceSampleComposer.Compose("Zed", locale, false);

        text.Should().Be("Hello chat, I'm Zed, and this is how I sound. " + native);
    }

    [Theory]
    [InlineData("ThomasNeural", "Thomas")]
    [InlineData("Emma Multilingual", "Emma")]
    [InlineData("Jenny Online", "Jenny")]
    public void Compose_ReducesTheCatalogueNameToTheShortFirstName(string name, string expected)
    {
        string text = TtsVoiceSampleComposer.Compose(name, "en-GB", false);

        text.Should().Be($"Hello chat, I'm {expected}, and this is how I sound.");
    }

    [Theory]
    [InlineData("Emma Multilingual", "edge", true)]
    [InlineData("Rachel", "elevenlabs", true)]
    [InlineData("Aria", "edge", false)]
    public void IsMultilingual_DetectsEdgeNameAndElevenLabsModel(
        string name,
        string provider,
        bool expected
    )
    {
        TtsVoiceSampleComposer.IsMultilingual(name, provider).Should().Be(expected);
    }
}
