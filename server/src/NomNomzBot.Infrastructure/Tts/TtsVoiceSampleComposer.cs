// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Composes the sentence a voice speaks to introduce itself when a sample is requested without text:
/// English first, then the same sentence in the voice's own language. A multilingual voice speaks a
/// handful of translations instead. This is spoken text for the voice language, not UI text, so it
/// lives here and not in the resource files.
/// </summary>
public static class TtsVoiceSampleComposer
{
    private const string English = "Hello chat, I'm {0}, and this is how I sound.";

    private static readonly string[] MultilingualLanguages = ["nl", "de", "fr", "es", "ja"];

    // Keyed by the language part of a BCP-47 locale ("nl-NL" -> "nl").
    private static readonly Dictionary<string, string> Translations = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["nl"] = "Hoi chat, ik ben {0}, en zo klink ik.",
        ["de"] = "Hallo Chat, ich bin {0}, und so klinge ich.",
        ["fr"] = "Salut le chat, moi c'est {0}, et voilà ma voix.",
        ["es"] = "Hola chat, soy {0}, y así sueno.",
        ["pt"] = "Olá chat, eu sou {0}, e é assim que eu soo.",
        ["it"] = "Ciao chat, sono {0}, ed ecco come suono.",
        ["ja"] = "チャットのみなさん、{0}です。これが私の声です。",
        ["ko"] = "채팅 여러분 안녕하세요, 저는 {0}입니다. 제 목소리는 이렇습니다.",
        ["zh"] = "大家好，我是{0}，这就是我的声音。",
        ["ru"] = "Привет, чат! Я {0}, и вот как я звучу.",
        ["pl"] = "Cześć czacie, jestem {0}, i tak brzmię.",
        ["tr"] = "Merhaba sohbet, ben {0}, ve sesim böyle.",
        ["sv"] = "Hej chatten, jag är {0}, och så här låter jag.",
        ["ar"] = "مرحبا يا شات، أنا {0}، وهذا هو صوتي.",
        ["hi"] = "नमस्ते चैट, मैं {0} हूँ, और मेरी आवाज़ ऐसी है।",
    };

    private static readonly string[] NameNoise = ["Neural", "Multilingual"];

    /// <summary>True for an Edge "...Multilingual..." voice and for any ElevenLabs voice (eleven_multilingual_v2).</summary>
    public static bool IsMultilingual(string name, string provider) =>
        name.Contains("Multilingual", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "elevenlabs", StringComparison.OrdinalIgnoreCase);

    public static string Compose(string voiceName, string locale, bool isMultilingual)
    {
        string name = ShortName(voiceName);
        List<string> sentences = [string.Format(English, name)];

        IEnumerable<string> languages = isMultilingual
            ? MultilingualLanguages
            : [LanguageOf(locale)];
        foreach (string language in languages)
        {
            if (Translations.TryGetValue(language, out string? template))
                sentences.Add(string.Format(template, name));
        }

        return string.Join(' ', sentences);
    }

    // "Emma Multilingual" -> "Emma", "ThomasNeural" -> "Thomas", "Jenny Online" -> "Jenny".
    private static string ShortName(string voiceName)
    {
        string cleaned = voiceName;
        foreach (string noise in NameNoise)
            cleaned = cleaned.Replace(noise, string.Empty, StringComparison.OrdinalIgnoreCase);

        string first =
            cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? string.Empty;
        return first.Length > 0 ? first : voiceName;
    }

    private static string LanguageOf(string locale)
    {
        int dash = locale.IndexOf('-');
        return dash < 0 ? locale : locale[..dash];
    }
}
