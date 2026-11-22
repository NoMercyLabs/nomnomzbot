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

/// <summary>Dutch text of the whitelist and mass-ban built-ins; the English text is in the Whitelist and MassBan files.</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddDutchModerationSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddDutchWhitelistSlots(catalog);
        AddDutchUnwhitelistSlots(catalog);
        AddDutchMassBanSlots(catalog);
    }

    private static void AddDutchWhitelistSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Whitelist.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Whitelist.Usage,
            informative: ["Gebruik: !whitelist @gebruiker subscriber/vip/moderator"],
            friendly:
            [
                "Wil je iemand whitelisten? Typ !whitelist @gebruiker subscriber/vip/moderator.",
            ],
            sassy: ["Wie whitelisten, en als wat? !whitelist @gebruiker subscriber/vip/moderator"],
            hype: ["!WHITELIST @GEBRUIKER SUBSCRIBER/VIP/MODERATOR — GA ERVOOR!"],
            chill: ["typ !whitelist @gebruiker subscriber/vip/moderator"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Whitelist.NotAllowed,
            informative: ["Alleen de broadcaster kan !whitelist gebruiken."],
            friendly: ["Sorry, alleen de broadcaster kan !whitelist gebruiken."],
            sassy: ["!whitelist is voor de broadcaster. Jij bent het niet."],
            hype: ["ALLEEN DE BROADCASTER KAN WHITELISTEN!"],
            chill: ["alleen de broadcaster kan whitelisten."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Whitelist.InvalidLevel,
            informative:
            [
                "Ongeldig level \"{whitelist.level}\". Geldige levels: subscriber, vip, moderator",
            ],
            friendly:
            [
                "Het level \"{whitelist.level}\" ken ik niet. Kies subscriber, vip of moderator!",
            ],
            sassy: ["\"{whitelist.level}\" is geen level. Probeer subscriber, vip of moderator."],
            hype: ["\"{whitelist.level}\" IS GEEN LEVEL! KIES SUBSCRIBER, VIP OF MODERATOR!"],
            chill: ["\"{whitelist.level}\" is geen level. subscriber, vip of moderator."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Whitelist.TargetNotFound,
            informative: ["Gebruiker \"{user}\" niet gevonden op Twitch."],
            friendly: ["Ik kon \"{user}\" niet vinden op Twitch. Check de naam!"],
            sassy: ["\"{user}\" bestaat niet op Twitch. Leuke poging."],
            hype: ["\"{user}\" NIET GEVONDEN OP TWITCH!"],
            chill: ["kon \"{user}\" niet vinden op twitch."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Whitelist.Granted,
            informative: ["@{user} heeft {whitelist.level} toegang gekregen."],
            friendly: ["@{user} heeft nu {whitelist.level} toegang!"],
            sassy: ["@{user} heeft {whitelist.level} toegang. Ga er netjes mee om."],
            hype: ["@{user} IS NU {whitelist.level}! LET'S GO!"],
            chill: ["gaf @{user} {whitelist.level} toegang."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Whitelist.Failed,
            informative: ["De whitelist kon niet worden opgeslagen."],
            friendly: ["Sorry, ik kon die whitelist niet opslaan. Probeer het nog eens."],
            sassy: ["De whitelist bleef niet hangen. Probeer het nog eens."],
            hype: ["DE WHITELIST IS MISLUKT! PROBEER HET OPNIEUW!"],
            chill: ["kon die whitelist niet opslaan."]
        );
    }

    private static void AddDutchUnwhitelistSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.Unwhitelist.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unwhitelist.Usage,
            informative: ["Gebruik: !unwhitelist @gebruiker"],
            friendly: ["Wil je een whitelist weghalen? Typ !unwhitelist @gebruiker."],
            sassy: ["Wie unwhitelisten? !unwhitelist @gebruiker"],
            hype: ["!UNWHITELIST @GEBRUIKER — HAAL HET TERUG!"],
            chill: ["typ !unwhitelist @gebruiker"]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unwhitelist.NotAllowed,
            informative: ["Alleen de broadcaster kan !unwhitelist gebruiken."],
            friendly: ["Sorry, alleen de broadcaster kan !unwhitelist gebruiken."],
            sassy: ["!unwhitelist is voor de broadcaster. Jij bent het niet."],
            hype: ["ALLEEN DE BROADCASTER KAN UNWHITELISTEN!"],
            chill: ["alleen de broadcaster kan unwhitelisten."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unwhitelist.TargetNotFound,
            informative: ["Gebruiker \"{user}\" niet gevonden op Twitch."],
            friendly: ["Ik kon \"{user}\" niet vinden op Twitch. Check de naam!"],
            sassy: ["\"{user}\" bestaat niet op Twitch. Leuke poging."],
            hype: ["\"{user}\" NIET GEVONDEN OP TWITCH!"],
            chill: ["kon \"{user}\" niet vinden op twitch."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unwhitelist.Removed,
            informative: ["De permissie-override van @{user} is verwijderd."],
            friendly: ["De permissie-override van @{user} is weggehaald."],
            sassy: ["@{user} is de override kwijt. Weer gewoon."],
            hype: ["DE OVERRIDE VAN @{user} IS WEG!"],
            chill: ["permissie-override van @{user} weggehaald."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unwhitelist.NoOverride,
            informative: ["@{user} had geen permissie-override."],
            friendly: ["@{user} had helemaal geen permissie-override."],
            sassy: ["@{user} had niets om weg te halen."],
            hype: ["@{user} HAD GEEN OVERRIDE!"],
            chill: ["@{user} had geen override."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.Unwhitelist.Failed,
            informative: ["De whitelist kon niet worden verwijderd."],
            friendly: ["Sorry, ik kon die whitelist niet verwijderen. Probeer het nog eens."],
            sassy: ["De whitelist wil niet weg. Probeer het nog eens."],
            hype: ["VERWIJDEREN MISLUKT! PROBEER HET OPNIEUW!"],
            chill: ["kon die whitelist niet verwijderen."]
        );
    }

    private static void AddDutchMassBanSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        AddDutchFormal(
            catalog,
            BuiltinResponseSlots.Allow.Key,
            BuiltinResponseSlots.Allow.Usage,
            "Gebruik: {prefix}allow massban"
        );
        AddDutchFormal(
            catalog,
            BuiltinResponseSlots.Allow.Key,
            BuiltinResponseSlots.Allow.NothingPending,
            "Er wacht geen mass ban op goedkeuring in dit kanaal."
        );
        AddDutchFormal(
            catalog,
            BuiltinResponseSlots.Allow.Key,
            BuiltinResponseSlots.Allow.Approved,
            "Mass ban goedgekeurd. {massban.count} accounts worden nu gebanned."
        );

        AddDutchFormal(
            catalog,
            BuiltinResponseSlots.Disallow.Key,
            BuiltinResponseSlots.Disallow.Usage,
            "Gebruik: {prefix}disallow massban"
        );
        AddDutchFormal(
            catalog,
            BuiltinResponseSlots.Disallow.Key,
            BuiltinResponseSlots.Disallow.NothingPending,
            "Er wacht geen mass ban op goedkeuring in dit kanaal."
        );
        AddDutchFormal(
            catalog,
            BuiltinResponseSlots.Disallow.Key,
            BuiltinResponseSlots.Disallow.Declined,
            "Mass ban afgewezen. De {massban.count} accounts worden in dit kanaal niet gebanned."
        );

        AddDutchFormal(
            catalog,
            BuiltinResponseSlots.MassBan.Key,
            BuiltinResponseSlots.MassBan.Request,
            "@{massban.channel} Moderator {massban.moderator} heeft een ban aangevraagd voor {massban.count} "
                + "accounts die bij een botaanval horen. Omdat de stream live is, is er nog niets gedaan. De "
                + "broadcaster, een lead moderator of een editor kan {prefix}allow massban typen om ze nu te "
                + "bannen, of {prefix}disallow massban om te weigeren. Zonder reactie worden de bans "
                + "uitgevoerd als de stream eindigt."
        );
        AddDutchFormal(
            catalog,
            BuiltinResponseSlots.MassBan.Key,
            BuiltinResponseSlots.MassBan.Completed,
            "Mass ban voltooid: {massban.banned} van {massban.count} accounts gebanned."
        );
    }

    // One formal text for every tone, as the English mass-ban replies do.
    private static void AddDutchFormal(
        Dictionary<(string, string), SlotEntry> catalog,
        string builtinKey,
        string slot,
        string text
    ) =>
        Translate(
            catalog,
            Dutch,
            builtinKey,
            slot,
            informative: [text],
            friendly: [text],
            sassy: [text],
            hype: [text],
            chill: [text]
        );
}
