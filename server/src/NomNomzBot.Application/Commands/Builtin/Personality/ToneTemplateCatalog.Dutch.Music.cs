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

/// <summary>Dutch text of the music built-ins; the English text is in <c>ToneTemplateCatalog.Music.cs</c>.</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddDutchMusicSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddDutchQueueSlots(catalog);
        AddDutchSongRequestSlots(catalog);
        AddDutchSkipSlots(catalog);
        AddDutchVolumeAndBanSongSlots(catalog);
        AddDutchBangerAndPlaylistSlots(catalog);
        AddDutchPlaybackControlSlots(catalog);
    }

    private static void AddDutchQueueSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.CloseQueue.Key,
            BuiltinResponseSlots.CloseQueue.Closed,
            informative: ["De wachtrij voor nummers is nu gesloten."],
            friendly: ["De wachtrij is even dicht. Bedankt voor alle verzoekjes!"],
            sassy: ["Wachtrij dicht. De DJ moet even bijkomen."],
            hype: ["WACHTRIJ DICHT! BEDANKT VOOR ALLE VERZOEKJES!"],
            chill: ["de wachtrij is nu dicht."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.CloseQueue.Key,
            BuiltinResponseSlots.CloseQueue.AlreadyClosed,
            informative: ["De wachtrij voor nummers is al gesloten."],
            friendly: ["De wachtrij was al dicht!"],
            sassy: ["Al dicht. Je kunt niets twee keer sluiten."],
            hype: ["DE WACHTRIJ IS AL DICHT!"],
            chill: ["de wachtrij was al dicht."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.CloseQueue.Key,
            BuiltinResponseSlots.CloseQueue.Failed,
            informative: ["De wachtrij sluiten is niet gelukt. Probeer het zo nog eens."],
            friendly: ["Hm, de wachtrij sluiten werkte niet. Probeer het zo nog eens!"],
            sassy: ["De wachtrij weigert dicht te gaan. Probeer het nog eens."],
            hype: ["WACHTRIJ SLUITEN MISLUKT! PROBEER HET NOG EENS!"],
            chill: ["wachtrij sluiten lukte niet. probeer het zo weer."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.OpenQueue.Key,
            BuiltinResponseSlots.OpenQueue.Opened,
            informative: ["De wachtrij voor nummers is nu open."],
            friendly: ["De wachtrij is weer open. Stuur je verzoekjes maar in!"],
            sassy: ["Wachtrij open. Laat je keuze tellen."],
            hype: ["WACHTRIJ OPEN! STUUR DIE VERZOEKJES MAAR IN!"],
            chill: ["de wachtrij is weer open."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.OpenQueue.Key,
            BuiltinResponseSlots.OpenQueue.AlreadyOpen,
            informative: ["De wachtrij voor nummers is al open."],
            friendly: ["De wachtrij was al open!"],
            sassy: ["Al open. Ga eens iets aanvragen."],
            hype: ["DE WACHTRIJ IS AL OPEN!"],
            chill: ["de wachtrij was al open."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.OpenQueue.Key,
            BuiltinResponseSlots.OpenQueue.Failed,
            informative: ["De wachtrij openen is niet gelukt. Probeer het zo nog eens."],
            friendly: ["Hm, de wachtrij openen werkte niet. Probeer het zo nog eens!"],
            sassy: ["De wachtrij weigert open te gaan. Probeer het nog eens."],
            hype: ["WACHTRIJ OPENEN MISLUKT! PROBEER HET NOG EENS!"],
            chill: ["wachtrij openen lukte niet. probeer het zo weer."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.ClearSongs.Usage,
            informative: ["Gebruik: !clearsongs <gebruiker>"],
            friendly: ["Zeg maar van wie ik de nummers moet wissen! !clearsongs <gebruiker>"],
            sassy: ["Van wie? Probeer: !clearsongs <gebruiker>"],
            hype: ["VAN WIE? !clearsongs <gebruiker>"],
            chill: ["van wie? !clearsongs <gebruiker>"]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.ClearSongs.Cleared,
            informative: ["{count} nummers van {target} uit de wachtrij gehaald."],
            friendly: ["Klaar! {count} nummers van {target} zijn uit de wachtrij gehaald."],
            sassy: ["Weg. {count} nummers van {target} staan niet meer in de wachtrij."],
            hype: ["{count} NUMMERS VAN {target} OPGERUIMD!"],
            chill: ["{count} nummers van {target} gewist."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.ClearSongs.NothingQueued,
            informative: ["{target} heeft geen nummers in de wachtrij."],
            friendly: ["{target} heeft geen nummers in de wachtrij staan!"],
            sassy: ["{target} heeft niets in de wachtrij. Er valt niets te wissen."],
            hype: ["{target} HEEFT GEEN NUMMERS IN DE WACHTRIJ!"],
            chill: ["{target} heeft niets in de wachtrij."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.ClearSongs.Key,
            BuiltinResponseSlots.ClearSongs.Failed,
            informative:
            [
                "De nummers van {target} wissen is niet gelukt. Probeer het zo nog eens.",
            ],
            friendly: ["Hm, de nummers van {target} wissen werkte niet. Probeer het zo nog eens!"],
            sassy: ["De nummers van {target} willen niet weg. Probeer het nog eens."],
            hype: ["NUMMERS VAN {target} WISSEN MISLUKT! PROBEER HET NOG EENS!"],
            chill: ["nummers van {target} wissen lukte niet. probeer het zo weer."]
        );
    }

    private static void AddDutchSongRequestSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Usage,
            informative: ["Gebruik: !sr <naam van het nummer of URL>"],
            friendly: ["Zeg maar welk nummer je wilt! !sr <naam van het nummer of URL>"],
            sassy: ["Je bent het nummer vergeten. Probeer: !sr <naam van het nummer of URL>"],
            hype: ["WELK NUMMER? !sr <naam van het nummer of URL>"],
            chill: ["kies een nummer: !sr <naam van het nummer of URL>"]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.RequestsOff,
            informative: ["Nummerverzoeken staan uit in dit kanaal."],
            friendly: ["Nummerverzoeken hebben even pauze. Kom straks nog eens terug!"],
            sassy: ["Verzoeken staan uit. De DJ heeft het pand verlaten."],
            hype: ["NUMMERVERZOEKEN STAAN NU UIT. KOM LATER TERUG."],
            chill: ["verzoeken staan nu uit."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.QueueClosed,
            informative: ["De wachtrij voor nummers is gesloten."],
            friendly: ["De wachtrij is even dicht. Kom straks nog eens terug!"],
            sassy: ["De wachtrij is dicht. De DJ neemt geen verzoeken aan."],
            hype: ["DE WACHTRIJ IS DICHT. VOORLOPIG GEEN VERZOEKEN MEER."],
            chill: ["de wachtrij is nu dicht."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Revoked,
            informative: ["Stop met nummers aanvragen, je toestemming is ingetrokken."],
            friendly:
            [
                "Je toestemming om nummers aan te vragen is ingetrokken, dus ik kan dit nummer niet toevoegen.",
            ],
            sassy: ["Je verzoeken zijn ingetrokken. Stop met vragen."],
            hype: ["VERZOEKEN INGETROKKEN. STOP MET VRAGEN."],
            chill: ["je verzoeken zijn ingetrokken."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackBlocked,
            informative: ["\"{track.name}\" is geblokkeerd in dit kanaal."],
            friendly:
            [
                "Sorry, \"{track.name}\" staat niet op de lijst van dit kanaal. Probeer een ander nummer!",
            ],
            sassy:
            [
                "\"{track.name}\" is hier verbannen. Daar is een reden voor. Kies iets anders.",
            ],
            hype: ["\"{track.name}\" IS GEBLOKKEERD. KIES EEN ANDERE BANGER."],
            chill: ["\"{track.name}\" is hier geblokkeerd. kies een ander nummer."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoActiveDevice,
            informative:
            [
                "\"{track.name}\" kon niet in de wachtrij: er speelt nu niets op een apparaat. Start het afspelen en probeer het opnieuw.",
            ],
            friendly:
            [
                "Ik kon \"{track.name}\" niet in de wachtrij zetten, want er speelt nog niets. Start het afspelen en probeer het opnieuw!",
            ],
            sassy:
            [
                "\"{track.name}\" zou top zijn, als er iets speelde. Start eerst het afspelen.",
            ],
            hype:
            [
                "ER SPEELT NIETS OP EEN APPARAAT. START HET AFSPELEN EN PROBEER \"{track.name}\" DAN OPNIEUW.",
            ],
            chill:
            [
                "er speelt nergens iets, dus \"{track.name}\" kwam niet in de wachtrij. start het afspelen en probeer opnieuw.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoOverlayPlayer,
            informative:
            [
                "\"{track.name}\" kon niet in de wachtrij: de audio-overlay is niet open. Open hem in OBS om YouTube-verzoeken af te spelen.",
            ],
            friendly:
            [
                "Ik kon \"{track.name}\" niet in de wachtrij zetten, want de audio-overlay staat niet open. Open hem in OBS om YouTube-verzoeken af te spelen!",
            ],
            sassy:
            [
                "\"{track.name}\" is YouTube en de audio-overlay is dicht. Open hem eerst in OBS.",
            ],
            hype:
            [
                "DE AUDIO-OVERLAY IS DICHT. OPEN HEM IN OBS EN PROBEER \"{track.name}\" DAN OPNIEUW.",
            ],
            chill:
            [
                "de audio-overlay is niet open, dus \"{track.name}\" kwam niet in de wachtrij. open hem in obs voor youtube-verzoeken.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.PremiumRequired,
            informative:
            [
                "\"{track.name}\" kon niet in de wachtrij: daarvoor is een Premium-account nodig.",
            ],
            friendly:
            [
                "Ik kon \"{track.name}\" niet in de wachtrij zetten. Het muziekaccount heeft daarvoor Premium nodig.",
            ],
            sassy: ["\"{track.name}\" vraagt om Premium. De gratis versie zegt nee."],
            hype: ["\"{track.name}\" VRAAGT OM PREMIUM. DE GRATIS VERSIE ZEI NEE."],
            chill: ["\"{track.name}\" heeft een premium-account nodig."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.AuthFailed,
            informative:
            [
                "\"{track.name}\" kon niet in de wachtrij: de muziekverbinding moet opnieuw worden gekoppeld.",
            ],
            friendly:
            [
                "Ik kon \"{track.name}\" niet in de wachtrij zetten. De muziekverbinding moet opnieuw worden gekoppeld.",
            ],
            sassy: ["\"{track.name}\" zit vast. De muziekverbinding is uitgelogd."],
            hype:
            [
                "MUZIEKVERBINDING VERBROKEN. \"{track.name}\" KWAM NIET IN DE WACHTRIJ. KOPPEL HEM OPNIEUW.",
            ],
            chill: ["muziekverbinding weg, \"{track.name}\" kwam niet in de wachtrij."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Forbidden,
            informative:
            [
                "\"{track.name}\" kon niet in de wachtrij: de muziekverbinding heeft daar geen toestemming voor.",
            ],
            friendly:
            [
                "Ik kon \"{track.name}\" niet in de wachtrij zetten. De muziekverbinding mag dat niet.",
            ],
            sassy:
            [
                "\"{track.name}\" kwam niet in de wachtrij. De muziekverbinding mag er niet aan komen.",
            ],
            hype:
            [
                "\"{track.name}\" KWAM NIET IN DE WACHTRIJ. DE MUZIEKVERBINDING HEEFT GEEN TOESTEMMING.",
            ],
            chill: ["geen toestemming om \"{track.name}\" in de wachtrij te zetten."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackTooLong,
            informative:
            [
                "Toevoegen mislukt. \"{track.name}\" is langer dan de maximale duur van {track.max}.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" is langer dan de limiet van {track.max}, dus ik kon het niet toevoegen. Kies een korter nummer!",
            ],
            sassy:
            [
                "\"{track.name}\" duurt langer dan {track.max}. Dit is een wachtrij, geen marathon.",
            ],
            hype:
            [
                "\"{track.name}\" DUURT LANGER DAN {track.max}. TE LANG. ZOEK EEN KORTERE BANGER.",
            ],
            chill:
            [
                "\"{track.name}\" duurt langer dan {track.max}. te lang, kies een korter nummer.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackExplicit,
            informative:
            [
                "\"{track.name}\" is explicit, en explicit nummers zijn hier niet toegestaan.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" is als explicit gemarkeerd en dit kanaal draait die niet. Kies een schone versie!",
            ],
            sassy: ["\"{track.name}\" is explicit. Dit kanaal doet niet aan explicit."],
            hype: ["\"{track.name}\" IS EXPLICIT. NIET HIER. ZOEK EEN SCHONE BANGER."],
            chill: ["\"{track.name}\" is explicit. mag hier niet, kies een schone versie."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotEmbeddable,
            informative:
            [
                "\"{track.name}\" kan hier niet worden afgespeeld: de uploader staat dat op andere sites niet toe. Probeer een andere upload van het nummer.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" kan niet op de stream spelen, de uploader blokkeert dat. Een andere upload werkt misschien wel!",
            ],
            sassy:
            [
                "\"{track.name}\" zit door de uploader vastgeklonken aan YouTube. Zoek een upload die wel afspeelt.",
            ],
            hype: ["\"{track.name}\" IS DOOR DE UPLOADER VERGRENDELD. ZOEK EEN ANDERE UPLOAD!"],
            chill:
            [
                "\"{track.name}\" kan hier niet spelen, de uploader blokkeert het. probeer een andere upload.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.AgeRestricted,
            informative:
            [
                "\"{track.name}\" heeft een leeftijdsbeperking op YouTube en kan niet op de stream spelen. Probeer een andere upload van het nummer.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" heeft een leeftijdsbeperking op YouTube en kan niet op de stream spelen. Een andere upload werkt misschien wel!",
            ],
            sassy:
            [
                "\"{track.name}\" heeft een leeftijdsbeperking. Die draai ik niet. Zoek een andere upload.",
            ],
            hype:
            [
                "\"{track.name}\" HEEFT EEN LEEFTIJDSBEPERKING. KAN HET NIET SPELEN. ZOEK EEN ANDERE UPLOAD!",
            ],
            chill:
            [
                "\"{track.name}\" heeft een leeftijdsbeperking, kan hier niet spelen. probeer een andere upload.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.LiveBroadcast,
            informative:
            [
                "Livestreams en premières kunnen niet worden aangevraagd. Stuur een link naar een gewone video.",
            ],
            friendly:
            [
                "Dat is een livestream of een première, dus ik kan hem niet in de wachtrij zetten. Een link naar een gewone video werkt wel!",
            ],
            sassy: ["Dat is een livestream. Die houdt nooit op. Stuur een gewone video."],
            hype: ["GEEN LIVESTREAMS OF PREMIÈRES! STUUR EEN GEWONE VIDEO!"],
            chill: ["livestreams en premières kunnen niet. stuur een gewone video."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackMissing,
            informative: ["Die video bestaat niet of is privé."],
            friendly:
            [
                "Ik kan die video niet vinden, hij is misschien verwijderd of privé. Heb je een andere link?",
            ],
            sassy: ["Die video is weg of privé. Probeer er een die bestaat."],
            hype: ["DIE VIDEO IS WEG OF PRIVÉ! PROBEER EEN ANDERE!"],
            chill: ["die video is verwijderd of privé. probeer een andere."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.UnreadableLink,
            informative:
            [
                "Ik kon die link niet lezen. Plak de volledige videolink, of stuur de naam van het nummer.",
            ],
            friendly:
            [
                "Hm, ik kon die link niet lezen. Probeer de volledige videolink, of gewoon de naam van het nummer!",
            ],
            sassy:
            [
                "Die link is onleesbaar. De volledige videolink of de naam van het nummer graag.",
            ],
            hype:
            [
                "KAN DIE LINK NIET LEZEN! STUUR DE VOLLEDIGE VIDEOLINK OF DE NAAM VAN HET NUMMER!",
            ],
            chill:
            [
                "kon die link niet lezen. stuur de volledige videolink of de naam van het nummer.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.LengthUnknown,
            informative:
            [
                "Ik kon niet zien hoe lang die video duurt, dus hij kan niet worden aangevraagd. Probeer een andere upload.",
            ],
            friendly:
            [
                "Hm, YouTube vertelt me niet hoe lang die video duurt, dus ik kan hem niet in de wachtrij zetten. Een andere upload werkt misschien wel!",
            ],
            sassy:
            [
                "Van die video kan ik de lengte niet lezen. Ik zet geen mysterie in de wachtrij. Probeer een andere upload.",
            ],
            hype:
            [
                "GEEN LENGTE BIJ DIE VIDEO! KAN HEM NIET IN DE WACHTRIJ ZETTEN! PROBEER EEN ANDERE UPLOAD!",
            ],
            chill:
            [
                "kan niet zien hoe lang die video duurt, dus niet in de wachtrij. probeer een andere upload.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotMusic,
            informative:
            [
                "Die video kan niet worden aangevraagd. YouTube-verzoeken moeten in de categorie Muziek van YouTube staan of van een officieel artiestenkanaal (- Topic) komen. Stuur een YouTube Music-link, of de naam van een nummer.",
            ],
            friendly:
            [
                "Oei, die kan ik niet in de wachtrij zetten! YouTube-verzoeken moeten in de categorie Muziek van YouTube staan of van een officieel artiestenkanaal (- Topic) komen. Stuur me een YouTube Music-link, of gewoon de naam van een nummer!",
            ],
            sassy:
            [
                "Die zet ik er niet in. YouTube-verzoeken moeten in de categorie Muziek van YouTube staan of van een officieel artiestenkanaal (- Topic) komen, en die video is geen van beide. Stuur een YouTube Music-link, of de naam van een nummer.",
            ],
            hype:
            [
                "GEEN MUZIEK! YOUTUBE-VERZOEKEN MOETEN IN DE CATEGORIE MUZIEK VAN YOUTUBE STAAN OF VAN EEN OFFICIEEL ARTIESTENKANAAL (- TOPIC) KOMEN! STUUR EEN YOUTUBE MUSIC-LINK, OF DE NAAM VAN EEN NUMMER!",
            ],
            chill:
            [
                "die kan ik niet in de wachtrij zetten. youtube-verzoeken moeten in de categorie muziek van youtube staan of van een officieel artiestenkanaal (- topic) komen. stuur een youtube music-link, of de naam van een nummer.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.QuotaExhausted,
            informative:
            [
                "De dagelijkse limiet van YouTube is op. Nummerverzoeken werken weer na {retry.time}.",
            ],
            friendly:
            [
                "YouTube zegt dat de verzoeken van vandaag op zijn! Na {retry.time} kun je weer nummers aanvragen.",
            ],
            sassy:
            [
                "YouTube heeft ons voor vandaag afgesloten. Na {retry.time} kun je weer verzoeken doen.",
            ],
            hype: ["YOUTUBE-LIMIET BEREIKT VOOR VANDAAG! VERZOEKEN WERKEN WEER NA {retry.time}!"],
            chill:
            [
                "de dagelijkse limiet van youtube is op. verzoeken werken weer na {retry.time}.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.PerStreamLimit,
            informative:
            [
                "Je hebt deze stream al {request.limit} nummer(s) aangevraagd. Dat is de limiet.",
            ],
            friendly:
            [
                "Je hebt je {request.limit} verzoek(en) voor deze stream gebruikt. Tot de volgende stream!",
            ],
            sassy: ["{request.limit} verzoek(en) per stream. Die van jou zijn op."],
            hype:
            [
                "DAT WAREN JE {request.limit} VERZOEK(EN) VOOR DEZE STREAM. TOT DE VOLGENDE KEER!",
            ],
            chill: ["je {request.limit} verzoek(en) voor deze stream zijn op."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.TrackTooLongRefunded,
            informative:
            [
                "Toevoegen mislukt. \"{track.name}\" is langer dan de maximale duur van {track.max}, je punt is teruggegeven.",
            ],
            friendly:
            [
                "Sorry, \"{track.name}\" is langer dan de limiet van {track.max}, dus ik kon het niet toevoegen. Je punten zijn terug, kies een korter nummer!",
            ],
            sassy:
            [
                "\"{track.name}\" duurt langer dan {track.max}. Dit is een wachtrij, geen marathon. Punten teruggegeven.",
            ],
            hype:
            [
                "\"{track.name}\" DUURT LANGER DAN {track.max}. TE LANG. PUNTEN TERUG, PROBEER HET OPNIEUW.",
            ],
            chill:
            [
                "\"{track.name}\" duurt langer dan {track.max}. punten terug, kies een korter nummer.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.AddedWithCode,
            informative:
            [
                "Toegevoegd aan de wachtrij: {track.name} van {track.artist} (code {request.code})",
            ],
            friendly:
            [
                "\"{track.name}\" van {track.artist} staat in de wachtrij! (code {request.code})",
            ],
            sassy:
            [
                "Vooruit, {track.name} van {track.artist} staat in de wachtrij. (code {request.code})",
            ],
            hype: ["{track.name} VAN {track.artist} STAAT IN DE WACHTRIJ! (CODE {request.code})"],
            chill: ["{track.name} van {track.artist} staat erin. (code {request.code})"]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.QueueFull,
            informative:
            [
                "De wachtrij is vol (maximaal {queue.max}). Probeer het opnieuw als hij korter is.",
            ],
            friendly:
            [
                "De wachtrij is nu vol (maximaal {queue.max}). Probeer het straks nog eens!",
            ],
            sassy: ["De wachtrij zit vol op {queue.max}. Geduld is gratis."],
            hype: ["DE WACHTRIJ IS VOL OP {queue.max}. HOUD JE BANGERS NOG EVEN VAST."],
            chill: ["wachtrij is vol ({queue.max}). probeer het later."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.PerUserLimit,
            informative:
            [
                "Je hebt al {request.limit} verzoek(en) in de wachtrij. Wacht tot er een gespeeld is voordat je er meer toevoegt.",
            ],
            friendly:
            [
                "Je hebt al {request.limit} verzoek(en) in de wachtrij. Wacht tot er een gespeeld is, dan kun je er meer toevoegen!",
            ],
            sassy:
            [
                "{request.limit} verzoek(en) per persoon. Die van jou zijn op. Wacht op je beurt.",
            ],
            hype: ["JE HEBT AL {request.limit} IN DE WACHTRIJ. LAAT EERST EEN NUMMER SPELEN."],
            chill: ["je hebt al {request.limit} in de wachtrij. wacht tot er een speelt."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.MissingScope,
            informative: ["De muziekverbinding moet opnieuw worden gekoppeld."],
            friendly:
            [
                "De muziekverbinding moet opnieuw worden gekoppeld. De streamer kan dat doen in het dashboard.",
            ],
            sassy: ["De muziekverbinding is kapot. Dat mag de streamer fixen."],
            hype: ["DE MUZIEKVERBINDING MOET OPNIEUW WORDEN GEKOPPELD."],
            chill: ["de muziekverbinding moet opnieuw gekoppeld worden."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotConfigured,
            informative:
            [
                "YouTube-verzoeken zijn nog niet ingesteld op deze bot. De eigenaar van de bot moet een YouTube API-sleutel toevoegen.",
            ],
            friendly:
            [
                "YouTube-verzoeken zijn nog niet ingesteld op deze bot. De eigenaar van de bot moet een YouTube API-sleutel toevoegen.",
            ],
            sassy:
            [
                "YouTube-verzoeken zijn hier niet ingesteld. De eigenaar van de bot is de YouTube API-sleutel vergeten.",
            ],
            hype:
            [
                "YOUTUBE-VERZOEKEN ZIJN NOG NIET INGESTELD. DE EIGENAAR VAN DE BOT MOET EEN YOUTUBE API-SLEUTEL TOEVOEGEN.",
            ],
            chill:
            [
                "youtube-verzoeken zijn nog niet ingesteld. de eigenaar van de bot moet een api-sleutel toevoegen.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.ProviderUnavailable,
            informative: ["De muziekdienst is tijdelijk niet beschikbaar."],
            friendly: ["De muziekdienst heeft een moment. Probeer het zo nog eens!"],
            sassy: ["De muziekdienst ligt eruit. Deze keer ligt het niet aan mij."],
            hype: ["DE MUZIEKDIENST LIGT ER EVEN UIT. PROBEER HET STRAKS OPNIEUW."],
            chill: ["muziekdienst ligt er even uit."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.RateLimited,
            informative:
            [
                "De muziekdienst beperkt dit kanaal tijdelijk. Probeer het over ongeveer {retry.minutes} minuut/minuten nog eens.",
            ],
            friendly:
            [
                "De muziekdienst heeft even adem nodig. Probeer het over ongeveer {retry.minutes} minuut/minuten nog eens!",
            ],
            sassy:
            [
                "De muziekdienst heeft ons {retry.minutes} minuut/minuten afgesloten. Geef de limiet de schuld.",
            ],
            hype:
            [
                "DE MUZIEKDIENST HEEFT {retry.minutes} MINUUT/MINUTEN NODIG. HOUD DAT VERZOEK NOG EVEN VAST.",
            ],
            chill:
            [
                "de muziekdienst beperkt ons. probeer het over {retry.minutes} minuut/minuten opnieuw.",
            ]
        );

        AddDutchSongRequestSourceSlots(catalog);
    }

    private static void AddDutchSongRequestSourceSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.UnsupportedContent,
            informative:
            [
                "Je kunt alleen losse nummers aanvragen. Die link is een playlist, album, aflevering, show of artiestenpagina. Plak de link van één nummer, of zoek gewoon op naam.",
            ],
            friendly:
            [
                "Ik kan alleen losse nummers aannemen, geen playlists, albums of artiestenpagina's. Plak de link van een nummer of zoek op naam!",
            ],
            sassy:
            [
                "Dat is een hele playlist, album of artiest. Ik neem één nummer tegelijk. Plak de link van een nummer of zoek op naam.",
            ],
            hype:
            [
                "ÉÉN NUMMER TEGELIJK! GEEN PLAYLISTS, ALBUMS OF ARTIESTENPAGINA'S. PLAK DE LINK VAN EEN NUMMER OF ZOEK OP NAAM.",
            ],
            chill: ["alleen losse nummers graag. plak de link van een nummer of zoek op naam."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.UnsupportedLink,
            informative:
            [
                "Die link kan hier niet worden gebruikt. Stuur de naam van een nummer, of een link van de muziekdienst die dit kanaal gebruikt.",
            ],
            friendly:
            [
                "Die link kan ik niet gebruiken, sorry! Stuur de naam van een nummer of een link van de muziekdienst van dit kanaal.",
            ],
            sassy:
            [
                "Met die link kan ik niets. Stuur de naam van een nummer of een link van de muziekdienst van dit kanaal.",
            ],
            hype:
            [
                "DIE LINK WERKT NIET! STUUR DE NAAM VAN EEN NUMMER OF EEN LINK VAN DE MUZIEKDIENST VAN DIT KANAAL.",
            ],
            chill:
            [
                "die link werkt niet. stuur de naam van een nummer of een link van de muziekdienst van dit kanaal.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.SourceNotAllowed,
            informative:
            [
                "Dit kanaal neemt geen nummerverzoeken aan van die muziekdienst. Stuur in plaats daarvan de naam van een nummer.",
            ],
            friendly:
            [
                "Dit kanaal neemt geen verzoeken aan van die muziekdienst, sorry! Stuur in plaats daarvan de naam van een nummer.",
            ],
            sassy:
            [
                "Niet die muziekdienst. Dit kanaal heeft hem uitgezet. Stuur de naam van een nummer.",
            ],
            hype:
            [
                "DIE MUZIEKDIENST STAAT UIT IN DIT KANAAL! STUUR IN PLAATS DAARVAN DE NAAM VAN EEN NUMMER.",
            ],
            chill: ["die muziekdienst staat hier uit. stuur de naam van een nummer."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.SourceNeedsRole,
            informative:
            [
                "Jouw rol mag in dit kanaal geen nummers aanvragen via die muziekdienst. Stuur in plaats daarvan de naam van een nummer.",
            ],
            friendly:
            [
                "Sorry, jouw rol mag hier niets aanvragen via die muziekdienst. Probeer de naam van een nummer!",
            ],
            sassy:
            [
                "Niet voor jouw rol. Die muziekdienst zit hier op slot. Stuur de naam van een nummer.",
            ],
            hype:
            [
                "DIE MUZIEKDIENST ZIT HIER OP SLOT VOOR JOUW ROL! STUUR IN PLAATS DAARVAN DE NAAM VAN EEN NUMMER.",
            ],
            chill:
            [
                "die muziekdienst zit hier op slot voor jouw rol. stuur de naam van een nummer.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotPlayable,
            informative:
            [
                "Dat nummer kan niet worden afgespeeld in het land van de streamer. Probeer een andere versie.",
            ],
            friendly:
            [
                "Die is niet beschikbaar in het land van de streamer. Misschien wel in een andere versie!",
            ],
            sassy: ["Dat nummer zit hier op regioslot. Zoek een versie die wel afspeelt."],
            hype: ["DAT NUMMER IS GEBLOKKEERD IN DIT LAND. PROBEER EEN ANDERE VERSIE!"],
            chill: ["dat nummer kan hier niet spelen. probeer een andere versie."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Unreachable,
            informative:
            [
                "De muziekdienst was niet bereikbaar voor \"{query}\". Probeer het zo nog eens.",
            ],
            friendly:
            [
                "Ik kon de muziekdienst niet bereiken voor \"{query}\". Probeer het zo nog eens!",
            ],
            sassy: ["De muziekdienst negeerde me voor \"{query}\". Probeer het zo nog eens."],
            hype: ["MUZIEKDIENST NIET BEREIKBAAR VOOR \"{query}\". PROBEER HET ZO OPNIEUW."],
            chill: ["kon de muziekdienst niet bereiken voor \"{query}\". probeer het zo weer."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoProviderBroadcaster,
            informative:
            [
                "Nummerverzoeken zijn nog niet gekoppeld. Koppel Spotify of YouTube in het dashboard.",
            ],
            friendly:
            [
                "Nummerverzoeken zijn nog niet gekoppeld. Koppel Spotify of YouTube in het dashboard om te beginnen!",
            ],
            sassy:
            [
                "Nummerverzoeken kunnen nergens heen. Koppel Spotify of YouTube in het dashboard.",
            ],
            hype: ["KOPPEL SPOTIFY OF YOUTUBE IN HET DASHBOARD OM NUMMERVERZOEKEN AAN TE ZETTEN."],
            chill:
            [
                "nummerverzoeken zijn niet gekoppeld. voeg spotify of youtube toe in het dashboard.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NoProviderModerator,
            informative:
            [
                "Nummerverzoeken zijn niet gekoppeld. De broadcaster kan Spotify of YouTube koppelen in het dashboard.",
            ],
            friendly:
            [
                "Nummerverzoeken zijn niet gekoppeld. Geef de broadcaster een seintje: Spotify of YouTube koppelen kan in het dashboard.",
            ],
            sassy:
            [
                "Nummerverzoeken zijn niet gekoppeld. De broadcaster moet eerst Spotify of YouTube koppelen.",
            ],
            hype:
            [
                "NUMMERVERZOEKEN ZIJN NIET GEKOPPELD. DE BROADCASTER MOET SPOTIFY OF YOUTUBE KOPPELEN.",
            ],
            chill: ["nummerverzoeken zijn niet gekoppeld. zeg het tegen de broadcaster."]
        );
    }

    private static void AddDutchSkipSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Failed,
            informative: ["Er is niets om over te slaan, of overslaan is mislukt."],
            friendly: ["Ik kon dat nummer niet overslaan. Probeer het zo nog eens!"],
            sassy: ["Overslaan mislukt. Het nummer is koppig."],
            hype: ["OVERSLAAN MISLUKT. PROBEER HET OPNIEUW."],
            chill: ["overslaan lukte niet."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NoPlayer,
            informative: [DutchNoPlayerLine],
            friendly: ["De muziekspeler is nog niet open. Voeg de Audio-widget toe aan OBS!"],
            sassy:
            [
                "Een speler die niet open is kun je niet overslaan. Voeg de Audio-widget toe aan OBS.",
            ],
            hype: ["GEEN SPELER OPEN! VOEG DE AUDIO-WIDGET TOE AAN OBS!"],
            chill: ["de speler is niet open. voeg de audio-widget toe aan obs."]
        );

        AddDutchNoSpotifyDeviceSlot(catalog, BuiltinResponseSlots.Skip.Key, "overslaan");

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NoProvider,
            informative: ["Geen actieve muziekdienst."],
            friendly: ["Er is geen muziekdienst gekoppeld, dus er valt niets over te slaan."],
            sassy: ["Geen muziekdienst gekoppeld. Er valt niets over te slaan."],
            hype: ["GEEN MUZIEKDIENST GEKOPPELD. NIETS OM OVER TE SLAAN."],
            chill: ["geen muziekdienst gekoppeld."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.PremiumRequired,
            informative: ["De muziekdienst heeft een Premium-account nodig om over te slaan."],
            friendly: ["Het muziekaccount heeft Premium nodig om nummers over te slaan."],
            sassy: ["Overslaan vraagt om Premium. De gratis versie zegt nee."],
            hype: ["OVERSLAAN VRAAGT OM PREMIUM."],
            chill: ["overslaan kan alleen met premium."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NotConnected,
            informative:
            [
                "Overslaan lukte niet: de muziekverbinding moet opnieuw worden gekoppeld.",
            ],
            friendly: ["Ik kon niet overslaan. De muziekverbinding moet opnieuw worden gekoppeld."],
            sassy: ["Overslaan lukt niet met een dode muziekverbinding. Koppel hem opnieuw."],
            hype: ["NIET OVERGESLAGEN! DE MUZIEKVERBINDING MOET OPNIEUW WORDEN GEKOPPELD!"],
            chill: ["overslaan lukte niet. de muziekverbinding moet opnieuw gekoppeld worden."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Forbidden,
            informative:
            [
                "Overslaan lukte niet: de muziekverbinding heeft daar geen toestemming voor.",
            ],
            friendly:
            [
                "Ik kon niet overslaan. De muziekverbinding heeft daar geen toestemming voor.",
            ],
            sassy: ["De muziekverbinding mag niet overslaan. Repareer de rechten."],
            hype: ["NIET OVERGESLAGEN! DE MUZIEKVERBINDING HEEFT GEEN TOESTEMMING!"],
            chill: ["overslaan lukte niet. de muziekverbinding heeft geen toestemming."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Usage,
            informative:
            [
                "Gebruik: !skip <N>. Dat verwijdert JOUW N-de verzoek in de wachtrij. !skip zonder nummer slaat het huidige nummer over (mods slaan elk nummer over, jij je eigen nummers).",
            ],
            friendly:
            [
                "Probeer !skip <N> om je N-de verzoek te verwijderen. !skip alleen slaat het huidige nummer over (mods slaan elk nummer over, jij je eigen nummers).",
            ],
            sassy:
            [
                "!skip <N> verwijdert JOUW N-de verzoek. !skip alleen slaat het huidige nummer over: mods elk nummer, jij alleen je eigen. Lees het nog eens.",
            ],
            hype:
            [
                "!skip <N> VERWIJDERT JOUW N-DE VERZOEK. !skip ALLEEN SLAAT HET HUIDIGE NUMMER OVER (MODS ELK NUMMER, JIJ JE EIGEN).",
            ],
            chill:
            [
                "!skip <n> verwijdert je n-de verzoek. !skip alleen slaat het huidige nummer over (mods elk nummer, jij je eigen).",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NoRequest,
            informative: ["@{user} Je hebt geen verzoek op positie {request.position}."],
            friendly: ["@{user} Ik kan je verzoek op positie {request.position} niet vinden."],
            sassy:
            [
                "@{user} Op positie {request.position} staat geen verzoek van jou. Tel nog eens na.",
            ],
            hype: ["@{user} GEEN VERZOEK VAN JOU OP POSITIE {request.position}."],
            chill: ["@{user} geen verzoek van jou op positie {request.position}."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.RemoveFailed,
            informative: ["@{user} Dat verzoek verwijderen is niet gelukt. Probeer het nog eens."],
            friendly: ["@{user} Ik kon dat verzoek niet verwijderen. Probeer het nog eens!"],
            sassy: ["@{user} Het verzoek wil niet weg. Probeer het nog eens."],
            hype: ["@{user} VERWIJDEREN MISLUKT. PROBEER HET OPNIEUW."],
            chill: ["@{user} verwijderen lukte niet. probeer het weer."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.Removed,
            informative: ["@{user} Je verzoek is verwijderd: {track.name} van {track.artist}"],
            friendly:
            [
                "@{user} Gelukt! Ik heb je verzoek verwijderd: {track.name} van {track.artist}",
            ],
            sassy: ["@{user} Vooruit. {track.name} van {track.artist} is uit de wachtrij."],
            hype: ["@{user} VERWIJDERD: {track.name} VAN {track.artist}."],
            chill: ["@{user} {track.name} van {track.artist} verwijderd."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.SkippedOwn,
            informative: ["Je nummer is overgeslagen."],
            friendly: ["Gelukt, ik heb je nummer overgeslagen!"],
            sassy: ["Je nummer is overgeslagen. Sta achter je keuzes, en bedenk dan iets anders."],
            hype: ["JE NUMMER IS OVERGESLAGEN. DE VOLGENDE."],
            chill: ["je nummer is overgeslagen."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NotYours,
            informative: ["Je kunt alleen nummers overslaan die je zelf hebt aangevraagd."],
            friendly: ["Je kunt alleen nummers overslaan die je zelf hebt aangevraagd, sorry!"],
            sassy: ["Leuk geprobeerd. Je kunt alleen je eigen verzoeken overslaan."],
            hype: ["JE KUNT ALLEEN JE EIGEN VERZOEKEN OVERSLAAN."],
            chill: ["je kunt alleen je eigen verzoeken overslaan."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Skip.Key,
            BuiltinResponseSlots.Skip.NothingPlaying,
            informative: ["Er speelt op dit moment geen nummer!"],
            friendly: ["Er speelt nu niets, dus er valt niets over te slaan!"],
            sassy: ["Er speelt niets. Er valt niets over te slaan. Indrukwekkend."],
            hype: ["NIETS AAN HET SPELEN. NIETS OM OVER TE SLAAN."],
            chill: ["er speelt niets."]
        );
    }

    private static void AddDutchVolumeAndBanSongSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.Current,
            informative: ["Het huidige volume is {volume.level}"],
            friendly: ["Het volume staat nu op {volume.level}%!"],
            sassy: ["Het volume staat op {volume.level}%. Ja, dat is wat je hoort."],
            hype: ["VOLUME STAAT OP {volume.level}%!"],
            chill: ["volume staat op {volume.level}%."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.Set,
            informative: ["Volume ingesteld op {volume.level}%."],
            friendly: ["Gelukt! Het volume is nu {volume.level}%."],
            sassy: ["Het volume is nu {volume.level}%. Je oren redden zich zelf."],
            hype: ["VOLUME OP {volume.level}% GEZET!"],
            chill: ["volume op {volume.level}% gezet."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.SetFailed,
            informative: ["Het volume instellen is mislukt."],
            friendly: ["Ik kon het volume niet aanpassen. Probeer het zo nog eens."],
            sassy: ["De volumeknop bewoog niet. Probeer het nog eens."],
            hype: ["VOLUME AANPASSEN MISLUKT. PROBEER HET OPNIEUW."],
            chill: ["kon het volume niet aanpassen."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.NoProvider,
            informative: ["Geen actieve muziekdienst."],
            friendly: ["Er is geen muziekdienst gekoppeld, dus ik kan het volume niet aanpassen."],
            sassy: ["Geen muziekdienst gekoppeld. Er is geen volume om aan te passen."],
            hype: ["GEEN MUZIEKDIENST GEKOPPELD."],
            chill: ["geen muziekdienst gekoppeld."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Volume.Key,
            BuiltinResponseSlots.Volume.PremiumRequired,
            informative:
            [
                "De muziekdienst heeft een Premium-account nodig om het volume aan te passen.",
            ],
            friendly: ["Het muziekaccount heeft Premium nodig om het volume aan te passen."],
            sassy: ["Volumeregeling vraagt om Premium. De gratis versie zegt nee."],
            hype: ["VOLUMEREGELING VRAAGT OM PREMIUM."],
            chill: ["volumeregeling kan alleen met premium."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.Banned,
            informative: ["{track.name} is verbannen en kan niet meer worden aangevraagd."],
            friendly:
            [
                "@{user} heeft \"{track.name}\" verbannen uit de nummerverzoeken. Hij komt niet meer terug!",
            ],
            sassy:
            [
                "@{user} heeft \"{track.name}\" verbannen uit de nummerverzoeken. Opgeruimd staat netjes.",
            ],
            hype: ["@{user} HEEFT \"{track.name}\" VERBANNEN UIT DE NUMMERVERZOEKEN. WEG."],
            chill: ["@{user} heeft \"{track.name}\" verbannen uit de verzoeken."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.StrikeWarning,
            informative:
            [
                "Je hebt nu 5 verbannen nummers. Zorg dat je zelf niet van deze functie wordt verbannen.",
            ],
            friendly:
            [
                "Dat zijn nu veel verbannen nummers. Wees voorzichtig, anders raak je deze functie kwijt.",
            ],
            sassy: ["Zoveel verbannen nummers. Ga zo door en deze functie is weg."],
            hype: ["TE VEEL VERBANNEN NUMMERS. PAS OP, ANDERS IS DEZE FUNCTIE WEG."],
            chill: ["dat zijn veel verbannen nummers. pas op, anders is de functie weg."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.StrikeRevoked,
            informative:
            [
                "Je toestemming om nummers te verzilveren is ingetrokken. Punten worden niet teruggegeven als je er meer aanvraagt.",
            ],
            friendly:
            [
                "Je toestemming voor nummerverzoeken is nu ingetrokken. Punten worden niet teruggegeven als je er toch meer aanvraagt.",
            ],
            sassy:
            [
                "Je verzoeken zijn ingetrokken. Punten komen niet terug als je het toch probeert.",
            ],
            hype: ["VERZOEKEN INGETROKKEN. GEEN TERUGBETALING ALS JE HET TOCH PROBEERT."],
            chill:
            [
                "je toestemming voor verzoeken is ingetrokken. geen terugbetaling als je het toch probeert.",
            ]
        );
    }

    private static void AddDutchBangerAndPlaylistSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.Nothing,
            informative: ["Er speelt nu niets, dus er is geen banger om toe te voegen."],
            friendly: ["Er speelt nu niets. Start een nummer en probeer !banger nog eens!"],
            sassy: ["Er speelt geen nummer! Stilte is moeilijk een banger te noemen."],
            hype: ["ER SPEELT NIETS. NOG GEEN BANGER OM TOE TE VOEGEN!"],
            chill: ["er speelt nu niets, dus niets om toe te voegen."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.Added,
            informative:
            [
                "@{user} heeft {track.name} van {track.artist} toegevoegd aan de playlist {playlist.name}.",
            ],
            friendly:
            [
                "Goeie keuze @{user}! {track.name} van {track.artist} staat nu in {playlist.name}!",
            ],
            sassy:
            [
                "Goede smaak, @{user}. {track.name} van {track.artist} is net in {playlist.name} beland.",
            ],
            hype:
            [
                "@{user} HEEFT HET GEFIKST! {track.name} VAN {track.artist} STAAT NU IN {playlist.name}! BANGER!",
            ],
            chill: ["@{user} heeft {track.name} van {track.artist} toegevoegd aan {playlist.name}."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.AlreadyThere,
            informative:
            [
                "@{user}, {track.name} van {track.artist} staat al in de playlist {playlist.name}.",
            ],
            friendly:
            [
                "@{user}, {track.name} van {track.artist} staat al in {playlist.name}. Grote geesten denken eender!",
            ],
            sassy:
            [
                "@{user}, {track.name} van {track.artist} staat al te knallen in {playlist.name}! Je smaak is consistent, maar je geheugen kan beter.",
                "Breaking: @{user} ontdekt dat {track.name} van {track.artist} geen twee keer een banger kan zijn! Wetenschappers verbaasd, {playlist.name} ongewijzigd.",
                "Leuk geprobeerd @{user}, maar {track.name} van {track.artist} zit al lekker in {playlist.name}. Misschien eerst kijken, dan klikken?",
                "Plot twist @{user}: {track.name} van {track.artist} was al gecertificeerd banger-materiaal in {playlist.name}! Deze fout was tenminste gratis!",
                "@{user} probeerde {track.name} van {track.artist} dubbel te bangeren. Zo werkt {playlist.name} niet, schat.",
                "Oef @{user}, {track.name} van {track.artist} leeft al zijn beste leven in {playlist.name}! Gelukkig kost dit commando niets.",
                "Achievement unlocked @{user}: 'Banger Déjà Vu!' {track.name} van {track.artist} staat al in {playlist.name}. Beloning: goede smaak, twee keer.",
                "Leuk feitje @{user}: {track.name} van {track.artist} staat al in {playlist.name}! Minder leuk feitje: je hebt een prima commando verspild.",
                "@{user}, je maakt {track.name} van {track.artist} niet bangerder door het nog eens aan {playlist.name} toe te voegen. Zo werkt banger-wiskunde niet!",
                "Alarm: @{user} probeerde een banger-paradox met {track.name} van {track.artist}! {playlist.name} weigerde deze tijdelijke anomalie. Het was tenminste gratis!",
            ],
            hype: ["{track.name} VAN {track.artist} IS AL EEN BANGER IN {playlist.name}, @{user}!"],
            chill: ["@{user}, {track.name} van {track.artist} staat al in {playlist.name}."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.NoPlaylist,
            informative:
            [
                "Er is geen banger-playlist gekozen. Een moderator kan er een kiezen in de muziekinstellingen.",
            ],
            friendly: ["Er is nog geen bangers-playlist. Kies er een in de muziekinstellingen!"],
            sassy:
            [
                "Er is geen bangers-playlist. Gewaagd om bangers te verzamelen zonder plek om ze neer te zetten.",
            ],
            hype: ["NOG GEEN BANGERS-PLAYLIST! KIES ER EEN IN DE MUZIEKINSTELLINGEN!"],
            chill: ["nog geen bangers-playlist gekozen, kies er een in de muziekinstellingen."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.Failed,
            informative:
            [
                "@{user}, {playlist.name} kon niet worden bijgewerkt met {track.name} van {track.artist}. Probeer het zo nog eens.",
            ],
            friendly:
            [
                "Oeps @{user}, {track.name} van {track.artist} is niet in {playlist.name} terechtgekomen. Probeer het zo nog eens!",
            ],
            sassy:
            [
                "@{user}, {playlist.name} zei nee tegen {track.name} van {track.artist}. Probeer het zo nog eens.",
            ],
            hype:
            [
                "{playlist.name} FAALDE BIJ {track.name} VAN {track.artist}, @{user}! PROBEER HET ZO OPNIEUW!",
            ],
            chill:
            [
                "kon {track.name} van {track.artist} niet toevoegen aan {playlist.name}, probeer het zo weer.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Banger.Key,
            BuiltinResponseSlots.Banger.Held,
            informative:
            [
                "@{user}, {track.name} van {track.artist} wordt na de stream toegevoegd aan {playlist.name}.",
            ],
            friendly:
            [
                "Goeie keuze @{user}! {track.name} van {track.artist} wordt na de stream opgeslagen in {playlist.name}!",
            ],
            sassy:
            [
                "@{user}, {track.name} van {track.artist} staat in de rij voor {playlist.name}. Opslaan na de stream, geduld is een schone zaak.",
            ],
            hype:
            [
                "{track.name} VAN {track.artist} STAAT VAST VOOR {playlist.name}, @{user}! OPGESLAGEN NA DE STREAM!",
            ],
            chill: ["na de stream opgeslagen: {track.name} van {track.artist} in {playlist.name}."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Playlist.Key,
            BuiltinResponseSlots.Playlist.Link,
            informative: ["De bangers-playlist is: {playlist.url}"],
            friendly: ["Hier is de bangers-playlist, veel plezier! {playlist.url}"],
            sassy: ["De bangers-playlist, aangezien je erom vroeg: {playlist.url}"],
            hype: ["DE BANGERS-PLAYLIST STAAT HIER: {playlist.url}"],
            chill: ["bangers-playlist: {playlist.url}"]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Playlist.Key,
            BuiltinResponseSlots.Playlist.NoPlaylist,
            informative: ["Er is geen playlist-ID ingesteld."],
            friendly: ["Er is nog geen bangers-playlist ingesteld."],
            sassy: ["Geen playlist-ID ingesteld. De bangers zijn dakloos."],
            hype: ["NOG GEEN BANGERS-PLAYLIST INGESTELD!"],
            chill: ["geen bangers-playlist ingesteld."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Playlist.Key,
            BuiltinResponseSlots.Playlist.NotFound,
            informative: ["Playlist niet gevonden."],
            friendly: ["Ik kon de bangers-playlist niet vinden."],
            sassy: ["Playlist niet gevonden. De bangers zijn zoek."],
            hype: ["PLAYLIST NIET GEVONDEN!"],
            chill: ["kon de playlist niet vinden."]
        );
    }

    private const string DutchNoPlayerLine =
        "De muziekspeler is niet open. Voeg de Audio-widget toe aan OBS.";

    private const string DutchNoSpotifyDeviceLine =
        "Er speelt geen Spotify-apparaat. Open Spotify op een apparaat.";

    /// <summary>The Dutch "no Spotify device" reply for one playback-control command; <paramref name="verb"/> is its infinitive.</summary>
    private static void AddDutchNoSpotifyDeviceSlot(
        Dictionary<(string, string), SlotEntry> catalog,
        string builtinKey,
        string verb
    ) =>
        Translate(
            catalog,
            Dutch,
            builtinKey,
            BuiltinResponseSlots.PlaybackControl.NoSpotifyDevice,
            informative: [DutchNoSpotifyDeviceLine],
            friendly: ["Er speelt nog geen Spotify-apparaat. Open Spotify op een apparaat!"],
            sassy: [$"Ik kan niet {verb} als geen apparaat Spotify afspeelt. Open Spotify."],
            hype: ["GEEN SPOTIFY-APPARAAT! OPEN SPOTIFY!"],
            chill: ["geen spotify-apparaat aan het spelen. open spotify."]
        );

    /// <summary>Dutch <c>!pause</c> / <c>!resume</c>: one success slot each, plus the shared failure slots.</summary>
    private static void AddDutchPlaybackControlSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Pause.Key,
            BuiltinResponseSlots.Pause.Paused,
            informative: ["De muziek is gepauzeerd."],
            friendly: ["Muziek gepauzeerd. Zo weer terug!"],
            sassy: ["Muziek gepauzeerd. Geniet van de stilte."],
            hype: ["MUZIEK GEPAUZEERD!"],
            chill: ["muziek gepauzeerd."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Resume.Key,
            BuiltinResponseSlots.Resume.Resumed,
            informative: ["De muziek wordt hervat."],
            friendly: ["De muziek staat weer aan. Veel plezier!"],
            sassy: ["Muziek hervat. De stilte werd ongemakkelijk."],
            hype: ["DE MUZIEK STAAT WEER AAN!"],
            chill: ["de muziek staat weer aan."]
        );

        foreach (
            (string key, string verb) in new[]
            {
                (BuiltinResponseSlots.Pause.Key, "pauzeren"),
                (BuiltinResponseSlots.Resume.Key, "hervatten"),
            }
        )
        {
            Translate(
                catalog,
                Dutch,
                key,
                BuiltinResponseSlots.PlaybackControl.NoPlayer,
                informative: [DutchNoPlayerLine],
                friendly: ["De muziekspeler is nog niet open. Voeg de Audio-widget toe aan OBS!"],
                sassy:
                [
                    $"Ik kan niet {verb} op een speler die niet open is. Voeg de Audio-widget toe aan OBS.",
                ],
                hype: ["GEEN SPELER OPEN! VOEG DE AUDIO-WIDGET TOE AAN OBS!"],
                chill: ["de speler is niet open. voeg de audio-widget toe aan obs."]
            );
            AddDutchNoSpotifyDeviceSlot(catalog, key, verb);
            Translate(
                catalog,
                Dutch,
                key,
                BuiltinResponseSlots.PlaybackControl.NoProvider,
                informative: ["Er is geen muziekdienst ingesteld voor dit kanaal."],
                friendly: [$"Er is nog geen muziekdienst ingesteld, dus ik kan niet {verb}."],
                sassy: [$"Geen muziekdienst ingesteld. Ik kan niet {verb}."],
                hype: ["GEEN MUZIEKDIENST INGESTELD!"],
                chill: ["geen muziekdienst ingesteld."]
            );
            Translate(
                catalog,
                Dutch,
                key,
                BuiltinResponseSlots.PlaybackControl.PremiumRequired,
                informative: ["De muziekdienst heeft daarvoor een Premium-account nodig."],
                friendly: [$"Het muziekaccount heeft Premium nodig om te kunnen {verb}."],
                sassy: [$"Voor {verb} heb je Premium nodig. De gratis versie zegt nee."],
                hype: [$"PREMIUM NODIG OM TE KUNNEN {verb.ToUpperInvariant()}!"],
                chill: [$"kan niet {verb} zonder premium."]
            );
            Translate(
                catalog,
                Dutch,
                key,
                BuiltinResponseSlots.PlaybackControl.Failed,
                informative: [$"De muziek {verb} is niet gelukt."],
                friendly: [$"Ik kon de muziek niet {verb}. Probeer het zo nog eens!"],
                sassy: ["De muziek werkt niet mee. Probeer het nog eens."],
                hype: [$"{verb.ToUpperInvariant()} MISLUKT! PROBEER HET OPNIEUW!"],
                chill: [$"kon de muziek niet {verb}."]
            );
        }
    }
}
