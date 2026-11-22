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

namespace NomNomzBot.Application.Commands.Builtin.Personality;

/// <summary>Dutch help line of each built-in command; the English lines are in <c>ToneTemplateCatalog.HelpLines.cs</c>.</summary>
public static partial class ToneTemplateCatalog
{
    // Same keys as HelpLines(); the command name and every subcommand a viewer types stay as written.
    private static (string BuiltinKey, string Line)[] DutchHelpLines() =>
        [
            ("accountage", "— Laat zien hoe oud je Twitch-account is."),
            ("banger", "— Voegt het nummer dat nu speelt toe aan de bangers-playlist."),
            ("bansong", "[reden] — (Mod) Bant het huidige nummer en slaat het over."),
            (
                "clearsongs",
                "<gebruiker> — (Mod) Verwijdert alle nummers in de wachtrij die die gebruiker heeft aangevraagd."
            ),
            ("closequeue", "— (Mod) Sluit de songwachtrij voor nieuwe verzoeken."),
            ("commands", "— Toont alle commands die je met jouw rechten kunt gebruiken."),
            ("discord", "— Toont de Discord-uitnodigingslink."),
            ("followage", "— Laat zien hoe lang je het kanaal al volgt."),
            ("help", "<command> — Toont hulp voor een specifiek command."),
            ("leaderboard", "— Toont de top 3 gebruikers in verschillende categorieën."),
            ("lurk", "— Markeert je als lurker in de chat."),
            ("unlurk", "— Markeert je niet meer als lurker."),
            ("openqueue", "— (Mod) Opent de songwachtrij weer voor nieuwe verzoeken."),
            ("pause", "— (Mod) Pauzeert de muziek."),
            (
                "play",
                "[link] — (Mod) Reageer op een clip of YouTube-link om die op stream te zetten."
            ),
            ("playlist", "— Geeft je de Spotify-link naar de bangers-playlist."),
            ("quote", "[add <tekst> | #nummer] — Bekijk of voeg stream quotes toe."),
            ("resume", "— (Mod) Zet de gepauzeerde muziek weer aan."),
            ("skip", "— (Mod) Slaat het nummer dat nu speelt over."),
            ("song", "— Toont het nummer dat nu op stream speelt."),
            ("sr", "<songnaam of link> — Vraag een nummer aan voor de wachtrij."),
            ("stats", "[@gebruiker] — Toont chatstatistieken van jezelf of een andere gebruiker."),
            ("update", "[@gebruiker] — Werkt gebruikersinfo bij vanuit Twitch."),
            ("voice", "languages | get <taal> | set <stem> | current — Beheer je TTS-stem."),
            ("volume", "[0-100] — (Mod) Toont of stelt het muziekvolume in."),
            ("whisper", "<tekst> — Fluistert je bericht dramatisch."),
            ("wrongsong", "[code | nummer] — Neemt je eigen songverzoek terug."),
        ];

    private static void AddDutchHelpLineSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        foreach ((string builtinKey, string text) in DutchHelpLines())
        {
            string line = $"{{prefix}}{builtinKey} {text}";
            Translate(
                catalog,
                Dutch,
                BuiltinResponseSlots.Help.Key,
                BuiltinResponseSlots.Help.LineFor(builtinKey),
                informative: [line],
                friendly: [$"@{{user}} goede vraag! {line}"],
                sassy: [$"@{{user}} {line} Je had de pins kunnen lezen, maar vooruit."],
                hype: [$"@{{user}} {line} GA HET NU GEBRUIKEN."],
                chill: [$"@{{user}} {line}"]
            );
        }
    }
}
