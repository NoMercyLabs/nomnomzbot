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

/// <summary>Dutch text of the slots <c>ToneTemplateCatalog.cs</c> adds itself; the English text is there.</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddDutchBaseSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddDutchBaseSongSlots(catalog);
        AddDutchBaseQueueSlots(catalog);
        AddDutchBaseSongRequestSlots(catalog);
        AddDutchBaseSkipSlots(catalog);
        AddDutchBaseCommandsAndHelpSlots(catalog);
        AddDutchBaseLurkAndAccountAgeSlots(catalog);
        AddDutchBaseWhisperAndBanSongSlots(catalog);
        AddDutchBaseVolumeSlots(catalog);
        AddDutchBaseUpdateSlots(catalog);
        AddDutchBaseGameAndStatusSlots(catalog);
    }

    private static void AddDutchBaseSongSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Song.Key,
            BuiltinResponseSlots.Song.Playing,
            informative:
            [
                "Het huidige nummer is: {song.name} van {song.artist} {song.link} {song.attribution}",
            ],
            friendly:
            [
                "We genieten van {song.name} van {song.artist}, top keuze! {song.link} {song.attribution}",
                "Nu aan het spelen: {song.name} van {song.artist}. Veel plezier! {song.link} {song.attribution}",
                "Dit is {song.name} van {song.artist}. {song.link} {song.attribution}",
            ],
            sassy:
            [
                "Het is {song.name} van {song.artist}. Je had de overlay kunnen lezen, maar ik voel me gevleid dat je het vraagt. {song.link} {song.attribution}",
                "{song.name} van {song.artist}. Ja, alweer. Nee, ik kies ze niet. Ik moet ze gewoon uitzitten. {song.link} {song.attribution}",
                "Nu aan de beurt: {song.name} van {song.artist}. Gedurfde keuze van iemand. Ik noem geen namen. {song.link} {song.attribution}",
                "{song.name} van {song.artist}. Voor de goede orde: niemand heeft het overgeslagen. Nog niet. {song.link} {song.attribution}",
            ],
            hype:
            [
                "{song.name} VAN {song.artist}. PURE TUNE. ZET HEM LUIDER. {song.link} {song.attribution}",
                "WE BLAZEN {song.name} VAN {song.artist} DOOR DE BOXEN. BUREN, SUCCES. {song.link} {song.attribution}",
                "{song.name} VAN {song.artist} EN HET GAAT HARD. {song.link} {song.attribution}",
            ],
            chill:
            [
                "{song.status} {song.name}, {song.artist}. {song.link} {song.attribution}",
                "speelt nu {song.name} van {song.artist}. {song.link} {song.attribution}",
                "{song.name}, {song.artist}. lekker. {song.link} {song.attribution}",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Song.Key,
            BuiltinResponseSlots.Song.Nothing,
            informative: ["Er speelt op dit moment geen nummer!"],
            friendly:
            [
                "Er speelt nu niets, vraag iets aan met !sr!",
                "Stil hier! Gooi een !sr in de chat om de muziek te starten.",
                "Nog geen nummer, jouw keuze kan de volgende zijn!",
            ],
            sassy:
            [
                "Er speelt niets. Alleen het geluid van niemand die !sr gebruikt. Los het op of niet, ik ben een bot en geen agent.",
                "Geen muziek. De wachtrij is overleden aan verwaarlozing. Doe een !sr als je je schuldig voelt. Dat zou je moeten.",
                "Dode lucht. Ik zou zelf wel iets opzetten, maar \"bots die de muziek kiezen\" is blijkbaar \"hoe het de vorige keer misging\".",
                "Stilte. Ergens huilt een dj. !sr, held.",
            ],
            hype:
            [
                "GEEN NUMMER AAN HET SPELEN. FIX HET NU MET !sr.",
                "STILTE? DAT DOEN WE HIER NIET. !sr, GO.",
                "DE SPELER IS LEEG. GOOI EEN !sr ERIN EN REDT ONS ALLEMAAL.",
            ],
            chill: ["er speelt nu niks.", "stil nu. !sr als je wilt.", "geen nummer. is ook prima."]
        );
    }

    private static void AddDutchBaseQueueSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.List,
            informative:
            [
                "Wachtrij ({queue.count}): {queue.list}",
                "Hierna: {queue.list}",
                "{queue.count} in de wachtrij: {queue.list}",
            ],
            friendly:
            [
                "Dit komt eraan: {queue.list}",
                "De wachtrij ziet er goed uit ({queue.count})! {queue.list}",
                "Hierna voor ons: {queue.list}",
            ],
            sassy:
            [
                "{queue.count} nummers diep: {queue.list}. Wil je ook een plekje? !queue @jij.",
                "OFFICIEEL WACHTRIJRAPPORT: {queue.count} nummers. {queue.list}. Klachten gaan naar /dev/null.",
                "Hierna, of je het leuk vindt of niet: {queue.list}",
                "De wachtrij, omdat je het vroeg in plaats van te scrollen: {queue.list}",
            ],
            hype:
            [
                "{queue.count} BANGERS GELADEN: {queue.list}",
                "DE WACHTRIJ STAAT VOL: {queue.list}",
                "DIT KOMT ERAAN EN HET IS ALLEMAAL VUUR: {queue.list}",
            ],
            chill:
            [
                "wachtrij: {queue.list}",
                "hierna: {queue.list}",
                "{queue.count} klaar: {queue.list}",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.Empty,
            informative:
            [
                "De wachtrij is leeg.",
                "Op dit moment niets in de wachtrij.",
                "Geen nummers in de wachtrij.",
            ],
            friendly:
            [
                "De wachtrij is leeg, voeg er een toe met !sr!",
                "Nog niets klaar. Jouw !sr kan de eerste zijn!",
                "Lege wachtrij! Kom op gang met !sr.",
            ],
            sassy:
            [
                "De wachtrij is leeg. Hij vult zichzelf niet. Daar is !sr voor. Dat is de hele afspraak.",
                "Niets in de wachtrij. De dj-booth is een spookstad. Iemand moet !sr doen voordat ik liftmuziek opzet.",
                "Leeg. Nul nummers. De lat ligt op de grond en niemand raapt hem op. !sr.",
                "Status van de wachtrij: 404. Nummers niet gevonden. Je weet wat je moet doen.",
            ],
            hype:
            [
                "DE WACHTRIJ IS LEEG. VUL HEM MET !sr.",
                "NIETS IN DE WACHTRIJ. VERANDER DAT. !sr NU.",
                "LEGE WACHTRIJ ALARM. !sr TOT DE REDDING.",
            ],
            chill: ["wachtrij is leeg.", "niks in de wachtrij. !sr misschien.", "leeg nu."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.Mine,
            informative:
            [
                "Aanvragen van {user}: {queue.mine}",
                "{user} heeft {queue.mine.count} in de wachtrij: {queue.mine}",
                "In de wachtrij voor {user}: {queue.mine}",
            ],
            friendly:
            [
                "Hier staan de nummers van {user}: {queue.mine}",
                "{user}, je staat erin! {queue.mine}",
                "Goed nieuws, {user}: {queue.mine}",
            ],
            sassy:
            [
                "{user}, je nummers, omdat je zo lief vroeg: {queue.mine}",
                "Ja {user}, het staat er nog steeds. {queue.mine}",
                "De claim van {user} op de speakers: {queue.mine}. Even geduld.",
            ],
            hype:
            [
                "{user} STAAT IN DE WACHTRIJ: {queue.mine}",
                "DE BANGERS VAN {user} STAAN KLAAR: {queue.mine}",
                "MAAK JE KLAAR VOOR {user}: {queue.mine}",
            ],
            chill:
            [
                "van {user}: {queue.mine}",
                "die van jou, {user}: {queue.mine}",
                "{user} is zo aan de beurt: {queue.mine}",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Queue.Key,
            BuiltinResponseSlots.Queue.None,
            informative:
            [
                "{user} heeft geen nummers in de wachtrij.",
                "Geen aanvragen van {user} in de wachtrij.",
                "Niets in de wachtrij voor {user}.",
            ],
            friendly:
            [
                "{user} heeft nog niets in de wachtrij. !sr staat open!",
                "Nu geen nummers van {user}. Voeg er een toe met !sr!",
                "De plek van {user} is vrij. Ga ervoor met !sr!",
            ],
            sassy:
            [
                "{user} heeft precies nul nummers in de wachtrij. Gedurfde strategie.",
                "Niets van {user} hierin. Je kunt niet overslaan wat je nooit hebt aangevraagd.",
                "{user}? Niet in de wachtrij. Kijk nog eens na een !sr.",
            ],
            hype:
            [
                "{user} HEEFT NIETS IN DE WACHTRIJ. FIX DAT MET !sr.",
                "NOG GEEN NUMMERS VAN {user}. !sr EN KOM ERIN.",
                "{user} ONTBREEKT IN DE WACHTRIJ. !sr NU.",
            ],
            chill:
            [
                "niks van {user}.",
                "{user} heeft niks in de wachtrij.",
                "geen aanvragen van {user} nu.",
            ]
        );
    }

    private static void AddDutchBaseSongRequestSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Added,
            informative:
            [
                "{track.name} van {track.artist} is aan de wachtrij toegevoegd. {track.link}",
                "In de wachtrij: {track.name} van {track.artist}. {track.link}",
                "{track.name} van {track.artist} staat in de wachtrij. {track.link}",
            ],
            friendly:
            [
                "{track.name} van {track.artist} toegevoegd, goede keuze! {track.link}",
                "Gelukt! {track.name} van {track.artist} staat in de wachtrij. {track.link}",
                "{track.name} van {track.artist} komt eraan, bedankt! {track.link}",
            ],
            sassy:
            [
                "Vooruit. {track.name} van {track.artist} staat in de wachtrij. Ik heb ergere dingen gedraaid. Nauwelijks. {track.link}",
                "{track.name} van {track.artist} toegevoegd. Gedurfd. Genoteerd. Voor eeuwig vastgelegd. {track.link}",
                "{track.name} van {track.artist}? Prima. Het staat erin. De wachtrij oordeelt niet. Ik wel, maar de wachtrij niet. {track.link}",
                "{track.name} van {track.artist} staat erin. Je smaak is toegevoegd aan het dossier. {track.link}",
            ],
            hype:
            [
                "{track.name} VAN {track.artist} STAAT VAST. LET'S GO. {track.link}",
                "{track.name} VAN {track.artist} TOEGEVOEGD. DE WACHTRIJ IS NET BETER GEWORDEN. {track.link}",
                "{track.name} VAN {track.artist} KOMT ERAAN. HOUD JE VAST. {track.link}",
            ],
            chill:
            [
                "{track.name} van {track.artist} toegevoegd. {track.link}",
                "{track.name} staat erin. mooi. {track.link}",
                "{track.name} van {track.artist}, staat erin. {track.link}",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Duplicate,
            informative:
            [
                "\"{track.name}\" staat al in de wachtrij (aangevraagd door {requested.by}).",
                "\"{track.name}\" is al aangevraagd door {requested.by}. Kies een ander nummer en ik voeg het toe.",
                "\"{track.name}\" wacht al in de wachtrij, dankzij {requested.by}.",
            ],
            friendly:
            [
                "Goede smaak! {requested.by} heeft \"{track.name}\" al aangevraagd. Kies een ander nummer en het is van jou.",
                "{requested.by} was je voor met \"{track.name}\"! Heb je nog een ander nummer in gedachten?",
                "\"{track.name}\" staat al in de wachtrij dankzij {requested.by}. Kom maar met een ander nummer.",
            ],
            sassy:
            [
                "\"{track.name}\" staat AL in de wachtrij. {requested.by} was er eerder. Probeer eens te luisteren voordat je aanvraagt.",
                "Alweer? {requested.by} heeft \"{track.name}\" al aangevraagd. De wachtrij is geen loop-pedaal.",
                "Geweigerd. {requested.by} heeft \"{track.name}\" al in de wachtrij gezet. Eén kopie is genoeg, echt.",
                "Ik zet \"{track.name}\" niet twee keer in de wachtrij. {requested.by} was je voor. Scroll de volgende keer omhoog.",
                "Baanbrekende keuze, alleen had {requested.by} \"{track.name}\" eerder bedacht. Kies iets anders.",
                "@{user}, grote geesten denken hetzelfde... of je let gewoon niet op de wachtrij. Hoe dan ook, dat nummer staat al in de wachtrij!",
                "Gefeliciteerd @{user}! Dat nummer staat al in de wachtrij. Misschien de volgende keer even kijken?",
                "Plottwist @{user}: dat nummer staat al in de wachtrij!",
                "Oef @{user}, dat staat al in de wachtrij, schat.",
                "Nieuwsflits: lokale kijker @{user} ontdekt dat de wachtrij niet te doorzoeken is met ctrl+f.",
                "Dat nummer staat al in de wachtrij @{user}, maar geen zorgen, nu weet je het!",
                "Ach lieverd @{user}, dat komt zo al aan de beurt!",
                "Och schatje @{user}... dat nummer staat al in de rij.",
                "Prestatie ontgrendeld @{user}: 'De wachtrij niet gecheckt!'",
                "Leuk weetje @{user}: dat nummer staat al in de wachtrij! Minder leuk weetje: je hebt niet eerst gekeken.",
            ],
            hype:
            [
                "\"{track.name}\" STAAT ER AL IN. {requested.by} WAS ER EERST. GEEF ME EEN NIEUWE BANGER.",
                "{requested.by} HEEFT \"{track.name}\" AL AANGEVRAAGD. GROTE GEESTEN. VOLGENDE.",
                "\"{track.name}\" STAAT AL VAST, VIND EEN NIEUWE.",
            ],
            chill:
            [
                "\"{track.name}\" staat al in de wachtrij. {requested.by} heeft het.",
                "al aangevraagd door {requested.by}. kies een ander.",
                "{requested.by} heeft \"{track.name}\" al gevraagd.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.AlreadyPlaying,
            informative:
            [
                "Dat nummer speelt nu.",
                "Dat is het huidige nummer.",
                "Dat nummer speelt al.",
            ],
            friendly:
            [
                "Dat speelt nu, geniet ervan en kies daarna het volgende!",
                "Je hebt geluk, dat nummer staat al aan.",
                "Goede oren! Dat is het nummer dat nu speelt.",
            ],
            sassy:
            [
                "Dit is LETTERLIJK het nummer dat speelt. Nu. In je oren.",
                "Het speelt TERWIJL WE PRATEN. Nog een keer aanvragen laat het niet harder spelen.",
                "Gedurfd om het nummer aan te vragen dat nu speelt. Geweigerd, met liefde.",
                "Je vraagt het nummer aan dat speelt. Neem even een moment.",
            ],
            hype:
            [
                "DAT IS HET NUMMER DAT NU SPEELT. JE VINDT HET GEWELDIG. WE SNAPPEN HET.",
                "HET STAAT NU AAN. ZET HET LUIDER IN PLAATS DAARVAN.",
                "SPEELT AL. TOP KEUZE TROUWENS.",
            ],
            chill:
            [
                "dat nummer speelt nu.",
                "staat al aan.",
                "speelt al, kies een ander als het klaar is.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotFound,
            informative:
            [
                "Geen nummers gevonden voor \"{query}\".",
                "Ik kon \"{query}\" niet vinden.",
                "Niets kwam overeen met \"{query}\".",
            ],
            friendly:
            [
                "Hm, ik kon \"{query}\" niet vinden. Probeer een andere spelling?",
                "Geen geluk met \"{query}\". Probeer het nog eens!",
                "Ik kon \"{query}\" niet vinden, maar geef niet op!",
            ],
            sassy:
            [
                "\"{query}\"? Overal gezocht. Zelfs onder de bank. Niets.",
                "Nul resultaten voor \"{query}\". Het bestaat niet of je hebt zelf een nummer verzonnen. Indrukwekkend, hoe dan ook.",
                "\"{query}\" leverde niets op. Spelling is gratis, hoor.",
                "404: \"{query}\" niet gevonden. Op geen enkel platform. Misschien niet eens in deze werkelijkheid.",
            ],
            hype:
            [
                "NIETS GEVONDEN VOOR \"{query}\". PROBEER HET OPNIEUW. WE GELOVEN IN JE.",
                "\"{query}\" KWAM LEEG TERUG. LAAD OPNIEUW EN PROBEER HET NOG EENS.",
                "NET ERNAAST OP \"{query}\". GA NOG EEN KEER.",
            ],
            chill:
            [
                "niks voor \"{query}\".",
                "kon \"{query}\" niet vinden. jammer.",
                "geen match voor \"{query}\".",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoCloseMatch,
            informative:
            [
                "Ik kon \"{query}\" niet vinden. Het dichtstbijzijnde was \"{track.name}\" van {track.artist}, dus ik heb het niet toegevoegd.",
                "Geen match voor \"{query}\". Dichtstbijzijnde resultaat: \"{track.name}\" van {track.artist}. Er is niets toegevoegd.",
            ],
            friendly:
            [
                "Hm, ik kon \"{query}\" niet vinden. Het dichtstbijzijnde was \"{track.name}\" van {track.artist}, dus ik heb het weggelaten. Probeer een andere spelling?",
                "Geen geluk met \"{query}\". \"{track.name}\" van {track.artist} kwam het dichtst in de buurt, maar ik heb het niet toegevoegd!",
            ],
            sassy:
            [
                "\"{query}\"? Het dichtstbijzijnde was \"{track.name}\" van {track.artist}. Dat voeg ik niet toe. Graag gedaan.",
                "Geen \"{query}\" te vinden. \"{track.name}\" van {track.artist} kwam in de buurt, maar bijna is niet het nummer.",
            ],
            hype:
            [
                "GEEN MATCH VOOR \"{query}\". DICHTSTBIJZIJNDE WAS \"{track.name}\" VAN {track.artist}. NIET TOEGEVOEGD. PROBEER HET OPNIEUW.",
                "\"{query}\" GEMIST! DICHTSTBIJZIJNDE WAS \"{track.name}\" VAN {track.artist}. WEGGELATEN. GA NOG EEN KEER.",
            ],
            chill:
            [
                "geen match voor \"{query}\". dichtstbij was \"{track.name}\" van {track.artist}, dus ik liet het weg.",
                "kon \"{query}\" niet vinden. \"{track.name}\" van {track.artist} kwam het dichtst in de buurt. niet toegevoegd.",
            ]
        );
    }

    private static void AddDutchBaseSkipSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Skipped,
            informative: ["Doorgegaan naar het volgende nummer."],
            friendly:
            [
                "Overgeslagen! Door naar het volgende.",
                "Klaar, ik heb het voor je overgeslagen!",
                "Volgende! Dat nummer is overgeslagen.",
            ],
            sassy:
            [
                "Overgeslagen. Iemand moest het zeggen. Ik deed het gewoon.",
                "Weg. Over dat nummer praten we niet meer.",
                "Overgeslagen. De wachtrij bedankt je voor je diensten.",
                "Dat nummer is het pand uitgezet. Volgende.",
            ],
            hype:
            [
                "OVERGESLAGEN. VOLGENDE BANGER ONDERWEG.",
                "WEG ERMEE. VOLGENDE, LET'S GO.",
                "OVERGESLAGEN. DOOR NAAR HET VUUR.",
            ],
            chill: ["overgeslagen.", "volgende. overgeslagen.", "weg. door naar het volgende."]
        );
    }

    private static void AddDutchBaseCommandsAndHelpSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Commands.Key,
            BuiltinResponseSlots.Commands.List,
            informative: ["{commands} — Gebruik {prefix}help <commando> voor details."],
            friendly:
            [
                "@{user} dit kun je gebruiken: {commands} — Gebruik {prefix}help <commando> voor details.",
                "@{user} ik help graag, probeer er een van: {commands} — Gebruik {prefix}help <commando> voor details.",
            ],
            sassy:
            [
                "@{user} de commando's zijn: {commands}. Ja, allemaal. Gebruik {prefix}help <commando> voor details, en lees het deze keer helemaal.",
                "@{user} hier is elk commando, want blijkbaar was dat niet duidelijk: {commands} — Gebruik {prefix}help <commando> voor details.",
            ],
            hype:
            [
                "@{user} HIER IS HET HELE ARSENAAL: {commands} — GEBRUIK {prefix}help <commando> VOOR DETAILS.",
            ],
            chill:
            [
                "@{user} commando's: {commands} — gebruik {prefix}help <commando> voor details.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Commands.Key,
            BuiltinResponseSlots.Commands.Empty,
            informative: ["Geen commando's beschikbaar."],
            friendly: ["@{user} er staat nog niets aan, kom snel weer terug!"],
            sassy: ["@{user} geen commando's ingeschakeld. Het is stil. Te stil."],
            hype: ["@{user} NOG NIETS INGESCHAKELD. DE STREAMER LIGT TE SLAPEN."],
            chill: ["@{user} nog niets ingeschakeld."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Help.Key,
            BuiltinResponseSlots.Help.Described,
            informative: ["Commando !{command} — {description}"],
            friendly: ["@{user} goede vraag! !{command} — {description}"],
            sassy: ["@{user} !{command} — {description}. Je had de pins kunnen lezen, maar goed."],
            hype: ["@{user} !{command} — {description}. GA HET NU GEBRUIKEN."],
            chill: ["@{user} commando !{command} — {description}"]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Help.Key,
            BuiltinResponseSlots.Help.Usage,
            informative:
            [
                "Gebruik {prefix}help <commando> voor hulp bij een bepaald commando, of {prefix}commands om te zien wat er beschikbaar is.",
            ],
            friendly:
            [
                "@{user} gebruik {prefix}help <commando> voor hulp bij een bepaald commando, of {prefix}commands om te zien wat er beschikbaar is!",
            ],
            sassy:
            [
                "@{user} het is {prefix}help <commando>. Of {prefix}commands als je de hele lijst wilt. Niet moeilijk.",
            ],
            hype:
            [
                "@{user} GEBRUIK {prefix}help <commando> VOOR EEN BEPAALD COMMANDO, OF {prefix}commands VOOR HET HELE ARSENAAL.",
            ],
            chill:
            [
                "@{user} {prefix}help <commando> voor een bepaald commando, of {prefix}commands om te zien wat er is.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Help.Key,
            BuiltinResponseSlots.Help.Unknown,
            informative:
            [
                "Onbekend commando \"{command}\". Gebruik {prefix}commands om te zien wat er beschikbaar is.",
            ],
            friendly:
            [
                "@{user} hm, ik ken geen commando met de naam \"{command}\". Gebruik {prefix}commands om te zien wat er beschikbaar is!",
            ],
            sassy:
            [
                "@{user} \"{command}\"? Nooit van gehoord. Gebruik {prefix}commands om te zien wat er beschikbaar is.",
            ],
            hype:
            [
                "@{user} \"{command}\" BESTAAT NIET. GEBRUIK {prefix}commands OM TE ZIEN WAT ER WEL IS.",
            ],
            chill:
            [
                "@{user} geen commando \"{command}\". gebruik {prefix}commands om te zien wat er is.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Help.Key,
            BuiltinResponseSlots.Help.NoDescription,
            informative:
            [
                "{prefix}{command} — nog geen helptekst. Gebruik {prefix}commands om te zien wat er beschikbaar is.",
            ],
            friendly:
            [
                "@{user} {prefix}{command} bestaat, maar er is nog geen helptekst voor geschreven. Gebruik {prefix}commands om te zien wat er beschikbaar is!",
            ],
            sassy:
            [
                "@{user} {prefix}{command} bestaat echt, maar komt zonder handleiding. Gebruik {prefix}commands om te zien wat er beschikbaar is.",
            ],
            hype:
            [
                "@{user} {prefix}{command} BESTAAT MAAR HEEFT NOG GEEN HELPTEKST. GEBRUIK {prefix}commands VOOR DE REST.",
            ],
            chill:
            [
                "@{user} {prefix}{command} heeft nog geen helptekst. gebruik {prefix}commands om te zien wat er is.",
            ]
        );
    }

    private static void AddDutchBaseLurkAndAccountAgeSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Lurk.Key,
            BuiltinResponseSlots.Lurk.Lurking,
            informative: ["@{user} is nu aan het lurken. Veel kijkplezier!"],
            friendly: ["@{user} lurkt nu, fijn dat je er nog bent!"],
            sassy: ["@{user} is in lurkmodus. Stil, kijkend, oordelend. Respect."],
            hype: ["@{user} LURKT. TELT NOG STEEDS MEE. NOG STEEDS LEGENDARISCH."],
            chill: ["@{user} lurkt nu."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Lurk.Key,
            BuiltinResponseSlots.Lurk.NotLurking,
            informative: ["@{user} lurkt niet meer. Welkom terug!"],
            friendly: ["@{user} is terug! Fijn om je weer te zien!"],
            sassy: ["@{user} komt uit de schaduw. We zagen niets. We denken het ergste."],
            hype: ["@{user} IS TERUG. DE CHAT IS WEER COMPLEET."],
            chill: ["@{user} is terug."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.Age,
            informative: ["Je account is aangemaakt op {date} ({age} geleden)."],
            friendly: ["Je account is aangemaakt op {date}, {age} geleden. Mooi!"],
            sassy:
            [
                "Je account is geboren op {date}. {age} geleden, en je typt dit nog steeds in de chat. Respect voor de toewijding.",
            ],
            hype: ["ACCOUNT AANGEMAAKT OP {date}. {age} GELEDEN. VETERAANSTATUS VERDIEND."],
            chill: ["account is van {date}, {age} geleden."]
        );
    }

    private static void AddDutchBaseWhisperAndBanSongSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.Usage,
            informative: ["Gebruik: !whisper <gebruiker> <bericht>"],
            friendly: ["Bijna! Probeer: !whisper <gebruiker> <bericht>"],
            sassy:
            [
                "Gebruik: !whisper <gebruiker> <bericht>. Beide delen. Elke keer. Niet optioneel.",
            ],
            hype: ["GEBRUIK: !whisper <gebruiker> <bericht>. VUL HET IN EN VERSTUUR HET."],
            chill: ["gebruik: !whisper <gebruiker> <bericht>"]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.NotFound,
            informative: ["Ik kon geen Twitch-gebruiker met de naam \"{user}\" vinden."],
            friendly:
            [
                "Hm, ik kon geen Twitch-gebruiker met de naam \"{user}\" vinden. Even de spelling checken?",
            ],
            sassy:
            [
                "\"{user}\" is geen Twitch-gebruiker. Gecheckt. Twee keer. Probeer het goed te spellen.",
            ],
            hype: ["GEEN TWITCH-GEBRUIKER MET DE NAAM \"{user}\". CHECK HET EN PROBEER OPNIEUW."],
            chill: ["kon \"{user}\" niet vinden op twitch."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.TwitchUnavailable,
            informative: ["Twitch gaf zojuist geen antwoord. Probeer het zo nog eens."],
            friendly: ["Twitch gaf zojuist geen antwoord. Wil je het zo nog eens proberen?"],
            sassy: ["Twitch geeft geen antwoord. Niet mijn schuld. Probeer het zo nog eens."],
            hype: ["TWITCH IS STIL GEWORDEN. PROBEER HET ZO OPNIEUW."],
            chill: ["twitch gaf geen antwoord. probeer het straks nog eens."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.NotAvailable,
            informative: ["Whisperen is op dit moment niet beschikbaar."],
            friendly: ["Whisperen is nu niet beschikbaar, sorry daarvoor!"],
            sassy: ["Whisperen is nu niet beschikbaar. Spreek het platform erop aan, niet mij."],
            hype: ["WHISPEREN LIGT PLAT. NIETS OM TE VERSTUREN."],
            chill: ["whisperen is nu niet beschikbaar."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.CapReached,
            informative:
            [
                "De bot heeft de dagelijkse limiet van Twitch van 40 nieuwe whisper-ontvangers bereikt. Probeer het morgen opnieuw.",
            ],
            friendly:
            [
                "Ik heb vandaag 40 nieuwe mensen gewhisperd, dat is de dagelijkse limiet van Twitch. Probeer het morgen opnieuw!",
            ],
            sassy:
            [
                "Twitch vindt 40 nieuwe whisper-vrienden per dag genoeg. Probeer het morgen opnieuw.",
            ],
            hype: ["VANDAAG 40 NIEUWE WHISPERS, DAT IS DE LIMIET VAN TWITCH! MORGEN WEER!"],
            chill:
            [
                "de dagelijkse twitch-limiet van 40 nieuwe whisper-mensen is bereikt. probeer morgen opnieuw.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.Nothing,
            informative: ["Er speelt op dit moment geen nummer!"],
            friendly: ["Er speelt nu niets, dus er valt niets te bannen!"],
            sassy:
            [
                "Er speelt niets. Stilte bannen zou een gedurfd nieuw terrein zijn. Laten we dat niet doen.",
            ],
            hype: ["GEEN NUMMER AAN HET SPELEN. NIETS OM TE BANNEN. ZET EERST EEN NUMMER AAN."],
            chill: ["er speelt niks. niks om te bannen."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.CouldNotBan,
            informative: ["Kon dat nummer niet bannen. Probeer het zo nog eens."],
            friendly: ["Ik kon dat nummer net niet bannen. Wil je het zo nog eens proberen?"],
            sassy:
            [
                "Dat nummer is niet te bannen. Het leeft voort. Voorlopig. Probeer het zo nog eens.",
            ],
            hype: ["KON DAT NUMMER NIET BANNEN. PROBEER HET ZO OPNIEUW."],
            chill: ["kon dat nummer niet bannen. probeer het straks nog eens."]
        );
    }

    private static void AddDutchBaseVolumeSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.Usage,
            informative:
            [
                "Geef een geldig volumeniveau tussen 0 en 100 op: !volume <niveau> (0-100).",
            ],
            friendly: ["Bijna! Kies een volumeniveau tussen 0 en 100: !volume <niveau> (0-100)."],
            sassy:
            [
                "Een getal. Tussen nul en honderd. Meer is het niet: !volume <niveau> (0-100).",
            ],
            hype: ["KIES EEN GETAL VAN 0 TOT 100 EN VERSTUUR HET: !volume <niveau> (0-100)."],
            chill: ["volume heeft een niveau van 0 tot 100 nodig: !volume <niveau> (0-100)."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.CannotRead,
            informative: ["Er speelt op dit moment geen nummer!"],
            friendly: ["Er speelt niets, dus er is geen volume om te lezen of te wijzigen!"],
            sassy: ["Aan het volume van stilte kun je niet komen. Zet eerst een nummer aan."],
            hype:
            [
                "GEEN NUMMER AAN HET SPELEN. GEEN VOLUME OM AAN TE RAKEN. START EERST EEN NUMMER.",
            ],
            chill: ["er speelt niks, dus geen volume."]
        );
    }

    private static void AddDutchBaseUpdateSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.NotFound,
            informative: ["Ik kon gebruiker '{user}' niet vinden op Twitch."],
            friendly: ["Ik kon '{user}' niet vinden op Twitch. Wil je de spelling checken?"],
            sassy: ["'{user}' bestaat niet op Twitch. Niet mijn schuld. Check de naam."],
            hype: ["GEEN GEBRUIKER '{user}' OP TWITCH. PROBEER HET OPNIEUW."],
            chill: ["kon '{user}' niet vinden op twitch."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.TwitchUnavailable,
            informative: ["@{user} Twitch gaf zojuist geen antwoord. Probeer het zo nog eens."],
            friendly:
            [
                "@{user} Twitch gaf zojuist geen antwoord. Wil je het zo nog eens proberen?",
            ],
            sassy:
            [
                "@{user} Twitch geeft geen antwoord. Niet mijn schuld. Probeer het zo nog eens.",
            ],
            hype: ["@{user} TWITCH IS STIL GEWORDEN. PROBEER HET ZO OPNIEUW."],
            chill: ["@{user} twitch gaf geen antwoord. probeer het straks nog eens."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.UpdateFailed,
            informative: ["Er ging iets mis bij het bijwerken van {user}."],
            friendly:
            [
                "Hm, er ging iets mis bij het bijwerken van {user}. Wil je het opnieuw proberen?",
            ],
            sassy:
            [
                "Er ging iets mis bij het bijwerken van {user}. Niet mijn beste moment. Probeer het opnieuw.",
            ],
            hype: ["BIJWERKEN MISLUKT VOOR {user}. PROBEER HET OPNIEUW."],
            chill: ["er ging iets mis bij het bijwerken van {user}."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.LoginUnresolved,
            informative: ["@{user} je Twitch-login kon niet worden opgelost."],
            friendly:
            [
                "@{user} ik kon je Twitch-login daar niet achterhalen. Wil je het opnieuw proberen?",
            ],
            sassy:
            [
                "@{user} je Twitch-login kon niet worden opgelost. Dat ligt aan jou, niet aan mij.",
            ],
            hype: ["@{user} JE TWITCH-LOGIN KON NIET WORDEN OPGELOST. PROBEER HET OPNIEUW."],
            chill: ["@{user} kon je twitch-login niet oplossen."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.OwnInfoOnly,
            informative:
            [
                "@{user} je kunt alleen je eigen gegevens bijwerken, of mod zijn om andere te updaten.",
            ],
            friendly:
            [
                "@{user} je kunt voorlopig alleen je eigen gegevens verversen, mods kunnen die van anderen bijwerken!",
            ],
            sassy:
            [
                "@{user} je kunt alleen je eigen gegevens bijwerken. Mods krijgen het extra voorrecht. Jij niet.",
            ],
            hype: ["@{user} ALLEEN JE EIGEN GEGEVENS. MODS KRIJGEN DE REST."],
            chill: ["@{user} je kunt alleen je eigen gegevens bijwerken."]
        );
    }

    private static void AddDutchBaseGameAndStatusSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        foreach (string game in new[] { "coinflip", "dice", "slots" })
        {
            Translate(
                catalog,
                Dutch,
                game,
                BuiltinResponseSlots.Game.AccountUnresolved,
                informative: ["Je account kon niet worden opgelost. Probeer het opnieuw."],
                friendly: ["Ik kon je account daar niet vinden. Wil je het opnieuw proberen?"],
                sassy: ["Je account kon niet worden opgelost. Raar. Probeer het opnieuw."],
                hype: ["JE ACCOUNT KON NIET WORDEN OPGELOST. PROBEER HET OPNIEUW."],
                chill: ["kon je account niet oplossen. probeer het opnieuw."]
            );
        }

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequestErrors.Disabled,
            informative: ["Dit commando staat op dit moment uit."],
            friendly: ["Dit commando staat nu niet aan, sorry!"],
            sassy: ["Dit commando staat uit. Bespreek het met de streamer."],
            hype: ["DIT COMMANDO STAAT NU UIT."],
            chill: ["dit commando staat nu uit."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.BotStatus.Key,
            BuiltinResponseSlots.BotStatus.GoingOffline,
            informative: ["Herstart voor een update, zo terug."],
            friendly: ["Snelle herstart voor een update, ik ben zo terug!"],
            sassy: ["Ik ga neer voor een herstart. Probeer me niet te erg te missen."],
            hype: ["HERSTART VOOR EEN UPDATE. IN EEN FLITS TERUG."],
            chill: ["herstart voor een update, zo terug."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.BotStatus.Key,
            BuiltinResponseSlots.BotStatus.ConnectionLost,
            informative:
            [
                "⚠️ Verbinding met Twitch-events verbroken. Kanaalpunten-redeems en commando's staan op pauze terwijl ik opnieuw verbind. Even geduld!",
            ],
            friendly:
            [
                "Ik ben mijn verbinding met Twitch-events kwijt. Redeems en commando's staan op pauze terwijl ik opnieuw verbind. Even geduld!",
            ],
            sassy:
            [
                "Twitch heeft mijn event-verbinding verbroken. Redeems en commando's staan op pauze terwijl ik het oplos.",
            ],
            hype:
            [
                "TWITCH-EVENTVERBINDING KWIJT. REDEEMS EN COMMANDO'S STAAN OP PAUZE TERWIJL IK OPNIEUW VERBIND. EVEN GEDULD!",
            ],
            chill:
            [
                "twitch-eventverbinding kwijt, redeems en commando's staan op pauze terwijl ik opnieuw verbind. even geduld.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.BotStatus.Key,
            BuiltinResponseSlots.BotStatus.ConnectionRestored,
            informative: ["✅ Weer verbonden! Kanaalpunten-redeems en commando's werken weer."],
            friendly: ["Ik ben terug! Redeems en commando's werken weer."],
            sassy: ["Weer verbonden. Redeems en commando's werken weer, graag gedaan."],
            hype: ["WEER VERBONDEN! REDEEMS EN COMMANDO'S WERKEN WEER!"],
            chill: ["weer verbonden, redeems en commando's werken weer."]
        );
    }
}
