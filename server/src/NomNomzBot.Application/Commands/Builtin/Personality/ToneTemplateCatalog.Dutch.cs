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

public static partial class ToneTemplateCatalog
{
    private const string Dutch = "nl";

    /// <summary>The Dutch text of the slots translated so far; every other slot answers in English.</summary>
    private static void AddDutchSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Uptime.Key,
            BuiltinResponseSlots.Uptime.Live,
            informative:
            [
                "We zijn al {uptime} live.",
                "De stream staat nu {uptime} aan.",
                "Streamduur: {uptime}.",
            ],
            friendly:
            [
                "We zitten al {uptime} gezellig samen, fijn dat je er bent!",
                "Al {uptime} live en we genieten ervan!",
                "{uptime} stream tot nu toe, leuk dat je meekijkt!",
            ],
            sassy:
            [
                "We zijn {uptime} live. Ja, de hele tijd. Ik heb het bijgehouden, dat is mijn hele baan.",
                "De klok zegt {uptime}. De klok liegt niet. In tegenstelling tot \"nog één potje\" van twee uur geleden.",
                "{uptime}. Zolang zijn we al live. Wat jij met die tijd hebt gedaan, bespreek je maar met je browsergeschiedenis.",
                "{uptime} live en nog steeds geen plan. Consistent, dat wel.",
            ],
            hype:
            [
                "AL {uptime} LIVE EN WE GAAN GEWOON DOOR. NIEMAND IS MOE. ZELFS DE BOT NIET.",
                "{uptime} OP DE KLOK. DE GRIND SLAAPT NIET.",
                "{uptime} ONDERWEG EN WE ZIJN NOG MAAR AAN HET WARMDRAAIEN. GORDEL OM.",
            ],
            chill:
            [
                "al {uptime} live, geen haast.",
                "{uptime} bezig. gewoon chillen.",
                "al {uptime} bezig. alles goed.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Uptime.Key,
            BuiltinResponseSlots.Uptime.Offline,
            informative:
            [
                "De stream is op dit moment offline.",
                "We zijn nu offline.",
                "Op dit moment niet live.",
            ],
            friendly:
            [
                "We zijn nu offline, tot de volgende stream!",
                "Er is nog geen stream, maar leuk dat je even langskomt!",
                "Even offline, tot snel!",
            ],
            sassy:
            [
                "Offline. Je typte net !uptime in een lege kamer. Stoer hoor.",
                "De stream staat uit. Het is hier alleen ik. Heerlijk rustig. Verpest het niet.",
                "Geen stream nu. Ergens daarbuiten doet de streamer alsof hij een sociaal leven heeft.",
                "Offline. Uptime: nul. Sommige vragen beantwoorden zichzelf.",
            ],
            hype:
            [
                "WE ZIJN OFFLINE... VOORLOPIG. BLIJF KLAARSTAAN.",
                "NOG GEEN STREAM. DE RUST VOOR DE STORM.",
                "OFFLINE, NIET VERSLAGEN. TOT DE VOLGENDE.",
            ],
            chill: ["offline nu.", "even niet live.", "we zijn uit. tot later."]
        );
    }
}
