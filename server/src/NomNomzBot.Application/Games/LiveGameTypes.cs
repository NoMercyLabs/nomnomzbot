// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Economy.Enums;

namespace NomNomzBot.Application.Games;

/// <summary>The engine-side phase of an in-memory session (the non-terminal half of <c>GameSessionStatus</c>).</summary>
public enum LiveGamePhase
{
    Lobby,
    Running,
    Resolving,
}

/// <summary>The bet/odds/config view of the session's <c>GameConfig</c> a game may read.</summary>
public sealed record GameConfigView(
    long? MinBet,
    long? MaxBet,
    decimal? PayoutMultiplier,
    IReadOnlyDictionary<string, object?>? Config
);

/// <summary>
/// Engine-owned state passed into every game hook. The game reads it and mutates ONLY <see cref="Data"/> —
/// the bag the engine snapshots to <c>GameSession.StateJson</c> on each transition (crash recovery + the
/// overlay frame source).
/// </summary>
public sealed class LiveGameState
{
    public required Guid SessionId { get; init; }
    public required Guid BroadcasterId { get; init; }
    public required GameConfigView Config { get; init; }
    public required IReadOnlyList<LiveGameParticipant> Participants { get; init; }
    public required LiveGamePhase Phase { get; init; }
    public required IDictionary<string, object?> Data { get; init; }
    public required IGameRandom Random { get; init; }
}

/// <summary>Engine-provided randomness — CSPRNG in production, a seeded fake in tests (fair odds by construction).</summary>
public interface IGameRandom
{
    int Next(int maxExclusive);
    double NextDouble();
    bool Roll(double percent);
}

public sealed record LiveGameParticipant(
    Guid UserId,
    Guid AccountId,
    string DisplayName,
    long Stake
);

/// <summary>One matched chat input: the participant, the keyword that matched, and the remaining tokens.</summary>
public sealed record LiveGameInput(
    LiveGameParticipant Player,
    string Keyword,
    IReadOnlyList<string> Args,
    string RawMessage
);

/// <summary>A hook's outcome: optionally push an overlay frame, optionally trigger resolution.</summary>
public sealed record LiveGameTransition(
    bool PushOverlay,
    object? OverlayPayload = null,
    bool Resolve = false
)
{
    public static LiveGameTransition Continue() => new(false);

    public static LiveGameTransition Push(object payload) => new(true, payload);

    public static LiveGameTransition GoResolve(object? payload = null) =>
        new(payload is not null, payload, true);

    /// <summary>Input rejected — not a valid play; nothing changes.</summary>
    public static LiveGameTransition Ignore() => new(false);
}

/// <summary>Who won what. The engine credits payouts, appends the <c>GamePlay</c> rows, and pushes the final frame.</summary>
public sealed record LiveGameResolution(
    IReadOnlyList<LiveGameAward> Awards,
    object? FinalOverlayPayload
);

public sealed record LiveGameAward(
    Guid UserId,
    Guid AccountId,
    long Stake,
    GameOutcome Outcome,
    long Payout
);
