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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>
/// Speaks the three live-round lines (<see cref="BuiltinResponseSlots.LiveGame"/>) in the channel's
/// personality through the same composer every built-in reply uses, so a streamer can re-word them. The
/// engine calls it directly, not through the domain events: the line needs the manifest (join word, lobby
/// seconds) and the startup sweep, which publishes the same cancel event, must stay silent.
/// </summary>
public sealed class LiveGameAnnouncer(
    IBuiltinResponseComposer composer,
    IApplicationDbContext db,
    IChatProvider chat,
    ILogger<LiveGameAnnouncer> logger
) : ILiveGameAnnouncer
{
    private const string MinPlayersUnmet = "min_players_unmet";

    public Task AnnounceOpenedAsync(
        Guid broadcasterId,
        LiveGameManifest manifest,
        long minBet,
        CancellationToken ct = default
    ) =>
        SayAsync(
            broadcasterId,
            manifest.RequiresEntryFee
                ? BuiltinResponseSlots.LiveGame.OpenedBet
                : BuiltinResponseSlots.LiveGame.Opened,
            $"{manifest.DisplayName} is open! Type {JoinWord(manifest)} to join.",
            new()
            {
                ["game.name"] = manifest.DisplayName,
                ["game.join"] = JoinWord(manifest),
                ["game.lobby"] = ((int)manifest.LobbyWindow.TotalSeconds).ToString(),
                ["game.minbet"] = minBet.ToString(),
            },
            ct
        );

    public Task AnnounceSettledAsync(
        Guid broadcasterId,
        LiveGameManifest manifest,
        int winnerCount,
        long totalPaidOut,
        CancellationToken ct = default
    ) =>
        SayAsync(
            broadcasterId,
            BuiltinResponseSlots.LiveGame.Settled,
            $"{manifest.DisplayName} is over. Winners: {winnerCount}. Paid out: {totalPaidOut}.",
            new()
            {
                ["game.name"] = manifest.DisplayName,
                ["game.winners"] = winnerCount.ToString(),
                ["game.payout"] = totalPaidOut.ToString(),
            },
            ct
        );

    public Task AnnounceCancelledAsync(
        Guid broadcasterId,
        LiveGameManifest manifest,
        string reason,
        CancellationToken ct = default
    ) =>
        SayAsync(
            broadcasterId,
            reason == MinPlayersUnmet
                ? BuiltinResponseSlots.LiveGame.CancelledFewPlayers
                : BuiltinResponseSlots.LiveGame.Cancelled,
            $"{manifest.DisplayName} was canceled. Any bets are refunded.",
            new() { ["game.name"] = manifest.DisplayName },
            ct
        );

    /// <summary>The first chat word of the game, always with its "!" so the line names what to type.</summary>
    private static string JoinWord(LiveGameManifest manifest)
    {
        string word = manifest.InputKeywords.FirstOrDefault() ?? manifest.DisplayName;
        return word.StartsWith('!') ? word : $"!{word}";
    }

    private async Task SayAsync(
        Guid broadcasterId,
        string slot,
        string neutralFallback,
        Dictionary<string, string> variables,
        CancellationToken ct
    )
    {
        // Chat is a courtesy: a failed line must never fail, stall or roll back the round it describes.
        try
        {
            string? stored = await db
                .Channels.AsNoTracking()
                .Where(c => c.Id == broadcasterId)
                .Select(c => c.Personality)
                .FirstOrDefaultAsync(ct);
            string line = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = broadcasterId,
                    Personality = PersonalityTone.Normalize(stored),
                    BuiltinKey = BuiltinResponseSlots.LiveGame.Key,
                    Slot = slot,
                    NeutralFallback = neutralFallback,
                    Variables = variables,
                },
                ct
            );
            if (!string.IsNullOrWhiteSpace(line))
                await chat.SendMessageAsync(broadcasterId, line, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Live game announcement {Slot} failed for {BroadcasterId}",
                slot,
                broadcasterId
            );
        }
    }
}
