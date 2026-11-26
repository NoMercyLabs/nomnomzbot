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

/// <summary>English reply text of the live-round announcements (<c>BuiltinResponseSlots.LiveGame.cs</c>).</summary>
public static partial class ToneTemplateCatalog
{
    private static void AddLiveGameSlots(Dictionary<(string, string), SlotEntry> catalog)
    {
        const string key = BuiltinResponseSlots.LiveGame.Key;

        Add(
            catalog,
            key,
            BuiltinResponseSlots.LiveGame.Opened,
            variables: ["game.name", "game.join", "game.lobby"],
            informative:
            [
                "{game.name} is open! Type {game.join} to join. You have {game.lobby} seconds.",
            ],
            friendly:
            [
                "{game.name} just opened! Type {game.join} to join in. You have {game.lobby} seconds.",
            ],
            sassy:
            [
                "{game.name} is open. Type {game.join} if you dare. {game.lobby} seconds, no more.",
            ],
            hype:
            [
                "{game.name} IS OPEN! TYPE {game.join} TO JOIN! {game.lobby} SECONDS ON THE CLOCK!",
            ],
            chill: ["{game.name} is open. type {game.join} to join. {game.lobby} seconds."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.LiveGame.OpenedBet,
            variables: ["game.name", "game.join", "game.lobby", "game.minbet"],
            informative:
            [
                "{game.name} is open! Type {game.join} to join, or {game.join} <bet> to pick your bet (minimum {game.minbet}). You have {game.lobby} seconds.",
            ],
            friendly:
            [
                "{game.name} just opened! Type {game.join} to join in, or {game.join} <bet> to pick your bet (minimum {game.minbet}). You have {game.lobby} seconds.",
            ],
            sassy:
            [
                "{game.name} is open. Type {game.join} <bet> if you dare. The minimum is {game.minbet}. {game.lobby} seconds, no more.",
            ],
            hype:
            [
                "{game.name} IS OPEN! TYPE {game.join} <BET> TO JOIN! MINIMUM {game.minbet}! {game.lobby} SECONDS ON THE CLOCK!",
            ],
            chill:
            [
                "{game.name} is open. type {game.join} <bet> to join. minimum {game.minbet}. {game.lobby} seconds.",
            ]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.LiveGame.Settled,
            variables: ["game.name", "game.winners", "game.payout"],
            informative: ["{game.name} is over. Winners: {game.winners}. Paid out: {game.payout}."],
            friendly:
            [
                "{game.name} is over! {game.winners} won, and {game.payout} was paid out. Well played, everyone!",
            ],
            sassy:
            [
                "{game.name} is over. Winners: {game.winners}. Paid out: {game.payout}. The rest of you know who you are.",
            ],
            hype:
            [
                "{game.name} IS OVER! {game.winners} WON! {game.payout} PAID OUT! WHAT A ROUND!",
            ],
            chill: ["{game.name} is over. {game.winners} won. {game.payout} paid out."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.LiveGame.CancelledFewPlayers,
            variables: ["game.name"],
            informative:
            [
                "{game.name} closed because not enough people joined. Any bets are refunded.",
            ],
            friendly:
            [
                "{game.name} closed because not enough people joined. Any bets are back with you. Next time!",
            ],
            sassy:
            [
                "{game.name} closed. Not enough people joined. Any bets are refunded. Shy crowd.",
            ],
            hype:
            [
                "{game.name} CLOSED. NOT ENOUGH PLAYERS! ANY BETS ARE REFUNDED. BRING FRIENDS NEXT TIME!",
            ],
            chill: ["{game.name} closed. not enough people joined. any bets are refunded."]
        );

        Add(
            catalog,
            key,
            BuiltinResponseSlots.LiveGame.Cancelled,
            variables: ["game.name"],
            informative: ["{game.name} was cancelled. Any bets are refunded."],
            friendly: ["{game.name} was cancelled, sorry! Any bets are back with you."],
            sassy: ["{game.name} was cancelled. Any bets are refunded. Nobody wins today."],
            hype: ["{game.name} WAS CANCELLED! ANY BETS ARE REFUNDED. WE'LL BE BACK!"],
            chill: ["{game.name} was cancelled. any bets are refunded."]
        );
    }

    private static void AddLiveGameSamples(Dictionary<string, string> samples)
    {
        samples["game.join"] = "!drop";
        samples["game.lobby"] = "60";
        samples["game.minbet"] = "10";
        samples["game.winners"] = "3";
    }
}
