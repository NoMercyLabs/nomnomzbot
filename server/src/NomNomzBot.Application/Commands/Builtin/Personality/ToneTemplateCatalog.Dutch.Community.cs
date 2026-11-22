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

/// <summary>Dutch text of the community built-ins; the English text is in <c>ToneTemplateCatalog.Community.cs</c>.</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddDutchCommunitySlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddDutchForgetmeAndShoutoutSlots(catalog);
        AddDutchStatsSlots(catalog);
        AddDutchQuoteSlots(catalog);
        AddDutchVoiceSlots(catalog);
        AddDutchMediaSlots(catalog);
        AddDutchPermitSlots(catalog);
        AddDutchUnpermitSlots(catalog);
    }

    private static void AddDutchForgetmeAndShoutoutSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Forgetme.Key,
            BuiltinResponseSlots.Forgetme.Done,
            informative: ["Klaar. Je gegevens zijn gewist en je begint met een schone lei."],
            friendly: ["Alles klaar. Je gegevens zijn gewist en je mag opnieuw beginnen!"],
            sassy: ["Poef. Weg. Je bent nu een mysterie, zelfs voor mij."],
            hype: ["SCHOONGEVEEGD. NIEUWE START GEACTIVEERD."],
            chill: ["klaar, je hebt een schone lei."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Shoutout.Key,
            BuiltinResponseSlots.Shoutout.Queued,
            informative: ["Shoutout voor {target.displayname} staat in de wachtrij."],
            friendly: ["Je shoutout voor {target.displayname} staat klaar en gaat zo de lucht in!"],
            sassy:
            [
                "Shoutout voor {target.displayname} staat in de wachtrij. Twitch laat ons wachten, kijk mij niet aan.",
            ],
            hype: ["SHOUTOUT VOOR {target.displayname} STAAT KLAAR! HIJ KOMT ERAAN!"],
            chill: ["de shoutout voor {target.displayname} staat in de wachtrij en komt zo."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Shoutout.Key,
            BuiltinResponseSlots.Shoutout.Announcement,
            informative:
            [
                "Ga kijken bij {target.displayname}! Volg het kanaal om de volgende stream niet te missen.",
                "Shoutout voor {target.displayname}, die {target.game} streamt. Geef het kanaal een follow.",
                "Kijk eens bij {target.displayname} en geef het kanaal een follow.",
            ],
            friendly:
            [
                "Geef {target.displayname} wat liefde en klik op die follow-knop!",
                "{target.displayname} is heerlijk om naar te kijken. Zeg hoi en laat een follow achter!",
                "Veel liefs voor {target.displayname}! Kom langs, zeg hallo en geef het kanaal een follow.",
            ],
            sassy:
            [
                "Check {target.displayname}! Die heeft best goede {target.game}-content. Geef een follow! Bijna een prof, of in elk geval doet het alsof op Twitch.",
                "Hé, kijk hier! {target.displayname} is flink bezig met {target.game}. Geef een follow! Zo goed dat het bijna irritant is.",
                "Opgelet, aardbewoners! {target.displayname} heeft {target.game}-video's die je moet zien. Geef een follow! Het is een masterclass of een circus, en beide zijn vermakelijk.",
                "Daar komt iets moois! {target.displayname} heeft {target.game}-actie voor je. Geef een follow! Kan het goed, of laat het in elk geval zo lijken.",
                "Niet lopen, rennen! {target.displayname} heeft meer {target.game} dan je aankunt. Geef een follow! Zeker het onderbreken van je snack waard.",
                "Onze eigen legende {target.displayname} heeft geweldige {target.game}-content! Geef een follow! Staat vast op het punt iets episch te doen, of glorieus onderuit te gaan.",
                "Let op, schatjes! {target.displayname} heeft wat {target.game} voor je. Geef een follow! Blijft bewijzen dat het geweldig is (zeg niet dat ik dat zei).",
                "Raad eens wie content heeft? {target.displayname}! Zit lekker in {target.game}. Geef een follow! Brengt de vibes mee, of je het nu wilt of niet.",
                "Aanschouw! {target.displayname} heeft stevige {target.game} voor je. Geef een follow! Verwent ons met aanwezigheid en twijfelachtige keuzes in {target.game}.",
            ],
            hype:
            [
                "SHOUTOUT VOOR {target.displayname}! KLIK NU OP DIE FOLLOW!",
                "GA KIJKEN BIJ {target.displayname}! FOLLOW, FOLLOW, FOLLOW!",
                "GROTE SHOUTOUT VOOR {target.displayname}! {target.game} HEEFT NOOIT ZO GOED GEKEKEN. KLIK OP FOLLOW!",
            ],
            chill:
            [
                "kijk eens bij {target.displayname} als je tijd hebt, en laat misschien een follow achter",
                "{target.displayname} is goed gezelschap. klik op follow wanneer je er klaar voor bent",
                "geen haast, maar {target.displayname} is een follow waard. ga maar kijken",
            ]
        );
    }

    private static void AddDutchStatsSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Stats.Key,
            BuiltinResponseSlots.Stats.Profile,
            informative:
            [
                "{stats.user} · {stats.messages} berichten · {stats.watchtime} gekeken · {stats.points} punten{stats.rankpart}{stats.streakpart} · voor het eerst gezien {stats.firstseen}",
            ],
            friendly:
            [
                "{stats.user}, je hebt {stats.messages} berichten gestuurd en {stats.points} punten verdiend. Samen {stats.watchtime} gekeken!",
                "Kijk nou, {stats.user}: {stats.points} punten, {stats.messages} berichten, hier sinds {stats.firstseen}!",
                "{stats.user} is geweldig: {stats.watchtime} gekeken en {stats.points} punten!",
            ],
            sassy:
            [
                "GEHEIM DOSSIER: {stats.user}. {stats.messages} berichten. {stats.watchtime} gekeken. {stats.points} punten. Risiconiveau: chronisch online.",
                "{stats.user}: {stats.messages} berichten, {stats.points} punten, hier sinds {stats.firstseen}. Indrukwekkend. Zorgwekkend. Allebei.",
                "{stats.user} heeft {stats.watchtime} kijktijd. Ik oordeel niet. Eigenlijk is oordelen het grootste deel van mijn code. Ik oordeel.",
                "{stats.user} in het kort: {stats.messages} berichten, {stats.points} punten, {stats.watchtime} gekeken. En op de een of andere manier was niets daarvan stil.",
            ],
            hype:
            [
                "{stats.user}: {stats.points} PUNTEN, {stats.messages} BERICHTEN, {stats.watchtime} GEKEKEN. LEGENDARISCHE STATUS.",
                "GROTE CIJFERS VOOR {stats.user}: {stats.points} PUNTEN EN {stats.watchtime} GEKEKEN.",
                "{stats.user} IS ANDERS GEBOUWD: {stats.messages} BERICHTEN, {stats.points} PUNTEN.",
            ],
            chill:
            [
                "{stats.user}: {stats.messages} berichten, {stats.watchtime}, {stats.points} punten.",
                "{stats.user} — {stats.points} punten, er al bij sinds {stats.firstseen}.",
                "{stats.user}: {stats.watchtime} gekeken, {stats.points} punten. degelijk.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Stats.Key,
            BuiltinResponseSlots.Stats.NotSeen,
            informative: ["Ik heb {stats.user} hier nog niet zien chatten."],
            friendly: ["Ik heb {stats.user} hier nog niet ontmoet, zeg snel hoi!"],
            sassy:
            [
                "{stats.user}? Nooit gezien. Een spook, of een lurker met een goede vermomming.",
            ],
            hype: ["NOG GEEN SPOOR VAN {stats.user}. HET PODIUM WACHT."],
            chill: ["{stats.user} nog niet gezien hier."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Stats.Key,
            BuiltinResponseSlots.Stats.AccountUnresolved,
            informative: ["Stats: je account kon niet worden gevonden."],
            friendly: ["Ik kon je account net niet vinden, probeer het nog eens!"],
            sassy:
            [
                "Je account verstopt zich voor me. Probeer het nog eens, en wees aardiger voor het ding.",
            ],
            hype: ["ACCOUNT NIET GEVONDEN. PROBEER HET NOG EENS, KAMPIOEN."],
            chill: ["kon je account niet vinden, probeer het nog eens."]
        );
    }

    private static void AddDutchQuoteSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Quote.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.Show,
            informative: ["Citaat: {quote}"],
            friendly: ["Hier is een goede: {quote}"],
            sassy: ["Geniet van dit meesterwerk: {quote}"],
            hype: ["CITAAT ONDERWEG: {quote}"],
            chill: ["citaat: {quote}"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.Empty,
            informative: ["Er zijn nog geen citaten."],
            friendly: ["Nog geen citaten opgeslagen, wees de eerste die er een toevoegt!"],
            sassy: ["Nog geen citaten. Niemand hier heeft iets zinnigs gezegd. Schokkend."],
            hype: ["NOG GEEN CITATEN. TIJD OM GESCHIEDENIS TE SCHRIJVEN."],
            chill: ["nog geen citaten."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.NotFound,
            informative: ["Ik kon citaat #{quote.number} niet vinden."],
            friendly: ["Hm, ik kan citaat #{quote.number} niet vinden. Probeer een ander nummer?"],
            sassy: ["Citaat #{quote.number} bestaat niet. Stoer dat je dat dacht."],
            hype: ["CITAAT #{quote.number} IS NERGENS TE VINDEN. VOLGENDE!"],
            chill: ["geen citaat #{quote.number}."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.Added,
            informative: ["Toegevoegd: {quote}"],
            friendly: ["Opgeslagen! {quote}"],
            sassy: ["Vooruit, het is nu vereeuwigd. {quote}"],
            hype: ["CITAAT VASTGELEGD! {quote}"],
            chill: ["toegevoegd. {quote}"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.AddUsage,
            informative: ["Gebruik: !quote add <tekst>, of reageer op een bericht met !quote add."],
            friendly:
            [
                "Typ !quote add <tekst> om een citaat op te slaan, of reageer op een bericht met !quote add.",
            ],
            sassy:
            [
                "Wat moet ik toevoegen dan? !quote add <tekst>, of reageer op een bericht met !quote add.",
            ],
            hype: ["GEEF ME IETS OM OP TE SLAAN! !quote ADD <tekst>, OF REAGEER OP EEN BERICHT."],
            chill: ["!quote add <tekst>, of reageer op een bericht met !quote add."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.AddFailed,
            informative: ["Ik kon dat citaat niet toevoegen."],
            friendly: ["Sorry, ik kon dat citaat niet opslaan. Probeer het nog eens."],
            sassy: ["Het citaat wilde niet worden opgeslagen. Ik kan het hem niet kwalijk nemen."],
            hype: ["HET CITAAT IS NIET OPGESLAGEN. PROBEER HET NOG EENS!"],
            chill: ["kon dat citaat niet toevoegen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.InvalidText,
            informative: ["Een citaat heeft tekst nodig, van maximaal 500 tekens."],
            friendly: ["Een citaat heeft tekst nodig, tot 500 tekens. Probeer het nog eens!"],
            sassy: ["Een citaat is tussen 1 en 500 tekens. Geen roman."],
            hype: ["CITATEN HEBBEN TEKST NODIG, MAXIMAAL 500 TEKENS. HOU HET KORT EN KRACHTIG!"],
            chill: ["citaattekst: 1 tot 500 tekens."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.Updated,
            informative: ["Bijgewerkt: {quote}"],
            friendly: ["Alles bijgewerkt! {quote}"],
            sassy: ["Herschreven. De geschiedenis wordt geschreven door de redacteuren. {quote}"],
            hype: ["CITAAT BIJGEWERKT! {quote}"],
            chill: ["bijgewerkt. {quote}"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.EditUsage,
            informative: ["Gebruik: !quote edit <nummer> <nieuwe tekst>"],
            friendly: ["Typ !quote edit <nummer> <nieuwe tekst> om een citaat te wijzigen."],
            sassy: ["Welk citaat, en waarin veranderen? !quote edit <nummer> <nieuwe tekst>."],
            hype: ["!quote EDIT <nummer> <nieuwe tekst> — JIJ KUNT DIT!"],
            chill: ["!quote edit <nummer> <nieuwe tekst>"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.EditFailed,
            informative: ["Ik kon dat citaat niet bijwerken."],
            friendly: ["Sorry, ik kon dat citaat niet bijwerken. Probeer het nog eens."],
            sassy: ["Het citaat weigerde te bewegen. Koppig ding."],
            hype: ["DE WIJZIGING IS NIET GELAND. PROBEER HET NOG EENS!"],
            chill: ["kon dat citaat niet bijwerken."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.Deleted,
            informative: ["Citaat #{quote.number} verwijderd."],
            friendly: ["Citaat #{quote.number} is verwijderd."],
            sassy: ["Citaat #{quote.number} is weg. We praten er niet meer over."],
            hype: ["CITAAT #{quote.number} VERWIJDERD. VOOR ALTIJD WEG!"],
            chill: ["citaat #{quote.number} verwijderd."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.DeleteUsage,
            informative: ["Gebruik: !quote del <nummer>"],
            friendly: ["Typ !quote del <nummer> om een citaat te verwijderen."],
            sassy: ["Welk citaat moet weg? !quote del <nummer>."],
            hype: ["!quote DEL <nummer> — NOEM JE DOELWIT!"],
            chill: ["!quote del <nummer>"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.AccountUnresolved,
            informative: ["Je account kon niet worden gevonden, probeer het nog eens."],
            friendly: ["Ik kon je account net niet vinden, probeer het nog eens!"],
            sassy: ["Je account verstopt zich voor me. Probeer het nog eens."],
            hype: ["ACCOUNT NIET GEVONDEN. PROBEER HET NOG EENS, KAMPIOEN."],
            chill: ["kon je account niet vinden, probeer het nog eens."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.NoAddPermission,
            informative: ["Je hebt geen toestemming om citaten toe te voegen."],
            friendly:
            [
                "Sorry, je kunt hier geen citaten toevoegen. Vraag het de streamer als je dat wilt.",
            ],
            sassy: ["Citaten toevoegen staat niet in je functieomschrijving."],
            hype: ["CITATEN TOEVOEGEN IS GEBLOKKEERD VOOR JOU. VRAAG HET DE STREAMER!"],
            chill: ["je kunt hier geen citaten toevoegen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.NoEditPermission,
            informative: ["Je hebt geen toestemming om citaten te bewerken."],
            friendly:
            [
                "Sorry, je kunt hier geen citaten bewerken. Vraag het de streamer als je dat wilt.",
            ],
            sassy: ["Citaten bewerken staat niet in je functieomschrijving."],
            hype: ["CITATEN BEWERKEN IS GEBLOKKEERD VOOR JOU. VRAAG HET DE STREAMER!"],
            chill: ["je kunt hier geen citaten bewerken."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Quote.NoDeletePermission,
            informative: ["Je hebt geen toestemming om citaten te verwijderen."],
            friendly:
            [
                "Sorry, je kunt hier geen citaten verwijderen. Vraag het de streamer als je dat wilt.",
            ],
            sassy: ["Citaten verwijderen staat niet in je functieomschrijving."],
            hype: ["CITATEN VERWIJDEREN IS GEBLOKKEERD VOOR JOU. VRAAG HET DE STREAMER!"],
            chill: ["je kunt hier geen citaten verwijderen."]
        );
    }

    private static void AddDutchVoiceSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddDutchVoiceCurrentAndSetSlots(catalog);
        AddDutchVoiceListSlots(catalog);
        AddDutchVoiceUsageSlots(catalog);
    }

    private static void AddDutchVoiceCurrentAndSetSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        const string key = BuiltinResponseSlots.Voice.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.Current,
            informative: ["Huidige stem: {voice.id}"],
            friendly:
            [
                "Je TTS-stem is {voice.id}! Wil je een andere? Probeer !voice <zoekterm>, of !voice clear voor de standaardstem van het kanaal.",
            ],
            sassy:
            [
                "Je klinkt als {voice.id}. Verander het met !voice <zoekterm>, of !voice clear als je er spijt van hebt.",
            ],
            hype:
            [
                "JE STEM IS {voice.id}! WISSEL HEM MET !voice <zoekterm>, OF !voice CLEAR VOOR DE STANDAARD.",
            ],
            chill:
            [
                "je stem is {voice.id}. !voice <zoekterm> om te wisselen, !voice clear om te resetten.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.CurrentDefault,
            informative:
            [
                "Standaardstem in gebruik: {voice.name}. Kies een eigen stem met !voice set <naam>",
            ],
            friendly:
            [
                "Je gebruikt de standaardstem van het kanaal, {voice.name}. Kies je eigen stem met !voice <zoekterm>, zoals !voice british female!",
            ],
            sassy:
            [
                "Je zit op de standaardstem, {voice.name}, net als iedereen. Val op met !voice <zoekterm>, bijvoorbeeld !voice british female.",
            ],
            hype:
            [
                "JE ZIT OP DE STANDAARDSTEM, {voice.name}! PAK JE EIGEN STEM MET !voice <zoekterm>, ZOALS !voice BRITISH FEMALE!",
            ],
            chill:
            [
                "op de standaardstem, {voice.name}. kies er een met !voice <zoekterm>, bijvoorbeeld !voice british female.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.CurrentNone,
            informative: ["Geen stem ingesteld. Zoek stemmen met !voice get <taal>."],
            friendly:
            [
                "Er is nog geen stem ingesteld. Zoek er een met !voice get <taal>, zoals !voice get nl!",
            ],
            sassy: ["Geen stem ingesteld. Zoek er een met !voice get <taal>, als je erom geeft."],
            hype: ["NOG GEEN STEM INGESTELD! ZOEK ER EEN MET !voice GET <taal>!"],
            chill: ["geen stem ingesteld. zoek er een met !voice get <taal>."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.MultipleMatches,
            informative: ["Meerdere treffers: {voice.list}"],
            friendly:
            [
                "Een paar stemmen passen hierbij: {voice.list}. Noem de volledige naam van de stem die je wilt!",
            ],
            sassy: ["Meerdere stemmen passen: {voice.list}. Wees specifiek."],
            hype: ["VEEL TREFFERS: {voice.list}! NOEM DE VOLLEDIGE NAAM VAN DE STEM DIE JE WILT!"],
            chill: ["een paar treffers: {voice.list}. gebruik de volledige naam."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.Cleared,
            informative: ["Je TTS-stem is terug op de standaardstem van het kanaal."],
            friendly: ["Klaar! Je zit weer op de standaardstem van het kanaal!"],
            sassy: ["Terug naar de standaardstem. Basic, maar betrouwbaar."],
            hype: ["TERUG NAAR DE STANDAARDSTEM! KLAAR VOOR DE VOLGENDE."],
            chill: ["terug op de standaardstem."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.ClearFailed,
            informative: ["Ik kon je stem niet resetten."],
            friendly: ["Sorry, ik kon je stem niet resetten. Probeer het nog eens."],
            sassy: ["Je stem weigert te resetten. Indrukwekkend."],
            hype: ["DE RESET WERKTE NIET. PROBEER HET NOG EENS!"],
            chill: ["kon je stem niet resetten."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.Set,
            informative: ["✅ Stem ingesteld op {voice.name}!"],
            friendly: ["Gelukt! Je stem is nu {voice.name} [{voice.locale} {voice.gender}]!"],
            sassy:
            [
                "Je stem is nu {voice.name} [{voice.locale} {voice.gender}]. Probeer er een beetje aan te voldoen.",
            ],
            hype: ["NIEUWE STEM ONTGRENDELD: {voice.name} [{voice.locale} {voice.gender}]!"],
            chill: ["stem ingesteld op {voice.name} [{voice.locale} {voice.gender}]."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.SetMany,
            informative:
            [
                "Je TTS-stem is nu {voice.name} [{voice.locale} {voice.gender}]. ({voice.count} treffers, voeg een woord toe om te verfijnen, of !voice clear om te resetten.)",
            ],
            friendly:
            [
                "Gelukt! Je stem is nu {voice.name} [{voice.locale} {voice.gender}]! {voice.count} stemmen pasten erbij, voeg dus een woord toe om te verfijnen.",
            ],
            sassy:
            [
                "Je stem is nu {voice.name} [{voice.locale} {voice.gender}]. {voice.count} treffers, ik heb er een gekozen. Graag gedaan.",
            ],
            hype:
            [
                "NIEUWE STEM ONTGRENDELD: {voice.name} [{voice.locale} {voice.gender}]! {voice.count} TREFFERS, VOEG EEN WOORD TOE OM TE VERFIJNEN!",
            ],
            chill:
            [
                "stem ingesteld op {voice.name} [{voice.locale} {voice.gender}]. {voice.count} treffers, voeg een woord toe om te verfijnen.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.SetFailed,
            informative: ["Ik kon die stem niet instellen."],
            friendly: ["Sorry, ik kon die stem niet instellen. Probeer het nog eens."],
            sassy: ["Die stem zei nee. Probeer een andere."],
            hype: ["DIE STEM BLEEF NIET HANGEN. PROBEER EEN ANDERE!"],
            chill: ["kon die stem niet instellen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.NoMatch,
            informative:
            [
                "Stem '{query}' niet gevonden. Gebruik !voice get <taal> om de beschikbare stemmen te zien.",
            ],
            friendly:
            [
                "Ik kon geen stem vinden voor \"{query}\". Probeer een naam, een taal zoals nl-NL of een accent zoals british!",
            ],
            sassy:
            [
                "\"{query}\"? Zo klinkt geen enkele stem. Probeer een naam, een taal zoals nl-NL of een accent zoals british.",
            ],
            hype:
            [
                "GEEN STEM GEVONDEN VOOR \"{query}\"! PROBEER EEN NAAM, EEN TAAL ZOALS NL-NL OF EEN ACCENT ZOALS BRITISH!",
            ],
            chill:
            [
                "geen stem gevonden voor \"{query}\". probeer een naam, nl-nl, of een accent zoals british.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.Disabled,
            informative: ["Zelf een stem kiezen staat uit op dit kanaal."],
            friendly: ["Zelf een stem kiezen staat op dit kanaal op dit moment uit."],
            sassy: ["Stemmen kiezen staat hier uit. Bespreek het met de streamer."],
            hype: ["STEMMEN KIEZEN STAAT UIT OP DIT KANAAL."],
            chill: ["stem kiezen staat hier uit."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.Roulette,
            informative:
            [
                "Het rad heeft gesproken! Je volgende TTS-bericht klinkt als... {voice.name} ({voice.locale}). Veel succes.",
                "Stemroulette zegt: {voice.name} ({voice.locale}). Geen terugname.",
                "Het rad draait... {voice.name} ({voice.locale})! Moge het lot je gunstig gezind zijn.",
                "De RNG-goden hebben gekozen: {voice.name} ({voice.locale}). Wij zijn niet verantwoordelijk voor wat daarna gebeurt.",
                "En de willekeurige stem is... {voice.name} ({voice.locale})! Chat, plaats je weddenschappen over hoe dit klinkt.",
                "Stemroulette is geland op {voice.name} ({voice.locale}). Dit wordt interessant.",
            ],
            friendly:
            [
                "Het rad koos {voice.name} [{voice.locale} {voice.gender}] voor je. Geniet van je nieuwe stem!",
            ],
            sassy:
            [
                "Het rad heeft gesproken: {voice.name} [{voice.locale} {voice.gender}]. Geef het lot de schuld, niet mij.",
            ],
            hype:
            [
                "HET RAD HEEFT GESPROKEN: {voice.name} [{voice.locale} {voice.gender}]! GEEN TERUGNAME!",
            ],
            chill: ["het rad zegt {voice.name} [{voice.locale} {voice.gender}]. geen terugname."]
        );
    }

    private static void AddDutchVoiceListSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Voice.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.Languages,
            informative: ["Beschikbare talen: {voice.languages}"],
            friendly: ["Dit zijn de talen die ik spreek: {voice.languages}"],
            sassy: ["Ik ben best een polyglot: {voice.languages}"],
            hype: ["TALEN ONTGRENDELD: {voice.languages}"],
            chill: ["talen: {voice.languages}"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.Voices,
            informative: ["Stemmen voor {voice.language}: {voice.list}"],
            friendly:
            [
                "Stemmen voor {voice.language}: {voice.list}. Kies er een met !voice set <naam>!",
            ],
            sassy:
            [
                "Stemmen voor {voice.language}: {voice.list}. Kies verstandig met !voice set <naam>.",
            ],
            hype:
            [
                "STEMMEN VOOR {voice.language}: {voice.list}. KIES ER EEN MET !voice SET <naam>!",
            ],
            chill: ["stemmen voor {voice.language}: {voice.list}. !voice set <naam> om te kiezen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.VoicesMore,
            informative:
            [
                "Stemmen voor {voice.language}: {voice.list} (+{voice.more} meer). Kies er een met !voice set <naam>.",
            ],
            friendly:
            [
                "Stemmen voor {voice.language}: {voice.list} (en nog {voice.more}). Kies er een met !voice set <naam>!",
            ],
            sassy:
            [
                "Stemmen voor {voice.language}: {voice.list} (+{voice.more} meer, ik had geen zin meer om te typen). Kies met !voice set <naam>.",
            ],
            hype:
            [
                "STEMMEN VOOR {voice.language}: {voice.list} (+{voice.more} MEER)! KIES ER EEN MET !voice SET <naam>!",
            ],
            chill:
            [
                "stemmen voor {voice.language}: {voice.list} (+{voice.more} meer). !voice set <naam> om te kiezen.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.NoVoicesForLanguage,
            informative:
            [
                "Geen stemmen gevonden voor '{voice.language}'. Probeer !voice languages",
            ],
            friendly:
            [
                "Ik heb geen stemmen voor {voice.language}. Probeer !voice languages om te zien wat ik wel heb!",
            ],
            sassy:
            [
                "{voice.language}? Hier niets. Probeer !voice languages, het is een korte lijst.",
            ],
            hype: ["GEEN STEMMEN VOOR {voice.language}! KIJK BIJ !voice LANGUAGES WAT ER IS!"],
            chill: ["geen stemmen voor {voice.language}. probeer !voice languages."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.NoVoices,
            informative: ["Geen TTS-stemmen beschikbaar."],
            friendly: ["Ik kan op dit moment geen TTS-stemmen vinden. Probeer het later nog eens."],
            sassy: ["De stemmencatalogus is leeg. Iedereen is sprakeloos. Letterlijk."],
            hype: ["GEEN STEMMEN BESCHIKBAAR NU! KOM STRAKS TERUG!"],
            chill: ["nu geen tts-stemmen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.NoVoicesForRoulette,
            informative: ["Geen stemmen beschikbaar voor roulette!"],
            friendly: ["Ik heb nu geen stemmen om het rad mee te draaien!"],
            sassy: ["Het rad is leeg. Niets om te draaien."],
            hype: ["GEEN STEMMEN OP HET RAD NU!"],
            chill: ["geen stemmen voor roulette."]
        );
    }

    private static void AddDutchVoiceUsageSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Voice.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.GetUsage,
            informative:
            [
                "Gebruik: !voice get <taal> (bijvoorbeeld !voice get nl of !voice get nl-NL)",
            ],
            friendly:
            [
                "Typ !voice get <taal> om stemmen te zien, zoals !voice get nl, of !voice get nl-NL.",
            ],
            sassy: ["Stemmen voor welke taal? !voice get <taal>, bijvoorbeeld !voice get nl."],
            hype: ["!voice GET <taal>, ZOALS !voice GET NL, OF !voice GET NL-NL!"],
            chill: ["!voice get <taal>, bijvoorbeeld !voice get nl."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.SetUsage,
            informative:
            [
                "Gebruik: !voice set <naam> (bijvoorbeeld !voice set Ana, !voice set en-US-AnaNeural)",
            ],
            friendly: ["Typ !voice set <naam> om een stem te kiezen, zoals !voice set Ana."],
            sassy: ["Instellen op wat? !voice set <naam>, bijvoorbeeld !voice set Ana."],
            hype: ["!voice SET <naam>, ZOALS !voice SET ANA!"],
            chill: ["!voice set <naam>, bijvoorbeeld !voice set ana."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.SetFor,
            informative: ["Stem voor {viewer} ingesteld op {voice.name}!"],
            friendly: ["Gelukt! {viewer} praat nu met {voice.name}."],
            sassy: ["{viewer} is nu {voice.name}. Hopelijk bevalt het."],
            hype: ["{viewer} IS NU {voice.name}!"],
            chill: ["{viewer} gebruikt nu {voice.name}."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.OwnVoiceOnly,
            informative:
            [
                "Je kunt alleen je eigen stem instellen. Een andere kijker kiezen is voor mods.",
            ],
            friendly:
            [
                "Je kunt je eigen stem kiezen met !voice set <naam>. De stem van iemand anders instellen is voor mods.",
            ],
            sassy: ["Alleen je eigen stem, graag. De stemmen van anderen zijn voor mods."],
            hype:
            [
                "JE KUNT JE EIGEN STEM INSTELLEN! DE STEMMEN VAN ANDERE KIJKERS ZIJN VOOR MODS!",
            ],
            chill: ["alleen je eigen stem. mods kunnen de stem van anderen instellen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.UnknownViewer,
            informative: ["Ik ken hier geen kijker met de naam '{viewer}'."],
            friendly: ["Ik heb hier nog geen kijker '{viewer}' gezien. Controleer de spelling?"],
            sassy: ["'{viewer}'? Nooit van gehoord."],
            hype: ["GEEN KIJKER GEVONDEN MET DE NAAM '{viewer}'!"],
            chill: ["ken geen kijker met de naam '{viewer}'."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.Usage,
            informative:
            [
                "Stemcommando's: !voice languages | !voice get <taal> | !voice set <naam> | !voice current | !voice roulette",
            ],
            friendly:
            [
                "Dit kan ik voor je doen: !voice languages | !voice get <taal> | !voice set <naam> | !voice current | !voice roulette",
            ],
            sassy:
            [
                "Kies een baan: !voice languages | !voice get <taal> | !voice set <naam> | !voice current | !voice roulette",
            ],
            hype:
            [
                "!voice LANGUAGES | !voice GET <taal> | !voice SET <naam> | !voice CURRENT | !voice ROULETTE!",
            ],
            chill: ["!voice languages | get <taal> | set <naam> | current | roulette"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Voice.UnknownCommand,
            informative:
            [
                "Onbekend stemcommando. Gebruik: !voice languages | !voice get <taal> | !voice set <naam> | !voice current | !voice roulette",
            ],
            friendly:
            [
                "Die ken ik niet. Probeer: !voice languages | !voice get <taal> | !voice set <naam> | !voice current | !voice roulette",
            ],
            sassy:
            [
                "Dat is geen stemcommando. Gebruik: !voice languages | !voice get <taal> | !voice set <naam> | !voice current | !voice roulette",
            ],
            hype:
            [
                "ONBEKEND STEMCOMMANDO! GEBRUIK: !voice LANGUAGES | !voice GET <taal> | !voice SET <naam> | !voice CURRENT | !voice ROULETTE",
            ],
            chill:
            [
                "onbekend stemcommando. probeer !voice languages | get <taal> | set <naam> | current | roulette",
            ]
        );
    }

    private static void AddDutchMediaSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Media.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.Usage,
            informative: ["Gebruik: !media <twitch-clip of youtube-link>"],
            friendly: ["Typ !media gevolgd door een Twitch-clip of YouTube-link om iets te delen!"],
            sassy: ["Wat wil je delen? Geef me een link. !media <twitch-clip of youtube-link>"],
            hype: ["GOOI EEN LINK ERIN! !media <twitch-clip of youtube-link>"],
            chill: ["!media <twitch-clip of youtube-link>"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.PlayUsage,
            informative:
            [
                "Gebruik: reageer met !play op een bericht met een twitch-clip of youtube-link, of gebruik !play <link>",
            ],
            friendly:
            [
                "Reageer op een bericht met een Twitch-clip of YouTube-link en typ !play, of zet de link achter !play!",
            ],
            sassy:
            [
                "Wat moet ik afspelen? Reageer op een link met !play, of geef me er een: !play <link>",
            ],
            hype: ["REAGEER OP EEN LINK MET !play, OF GOOI ER EEN IN: !play <link>"],
            chill: ["reageer op een link met !play, of !play <link>"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.AccountUnresolved,
            informative: ["Je account kon niet worden gevonden."],
            friendly: ["Ik kon je account net niet vinden, probeer het nog eens!"],
            sassy: ["Je account verstopt zich voor me. Probeer het nog eens."],
            hype: ["ACCOUNT NIET GEVONDEN. PROBEER HET NOG EENS, KAMPIOEN."],
            chill: ["kon je account niet vinden, probeer het nog eens."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.Queued,
            informative: ["{media.title} is aan de wachtrij toegevoegd!"],
            friendly: ["{media.title} staat in de wachtrij, bedankt voor het delen!"],
            sassy: ["{media.title} haalde de wachtrij. Laten we hopen dat het zijn plek verdient."],
            hype: ["{media.title} STAAT IN DE WACHTRIJ! HIERMEE!"],
            chill: ["{media.title} staat in de wachtrij."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.Pending,
            informative: ["{media.title} is ingediend, een mod bekijkt het zo."],
            friendly: ["Bedankt! {media.title} is ingestuurd, een mod kijkt er zo naar."],
            sassy: ["{media.title} ligt nu in handen van de mods. Duim maar."],
            hype: ["{media.title} IS INGEDIEND! DE MODS OORDELEN ZO!"],
            chill: ["{media.title} ingediend, een mod kijkt ernaar."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.Disabled,
            informative: ["Media share staat niet aan op dit kanaal."],
            friendly: ["Media share staat op dit moment niet aan in dit kanaal."],
            sassy: ["Media share staat hier uit. Niemand kijkt vandaag naar je clip."],
            hype: ["MEDIA SHARE STAAT UIT OP DIT KANAAL!"],
            chill: ["media share staat hier uit."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.SourceNotAllowed,
            informative:
            [
                "Alleen Twitch-clips en YouTube-video's die dit kanaal accepteert, kunnen in de wachtrij.",
            ],
            friendly:
            [
                "Ik kan alleen Twitch-clips en YouTube-video's aannemen die dit kanaal accepteert. Probeer een andere link!",
            ],
            sassy:
            [
                "Die link is hier niet welkom. Alleen Twitch-clips en geaccepteerde YouTube-video's.",
            ],
            hype: ["ALLEEN GEACCEPTEERDE TWITCH-CLIPS EN YOUTUBE-VIDEO'S KUNNEN IN DE WACHTRIJ!"],
            chill: ["alleen geaccepteerde twitch-clips en youtube-video's."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.NotFound,
            informative: ["Ik kon die clip of video niet vinden."],
            friendly:
            [
                "Ik kon die clip of video niet vinden. Controleer de link en probeer het nog eens!",
            ],
            sassy: ["Die clip bestaat niet. Of hij is erg verlegen."],
            hype: ["CLIP NIET GEVONDEN! CONTROLEER DE LINK!"],
            chill: ["kon die clip of video niet vinden."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.TooLong,
            informative: ["Die clip is langer dan dit kanaal toestaat."],
            friendly: ["Die is wat te lang voor dit kanaal. Probeer een kortere!"],
            sassy: ["Dat is te lang. We hebben nog meer te doen. Kies iets korters."],
            hype: ["TE LANG VOOR DE WACHTRIJ! KIES EEN KORTERE!"],
            chill: ["te lang voor dit kanaal."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.NotEligible,
            informative: ["Je kunt op dit moment geen media indienen op dit kanaal."],
            friendly: ["Sorry, je kunt hier op dit moment geen media indienen."],
            sassy: ["Je staat niet op de lijst voor media-inzendingen."],
            hype: ["MEDIA INDIENEN IS OP DIT MOMENT GEBLOKKEERD VOOR JOU!"],
            chill: ["je kunt nu geen media indienen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.Cooldown,
            informative: ["Je dient te snel in. Wacht even voor de volgende."],
            friendly: ["Rustig aan, wacht even voor je volgende inzending!"],
            sassy: ["Rustig maar. Eén clip tegelijk, graag."],
            hype: ["TE SNEL! WACHT EVEN VOOR DE VOLGENDE!"],
            chill: ["te snel, wacht even."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.QueueFull,
            informative:
            [
                "De mediawachtrij is vol. Probeer het opnieuw als er items zijn afgespeeld.",
            ],
            friendly:
            [
                "De wachtrij is nu vol. Probeer het opnieuw zodra er clips zijn afgespeeld!",
            ],
            sassy: ["De wachtrij zit vol. Wacht op je beurt."],
            hype: ["WACHTRIJ VOL! PROBEER HET OPNIEUW ALS ER CLIPS ZIJN AFGESPEELD!"],
            chill: ["wachtrij zit vol, probeer het later weer."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.Unavailable,
            informative: ["Ik kon dat nu niet opzoeken. Probeer het zo nog eens."],
            friendly: ["Ik kon dat net niet opzoeken. Probeer het zo nog eens!"],
            sassy: ["Het internet zei nee. Probeer het zo nog eens."],
            hype: ["OPZOEKEN MISLUKT! PROBEER HET ZO NOG EENS!"],
            chill: ["kon dat niet opzoeken, probeer het weer."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Media.Failed,
            informative:
            [
                "Ik kon dat niet indienen. Controleer je punten en probeer het nog eens.",
            ],
            friendly:
            [
                "Sorry, ik kon dat niet indienen. Controleer je punten en probeer het nog eens!",
            ],
            sassy: ["Inzending mislukt. Check misschien je portemonnee."],
            hype: ["INZENDING MISLUKT! CONTROLEER JE PUNTEN EN PROBEER HET NOG EENS!"],
            chill: ["kon dat niet indienen. check je punten."]
        );
    }

    private static void AddDutchPermitSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Permit.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.Usage,
            informative: ["Gebruik: !permit @gebruiker <rol|capability> [minuten]"],
            friendly:
            [
                "Typ !permit @gebruiker <rol|capability> [minuten] om iemand een permit te geven.",
            ],
            sassy: ["Permit voor wie, en waarvoor? !permit @gebruiker <rol|capability> [minuten]"],
            hype: ["!permit @GEBRUIKER <rol|capability> [MINUTEN] — LATEN WE DELEGEREN!"],
            chill: ["!permit @gebruiker <rol|capability> [minuten]"]
        );

        AddDutchPermitSupportSlots(catalog, key, "permit");

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.GrantedRole,
            informative: ["{permit.role} gegeven aan {user}."],
            friendly: ["{user} heeft nu {permit.role}!"],
            sassy: ["{user} is nu {permit.role}. Laten we hopen dat het goed uitpakt."],
            hype: ["{user} IS NU {permit.role}! GROTE ZET!"],
            chill: ["{permit.role} gegeven aan {user}."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.GrantedCapability,
            informative: ["{permit.capability} gegeven aan {user}."],
            friendly: ["{user} mag nu {permit.capability} gebruiken!"],
            sassy: ["{user} heeft nu {permit.capability}. Gebruik het verstandig."],
            hype: ["{user} KRIJGT {permit.capability}! POWER-UP!"],
            chill: ["{permit.capability} gegeven aan {user}."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.RoleTooHigh,
            informative: ["Je kunt geen rol boven je eigen niveau toewijzen."],
            friendly: ["Sorry, je kunt alleen rollen toewijzen tot je eigen niveau."],
            sassy: ["Je kunt geen rol uitdelen die hoger is dan de jouwe. Goed geprobeerd."],
            hype: ["DIE ROL STAAT BOVEN JOUW NIVEAU! KAN NIET!"],
            chill: ["kan geen rol boven je eigen niveau geven."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.CapabilityDenied,
            informative:
            [
                "Je kunt '{permit.capability}' niet toewijzen. Het moet bestaan, te delegeren zijn en iets zijn dat je zelf hebt.",
            ],
            friendly:
            [
                "Sorry, ik kan '{permit.capability}' niet toewijzen. Het moet bestaan, te delegeren zijn en iets zijn dat je zelf hebt.",
            ],
            sassy:
            [
                "'{permit.capability}' is niet van jou om weg te geven: het moet bestaan, te delegeren zijn en van jezelf zijn.",
            ],
            hype:
            [
                "KAN '{permit.capability}' NIET TOEWIJZEN! HET MOET BESTAAN, TE DELEGEREN ZIJN EN VAN JEZELF ZIJN!",
            ],
            chill:
            [
                "kan '{permit.capability}' niet toewijzen: het moet bestaan, te delegeren zijn en van jezelf zijn.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.GrantFailed,
            informative: ["De permit kon niet worden gegeven."],
            friendly: ["Sorry, ik kon die permit niet geven. Probeer het nog eens."],
            sassy: ["De permit ging niet door. Niet mijn schuld."],
            hype: ["DE PERMIT IS MISLUKT! PROBEER HET NOG EENS!"],
            chill: ["kon die permit niet geven."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.GrantedSongRequest,
            informative: ["{track.name} voor {user} in de wachtrij gezet."],
            friendly: ["Het verzoek {track.name} van {user} staat nu in de wachtrij!"],
            sassy: ["Vooruit, {track.name} voor {user} gaat de wachtrij in."],
            hype: ["{track.name} VOOR {user} STAAT IN DE WACHTRIJ! HIERMEE!"],
            chill: ["{track.name} voor {user} in de wachtrij gezet."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.NoPendingRequest,
            informative: ["Er wacht geen nummerverzoek van {user} op toestemming."],
            friendly: ["Ik hou op dit moment geen nummerverzoek van {user} vast."],
            sassy: ["Bij {user} wacht niets op een permit."],
            hype: ["GEEN VERZOEK VAN {user} OM TE PERMITTEN!"],
            chill: ["niets vastgehouden voor {user}."]
        );
    }

    private static void AddDutchUnpermitSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Unpermit.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unpermit.Usage,
            informative: ["Gebruik: !unpermit @gebruiker [rol|capability]"],
            friendly: ["Typ !unpermit @gebruiker [rol|capability] om een permit weg te halen."],
            sassy: ["Unpermit voor wie? !unpermit @gebruiker [rol|capability]"],
            hype: ["!unpermit @GEBRUIKER [ROL|CAPABILITY] — NEEM HET TERUG!"],
            chill: ["!unpermit @gebruiker [rol|capability]"]
        );

        AddDutchPermitSupportSlots(catalog, key, "unpermit");

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unpermit.RevokedAll,
            informative: ["Alle permits van {user} ingetrokken."],
            friendly: ["Alle permits van {user} zijn weggehaald."],
            sassy: ["{user} is elke permit kwijt. Au."],
            hype: ["ALLE PERMITS VAN {user} INGETROKKEN!"],
            chill: ["alle permits van {user} weggehaald."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unpermit.Revoked,
            informative: ["{permit.capability} ingetrokken van {user}."],
            friendly: ["{permit.capability} is weggehaald bij {user}."],
            sassy: ["{user} heeft {permit.capability} niet meer. Het was leuk zolang het duurde."],
            hype: ["{permit.capability} INGETROKKEN VAN {user}!"],
            chill: ["{permit.capability} weggehaald bij {user}."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unpermit.RevokeFailed,
            informative: ["De permit kon niet worden ingetrokken."],
            friendly: ["Sorry, ik kon die permit niet intrekken. Probeer het nog eens."],
            sassy: ["De permit wil niet weg. Probeer het nog eens."],
            hype: ["INTREKKEN MISLUKT! PROBEER HET NOG EENS!"],
            chill: ["kon die permit niet intrekken."]
        );
    }

    private static void AddDutchPermitSupportSlots(
        Dictionary<(string, string), SlotEntry> catalog,
        string key,
        string verb
    )
    {
        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.NotAllowed,
            informative: [$"{verb}: je mag geen permits beheren (vereist permit:issue)"],
            friendly: ["Sorry, je mag in dit kanaal geen permits beheren."],
            sassy: ["Permits beheren staat boven jouw salarisschaal."],
            hype: ["PERMITS ZIJN GEBLOKKEERD VOOR JOU!"],
            chill: ["je kunt hier geen permits beheren."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.AccountUnresolved,
            informative: [$"{verb}: je account kon niet worden gevonden"],
            friendly: ["Ik kon je account net niet vinden, probeer het nog eens!"],
            sassy: ["Je account verstopt zich voor me. Probeer het nog eens."],
            hype: ["ACCOUNT NIET GEVONDEN. PROBEER HET NOG EENS, KAMPIOEN."],
            chill: ["kon je account niet vinden, probeer het nog eens."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.NoTarget,
            informative: [$"{verb}: geen doelwit, noem een gebruiker met @naam"],
            friendly: ["Voor wie is het? Noem een gebruiker met @naam!"],
            sassy: ["Geen doelwit. Noem een gebruiker met @naam."],
            hype: ["GEEN DOELWIT! NOEM EEN GEBRUIKER MET @NAAM!"],
            chill: ["geen doelwit, noem een gebruiker met @naam."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.TargetNotFound,
            informative: [$"{verb}: '{{user}}' is niet gevonden op Twitch"],
            friendly: ["Ik kon '{user}' niet vinden op Twitch. Controleer de naam!"],
            sassy: ["'{user}' bestaat niet op Twitch. Of verstopt zich goed."],
            hype: ["'{user}' NIET GEVONDEN OP TWITCH! CONTROLEER DE NAAM!"],
            chill: ["'{user}' niet gevonden op twitch."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Permit.TargetUnresolved,
            informative: [$"{verb}: de doelgebruiker kon niet worden gevonden"],
            friendly: ["Ik kon die gebruiker net niet opzoeken, probeer het nog eens!"],
            sassy: ["Die gebruiker glipte door mijn vingers. Probeer het nog eens."],
            hype: ["KON DIE GEBRUIKER NIET VINDEN! PROBEER HET NOG EENS!"],
            chill: ["kon die gebruiker niet vinden."]
        );
    }
}
