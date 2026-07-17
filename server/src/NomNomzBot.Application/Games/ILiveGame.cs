// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Games;

/// <summary>
/// The drop-in game contract (live-games.md §4). A game is PURE LOGIC: it reads the engine-supplied
/// <see cref="LiveGameState"/>, mutates only its own <c>Data</c> bag, and returns a transition. It never
/// touches the DB, currency, chat, tokens, or SignalR — the engine brokers all of that. Adding a game is a
/// new implementor (auto-discovered) plus its overlay widget plus a default config — zero engine edits.
/// </summary>
public interface ILiveGame
{
    /// <summary>Unique key, e.g. <c>drop_game</c> — equals the <c>GameConfig.GameType</c> that configures it.</summary>
    string GameKey { get; }

    LiveGameManifest Manifest { get; }

    Task<LiveGameTransition> OnStartAsync(LiveGameState state, CancellationToken ct);

    Task<LiveGameTransition> OnInputAsync(
        LiveGameState state,
        LiveGameInput input,
        CancellationToken ct
    );

    /// <summary>Called every <see cref="LiveGameManifest.TickInterval"/>; no-op allowed for non-timed games.</summary>
    Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct);

    Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct);
}

/// <summary>How the engine runs a game: its join keywords, overlay widget, player bounds, and timing.</summary>
public sealed record LiveGameManifest(
    string DisplayName,
    IReadOnlyList<string> InputKeywords,
    string OverlayWidgetKey,
    int MinPlayers,
    int MaxPlayers,
    TimeSpan LobbyWindow,
    TimeSpan? TickInterval,
    bool RequiresEntryFee
);
