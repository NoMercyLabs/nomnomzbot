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

/// <summary>Dutch text of the live-round announcements; the English text is in <c>ToneTemplateCatalog.LiveGame.cs</c>.</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddDutchLiveGameSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.LiveGame.Key;

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.LiveGame.Opened,
            informative:
            [
                "{game.name} is open! Typ {game.join} om mee te doen. Je hebt {game.lobby} seconden.",
            ],
            friendly:
            [
                "{game.name} is net open gegaan! Typ {game.join} om mee te doen. Je hebt {game.lobby} seconden.",
            ],
            sassy:
            [
                "{game.name} is open. Typ {game.join} als je durft. {game.lobby} seconden, niet meer.",
            ],
            hype:
            [
                "{game.name} IS OPEN! TYP {game.join} OM MEE TE DOEN! {game.lobby} SECONDEN OP DE KLOK!",
            ],
            chill: ["{game.name} is open. typ {game.join} om mee te doen. {game.lobby} seconden."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.LiveGame.OpenedBet,
            informative:
            [
                "{game.name} is open! Typ {game.join} om mee te doen, of {game.join} <inzet> om je inzet te kiezen (minimaal {game.minbet}). Je hebt {game.lobby} seconden.",
            ],
            friendly:
            [
                "{game.name} is net open gegaan! Typ {game.join} om mee te doen, of {game.join} <inzet> om je inzet te kiezen (minimaal {game.minbet}). Je hebt {game.lobby} seconden.",
            ],
            sassy:
            [
                "{game.name} is open. Typ {game.join} <inzet> als je durft. Het minimum is {game.minbet}. {game.lobby} seconden, niet meer.",
            ],
            hype:
            [
                "{game.name} IS OPEN! TYP {game.join} <INZET> OM MEE TE DOEN! MINIMAAL {game.minbet}! {game.lobby} SECONDEN OP DE KLOK!",
            ],
            chill:
            [
                "{game.name} is open. typ {game.join} <inzet> om mee te doen. minimaal {game.minbet}. {game.lobby} seconden.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.LiveGame.Settled,
            informative:
            [
                "{game.name} is afgelopen. Winnaars: {game.winners}. Uitbetaald: {game.payout}.",
            ],
            friendly:
            [
                "{game.name} is afgelopen! {game.winners} hebben gewonnen en {game.payout} is uitbetaald. Goed gespeeld, allemaal!",
            ],
            sassy:
            [
                "{game.name} is afgelopen. Winnaars: {game.winners}. Uitbetaald: {game.payout}. De rest weet wie ze zijn.",
            ],
            hype:
            [
                "{game.name} IS AFGELOPEN! {game.winners} HEBBEN GEWONNEN! {game.payout} UITBETAALD! WAT EEN RONDE!",
            ],
            chill:
            [
                "{game.name} is afgelopen. {game.winners} hebben gewonnen. {game.payout} uitbetaald.",
            ]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.LiveGame.CancelledFewPlayers,
            informative:
            [
                "{game.name} is gesloten omdat er te weinig mensen meededen. Inzetten worden teruggegeven.",
            ],
            friendly:
            [
                "{game.name} is gesloten omdat er te weinig mensen meededen. Je inzet krijg je terug. Tot de volgende keer!",
            ],
            sassy:
            [
                "{game.name} is gesloten. Te weinig mensen. Inzetten worden teruggegeven. Verlegen publiek.",
            ],
            hype:
            [
                "{game.name} IS GESLOTEN. TE WEINIG SPELERS! INZETTEN WORDEN TERUGGEGEVEN. NEEM VOLGENDE KEER VRIENDEN MEE!",
            ],
            chill: ["{game.name} is gesloten. te weinig mensen. inzetten worden teruggegeven."]
        );

        Translate(
            catalog,
            Dutch,
            key,
            BuiltinResponseSlots.LiveGame.Cancelled,
            informative: ["{game.name} is geannuleerd. Inzetten worden teruggegeven."],
            friendly: ["{game.name} is geannuleerd, sorry! Je inzet krijg je terug."],
            sassy:
            [
                "{game.name} is geannuleerd. Inzetten worden teruggegeven. Niemand wint vandaag.",
            ],
            hype: ["{game.name} IS GEANNULEERD! INZETTEN WORDEN TERUGGEGEVEN. WE KOMEN TERUG!"],
            chill: ["{game.name} is geannuleerd. inzetten worden teruggegeven."]
        );
    }
}
