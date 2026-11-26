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

/// <summary>Dutch text of the core built-ins; the English text is in <c>ToneTemplateCatalog.Core.cs</c>.</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddDutchCoreSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddDutchLurkAndAccountAgeSlots(catalog);
        AddDutchFollowAgeSlots(catalog);
        AddDutchWhisperAndUpdateSlots(catalog);
        AddDutchDiscordSlots(catalog);
        AddDutchLeaderboardSlots(catalog);
        AddDutchRewardSlots(catalog);
        AddDutchSystemSlots(catalog);
        foreach (string gameKey in GameKeys)
            AddDutchGameSlots(catalog, gameKey);
        AddDutchLiveGameSlots(catalog);
    }

    private static void AddDutchLurkAndAccountAgeSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Lurk.Key,
            BuiltinResponseSlots.Lurk.AccountUnresolved,
            informative: ["@{user} je account kon niet worden gevonden."],
            friendly: ["@{user} ik kon je account niet vinden — probeer je het nog een keer?"],
            sassy: ["@{user} je account is zoek. Gênant. Probeer het nog eens."],
            hype: ["@{user} JE ACCOUNT IS NIET GEVONDEN. PROBEER HET OPNIEUW."],
            chill: ["@{user} kon je account niet vinden."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.AccountUnresolved,
            informative: ["@{user} je account kon niet worden gevonden."],
            friendly: ["@{user} ik kon je account niet vinden — probeer je het nog een keer?"],
            sassy: ["@{user} je account is zoek. Gênant. Probeer het nog eens."],
            hype: ["@{user} JE ACCOUNT IS NIET GEVONDEN. PROBEER HET OPNIEUW."],
            chill: ["@{user} kon je account niet vinden."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.TwitchUnavailable,
            informative: ["@{user} Twitch reageert nu niet — probeer het zo opnieuw."],
            friendly: ["@{user} Twitch reageerde net niet — probeer je het zo nog eens?"],
            sassy: ["@{user} Twitch zwijgt. Mijn schuld niet. Probeer het zo nog eens."],
            hype: ["@{user} TWITCH IS STIL. PROBEER HET ZO OPNIEUW."],
            chill: ["@{user} twitch reageert niet, probeer het zo nog even."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.NotFound,
            informative: ["@{user} je account is niet gevonden op Twitch."],
            friendly:
            [
                "@{user} ik kon je account niet vinden op Twitch — vreemd! Probeer je het later nog eens?",
            ],
            sassy:
            [
                "@{user} Twitch zegt dat je niet bestaat. Gewaagde uitspraak. Probeer het later nog eens.",
            ],
            hype: ["@{user} GEEN ACCOUNT GEVONDEN OP TWITCH. PROBEER HET LATER OPNIEUW."],
            chill: ["@{user} kon je account niet vinden op twitch."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.Undetermined,
            informative: ["@{user} de leeftijd van je account is niet te bepalen."],
            friendly: ["@{user} ik kon de leeftijd van je account nu niet uitrekenen — sorry!"],
            sassy: ["@{user} de leeftijd van je account blijft een raadsel. Ook voor mij."],
            hype: ["@{user} LEEFTIJD VAN HET ACCOUNT ONBEKEND. HET RAADSEL WORDT GROTER."],
            chill: ["@{user} kon de leeftijd van je account niet uitrekenen."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.AgeOther,
            informative: ["Het account van {target} is aangemaakt op {date} ({age} geleden)."],
            friendly: ["Het account van {target} bestaat al sinds {date} — dat is {age}!"],
            sassy:
            [
                "Het account van {target} is aangemaakt op {date}. Dat is {age} aan twijfelachtige keuzes.",
            ],
            hype: ["HET ACCOUNT VAN {target} IS VAN {date}! DAT IS {age} AAN GROOTHEID!"],
            chill: ["het account van {target} is van {date}, dus ongeveer {age}."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.AccountAge.Key,
            BuiltinResponseSlots.AccountAge.UserNotFound,
            informative: ["@{user} Twitch kent geen gebruiker met de naam {target}."],
            friendly:
            [
                "@{user} ik kon niemand met de naam {target} vinden op Twitch — controleer je even de spelling?",
            ],
            sassy: ["@{user} Twitch heeft nog nooit van {target} gehoord. Spelling is moeilijk."],
            hype: ["@{user} NIEMAND MET DE NAAM {target} OP TWITCH. CONTROLEER DE NAAM!"],
            chill: ["@{user} niemand met de naam {target} op twitch."]
        );
    }

    private static void AddDutchFollowAgeSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.Age,
            informative: ["Je volgt al {age}!"],
            friendly: ["Je volgt al {age} — bedankt dat je blijft hangen!"],
            sassy: ["Je volgt al {age}. Knap volgehouden om steeds weer te komen opdagen."],
            hype: ["JE VOLGT AL {age}! LEGENDARISCH!"],
            chill: ["je volgt al {age}."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.NotFollowing,
            informative: ["Je volgt nog niet!"],
            friendly: ["Je volgt nog niet — druk op die follow-knop wanneer je wilt!"],
            sassy: ["Je volgt niet. Stoutmoedig dat je het vraagt. De knop zit gewoon daar."],
            hype: ["JE VOLGT NIET! DOE DAAR NU WAT AAN!"],
            chill: ["je volgt nog niet."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.TwitchUnavailable,
            informative: ["Twitch reageert nu niet — probeer het zo opnieuw."],
            friendly: ["Twitch reageerde net niet — probeer je het zo nog eens?"],
            sassy: ["Twitch zwijgt. Mijn schuld niet. Probeer het zo nog eens."],
            hype: ["TWITCH IS STIL. PROBEER HET ZO OPNIEUW."],
            chill: ["twitch reageert niet, probeer het zo nog even."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.AgeOther,
            informative: ["{target} volgt al {age}!"],
            friendly: ["{target} volgt al {age} — bedankt voor de trouw!"],
            sassy: ["{target} volgt al {age}. Knap volgehouden om steeds weer te komen opdagen."],
            hype: ["{target} VOLGT AL {age}! LEGENDARISCH!"],
            chill: ["{target} volgt al {age}."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.NotFollowingOther,
            informative: ["{target} volgt niet!"],
            friendly: ["{target} volgt nog niet — de follow-knop staat te wachten!"],
            sassy: ["{target} volgt niet. De knop zit gewoon daar."],
            hype: ["{target} VOLGT NIET! DOE DAAR NU WAT AAN!"],
            chill: ["{target} volgt nog niet."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.FollowAge.Key,
            BuiltinResponseSlots.FollowAge.UserNotFound,
            informative: ["@{user} Twitch kent geen gebruiker met de naam {target}."],
            friendly:
            [
                "@{user} ik kon niemand met de naam {target} vinden op Twitch — controleer je even de spelling?",
            ],
            sassy: ["@{user} Twitch heeft nog nooit van {target} gehoord. Spelling is moeilijk."],
            hype: ["@{user} NIEMAND MET DE NAAM {target} OP TWITCH. CONTROLEER DE NAAM!"],
            chill: ["@{user} niemand met de naam {target} op twitch."]
        );
    }

    private static void AddDutchWhisperAndUpdateSlots(
        Dictionary<(string, string), SlotEntry> catalog
    )
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.Sent,
            informative: ["Whisper naar {user} verstuurd."],
            friendly: ["Je whisper naar {user} is verstuurd!"],
            sassy: ["Whisper naar {user} verstuurd. Geheimen zijn veilig. Waarschijnlijk."],
            hype: ["WHISPER VERSTUURD NAAR {user}."],
            chill: ["whisper naar {user} verstuurd."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Whisper.Key,
            BuiltinResponseSlots.Whisper.SendFailed,
            informative: ["Kon geen whisper sturen naar {user}."],
            friendly: ["Het lukte net niet om {user} een whisper te sturen — sorry!"],
            sassy:
            [
                "Geen whisper naar {user} kunnen sturen. Misschien willen ze niets van ons horen.",
            ],
            hype: ["WHISPER NAAR {user} MISLUKT. PROBEER HET OPNIEUW."],
            chill: ["kon {user} geen whisper sturen."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.UpdateUserInfo.Key,
            BuiltinResponseSlots.UpdateUserInfo.Updated,
            informative: ["Gebruikersinfo van {user} bijgewerkt!"],
            friendly: ["Klaar — de info van {user} is weer actueel!"],
            sassy: ["De info van {user} is bijgewerkt. Graag gedaan."],
            hype: ["DE INFO VAN {user} IS BIJGEWERKT. VERS EN KLAAR."],
            chill: ["info van {user} bijgewerkt."]
        );
    }

    private static void AddDutchDiscordSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Discord.Key,
            BuiltinResponseSlots.Discord.Invite,
            informative: ["Word lid van de community op Discord: {discord.invite}"],
            friendly: ["Kom gezellig bij ons hangen op Discord: {discord.invite}"],
            sassy: ["We hebben een Discord. Ja, echt. Kom maar binnen: {discord.invite}"],
            hype: ["JOIN DE DISCORD! HET FEEST STOPT NOOIT: {discord.invite}"],
            chill: ["discord staat voor je klaar als je wilt: {discord.invite}"]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Discord.Key,
            BuiltinResponseSlots.Discord.NotConnected,
            informative: ["Dit kanaal heeft nog geen gekoppelde Discord-server."],
            friendly: ["Dit kanaal heeft nog geen gekoppelde Discord-server — misschien snel!"],
            sassy: ["Geen Discord-server gekoppeld. Iemand heeft huiswerk."],
            hype: ["NOG GEEN DISCORD GEKOPPELD. DE STREAMER HEEFT HUISWERK."],
            chill: ["hier is nog geen discord gekoppeld."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Discord.Key,
            BuiltinResponseSlots.Discord.Unavailable,
            informative:
            [
                "De Discord-uitnodiging is nu niet beschikbaar — probeer het zo opnieuw.",
            ],
            friendly: ["De Discord-uitnodiging is nu niet klaar — probeer je het zo nog eens?"],
            sassy: ["De Discord-uitnodiging doet moeilijk. Probeer het zo nog eens."],
            hype: ["DISCORD-UITNODIGING IS NU DOWN. PROBEER HET ZO OPNIEUW."],
            chill: ["discord-uitnodiging is nu niet beschikbaar, probeer het zo nog even."]
        );
    }

    private static void AddDutchLeaderboardSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Leaderboard.Key,
            BuiltinResponseSlots.Leaderboard.None,
            informative: ["Dit kanaal heeft nog geen leaderboard."],
            friendly: ["Dit kanaal heeft nog geen leaderboard — kom snel eens terug!"],
            sassy: ["Er is geen leaderboard. Niemand wint. Niemand verliest."],
            hype: ["NOG GEEN LEADERBOARD. DE RACE IS NOG NIET BEGONNEN."],
            chill: ["nog geen leaderboard ingesteld."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Leaderboard.Key,
            BuiltinResponseSlots.Leaderboard.Empty,
            informative: ["Het leaderboard heeft nog geen ranglijst."],
            friendly: ["Er staat nog niemand op het leaderboard — jij kunt de eerste zijn!"],
            sassy: ["Het leaderboard is leeg. Ga eens indruk maken, iemand."],
            hype: ["LEEG LEADERBOARD. DE EERSTE PLEK LIGT VOOR HET GRIJPEN."],
            chill: ["er staat nog niemand op het leaderboard."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Leaderboard.Key,
            BuiltinResponseSlots.Leaderboard.Top,
            informative: ["Ranglijst {leaderboard.metric}: {leaderboard.list}"],
            friendly:
            [
                "Dit zijn onze top {leaderboard.metric} — goed gedaan allemaal! {leaderboard.list}",
            ],
            sassy: ["De top {leaderboard.metric}, voor wie de score bijhoudt: {leaderboard.list}"],
            hype: ["TOP {leaderboard.metric}! DE LEGENDES: {leaderboard.list}"],
            chill: ["de top {leaderboard.metric}: {leaderboard.list}"]
        );
    }

    private static void AddDutchRewardSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.Reward.Key,
            BuiltinResponseSlots.Reward.NoPermission,
            informative:
            [
                "@{user}, je mag deze reward niet gebruiken. Je punten zijn terugbetaald.",
            ],
            friendly:
            [
                "@{user}, sorry, deze reward is niet voor jou beschikbaar. Je punten zijn terugbetaald!",
            ],
            sassy:
            [
                "@{user}, die reward is niet voor jou. Je punten zijn terug, geen kwaad gedaan.",
            ],
            hype: ["@{user}, DIE REWARD IS NOG NIET VOOR JOU ONTGRENDELD! PUNTEN TERUGGEGEVEN!"],
            chill: ["@{user}, die reward is niet voor jou. punten terugbetaald."]
        );
    }

    private static void AddDutchSystemSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.PermissionDenied,
            informative: ["Je mag dat commando niet gebruiken."],
            friendly: ["Sorry, dat commando is nu niet voor je beschikbaar."],
            sassy: ["Je hebt geen rechten voor dat commando. Goed geprobeerd, hoor."],
            hype: ["DAT COMMANDO IS NIET VOOR JOU. NOG NIET."],
            chill: ["je mag dat commando niet gebruiken."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.Cooldown,
            informative: ["Dat commando heeft nog cooldown."],
            friendly: ["Dat commando heeft even rust nodig — probeer het zo nog eens!"],
            sassy: ["Dat commando heeft nog cooldown. Geduld is een schone zaak."],
            hype: ["COOLDOWN ACTIEF. AAN HET OPLADEN VOOR DE VOLGENDE KEER."],
            chill: ["dat commando heeft cooldown."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.Failed,
            informative: ["Sorry, dat commando liep vast en is niet afgemaakt."],
            friendly: ["Oeps, dat commando liep vast en is niet afgemaakt — sorry!"],
            sassy: ["Dat commando liep vast en gaf het op. Niet mijn beste moment."],
            hype: ["DAT COMMANDO LIEP VAST. HET IS NIET AFGEMAAKT."],
            chill: ["dat commando liep vast en is niet afgemaakt."]
        );

        Translate(
            catalog,
            Dutch,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.NothingRan,
            informative: ["Dat commando heeft nog niets om uit te voeren."],
            friendly:
            [
                "Dat commando is nog niet ingesteld — de streamer is er misschien nog mee bezig!",
            ],
            sassy: ["Dat commando heeft niets erachter. Het is alleen show."],
            hype: ["DAT COMMANDO HEEFT NOG NIETS OM UIT TE VOEREN. KOMT ERAAN."],
            chill: ["dat commando is nog niet ingesteld."]
        );
    }

    /// <summary>The Dutch reply cases of one instant game, registered under the game's own trigger key.</summary>
    private static void AddDutchGameSlots(
        Dictionary<(string, string), SlotEntry> catalog,
        string key
    )
    {
        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.Usage,
            informative: ["Gebruik: !{game.name} <inzet>"],
            friendly: ["Bijna! Probeer: !{game.name} <inzet>"],
            sassy: ["Gebruik: !{game.name} <inzet>. Een getal. Boven nul. Dat is alles."],
            hype: ["GEBRUIK: !{game.name} <inzet>. KIES EEN GETAL EN GO."],
            chill: ["gebruik: !{game.name} <inzet>"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.NotEnabled,
            informative:
            [
                "!{game.name} staat nog niet aan — zet het aan onder Economy → Games. Games gebruiken de valuta van je kanaal, stel die daar dus ook in.",
            ],
            friendly:
            [
                "!{game.name} staat nog niet aan — de streamer kan het aanzetten onder Economy → Games, samen met de kanaalvaluta.",
            ],
            sassy:
            [
                "!{game.name} staat niet aan. Iemand moet het aanzetten onder Economy → Games, en ook de valuta instellen.",
            ],
            hype:
            [
                "!{game.name} STAAT NOG UIT. ZET HET AAN ONDER ECONOMY → GAMES EN STEL DE VALUTA IN.",
            ],
            chill:
            [
                "!{game.name} staat nog niet aan. zet het aan onder economy → games, plus de valuta.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.Unavailable,
            informative: ["Games zijn nu niet beschikbaar."],
            friendly: ["Games zijn nu niet beschikbaar — sorry daarvoor!"],
            sassy: ["Games zijn niet beschikbaar. De bank is dicht. Probeer het later nog eens."],
            hype: ["GAMES ZIJN NU DOWN. PROBEER HET STRAKS OPNIEUW."],
            chill: ["games zijn nu niet beschikbaar."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.Won,
            informative:
            [
                "Je hebt {game.payout} gewonnen met {game.name} (inzet {game.bet})! Saldo: {game.balance}",
            ],
            friendly:
            [
                "Gefeliciteerd, je hebt {game.payout} gewonnen met {game.name}! Je saldo is nu {game.balance}.",
            ],
            sassy:
            [
                "Je hebt {game.payout} gewonnen met {game.name}. Geluk, geen kunde. Saldo: {game.balance}",
            ],
            hype:
            [
                "JE HEBT {game.payout} GEWONNEN MET {game.name}! SALDO: {game.balance}. LEGENDE.",
            ],
            chill: ["{game.payout} gewonnen met {game.name}. saldo: {game.balance}"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.Lost,
            informative: ["Je hebt {game.bet} verloren met {game.name}. Saldo: {game.balance}"],
            friendly:
            [
                "Helaas, je hebt {game.bet} verloren met {game.name}. Volgende keer meer geluk! Saldo: {game.balance}",
            ],
            sassy:
            [
                "Je hebt {game.bet} verloren met {game.name}. De bank zegt bedankt. Saldo: {game.balance}",
            ],
            hype:
            [
                "{game.bet} VERLOREN MET {game.name}. TIJD VOOR EEN COMEBACK. SALDO: {game.balance}",
            ],
            chill: ["{game.bet} verloren met {game.name}. saldo: {game.balance}"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.BetOutOfRange,
            informative: ["De inzet valt buiten het toegestane bereik."],
            friendly: ["Die inzet valt buiten het toegestane bereik — probeer een ander bedrag!"],
            sassy: ["Die inzet valt buiten het toegestane bereik. Lees de limieten."],
            hype: ["INZET BUITEN BEREIK. KIES EEN BEDRAG BINNEN DE LIMIETEN."],
            chill: ["die inzet valt buiten het toegestane bereik."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.InsufficientFunds,
            informative: ["Onvoldoende saldo."],
            friendly: ["Je hebt niet genoeg voor die inzet — probeer een kleinere!"],
            sassy: ["Die inzet kun je niet betalen. Droom kleiner."],
            hype: ["NIET GENOEG SALDO VOOR DIE INZET. GA WAT MEER VERDIENEN."],
            chill: ["niet genoeg saldo voor die inzet."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.AgeConsentRequired,
            informative: ["Voor deze game moet je bevestigen dat je 18 jaar of ouder bent."],
            friendly:
            [
                "Om deze game te spelen, moet je eerst bevestigen dat je 18 jaar of ouder bent.",
            ],
            sassy:
            [
                "Voor deze game moet je bevestigen dat je 18 of ouder bent. Regels zijn regels.",
            ],
            hype: ["BEVESTIG DAT JE 18 JAAR OF OUDER BENT OM DEZE GAME TE SPELEN."],
            chill: ["je moet bevestigen dat je 18 of ouder bent om dit te spelen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.NotAllowed,
            informative: ["Je rol is niet hoog genoeg voor deze game."],
            friendly: ["Deze game staat nog niet open voor jouw rol — sorry!"],
            sassy: ["Je rol is niet hoog genoeg voor deze game. Klim maar omhoog."],
            hype: ["JE ROL IS NOG NIET HOOG GENOEG VOOR DEZE GAME."],
            chill: ["jouw rol mag deze game nog niet spelen."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.OnCooldown,
            informative: ["De game heeft cooldown."],
            friendly: ["Deze game heeft even rust nodig — probeer het zo nog eens!"],
            sassy: ["De game heeft cooldown. De bank moet even bijkomen."],
            hype: ["GAME OP COOLDOWN. LAAD OP EN KOM TERUG."],
            chill: ["de game heeft cooldown."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.StreamLimit,
            informative: ["Speellimiet per stream bereikt voor deze game."],
            friendly:
            [
                "Je hebt deze game zo vaak gespeeld als mag deze stream — tot de volgende keer!",
            ],
            sassy: ["Speellimiet per stream bereikt. De bank heeft vandaag genoeg van je gezien."],
            hype: ["SPEELLIMIET BEREIKT VOOR DEZE STREAM. KOM VOLGENDE KEER TERUG."],
            chill: ["speellimiet voor deze stream bereikt."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.CurrencyDisabled,
            informative: ["De valuta staat uit."],
            friendly: ["De kanaalvaluta staat nog niet aan, dus er valt niets in te zetten."],
            sassy: ["Er is geen kanaalvaluta aan. Er valt niets te gokken."],
            hype: ["NOG GEEN KANAALVALUTA. NIKS OM IN TE ZETTEN."],
            chill: ["de kanaalvaluta staat nog niet aan."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Game.PlayFailed,
            informative: ["Dat werkte niet — probeer het opnieuw."],
            friendly: ["Hm, dat werkte niet — probeer je het nog eens?"],
            sassy: ["Dat werkte niet. Probeer het opnieuw. Misschien langzamer."],
            hype: ["DAT WERKTE NIET. PROBEER HET OPNIEUW."],
            chill: ["dat werkte niet, probeer het opnieuw."]
        );
    }
}
