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

    /// <summary>
    /// A 64-bit draw, the per-drop seed. The default joins two <see cref="NextDouble"/> draws so a scripted
    /// test source still yields a repeatable seed; the production adapter overrides it with a full CSPRNG draw.
    /// </summary>
    ulong NextUInt64()
    {
        ulong high = (ulong)(NextDouble() * uint.MaxValue);
        ulong low = (ulong)(NextDouble() * uint.MaxValue);
        return (high << 32) | low;
    }
}

public sealed record LiveGameParticipant(
    Guid UserId,
    Guid AccountId,
    string DisplayName,
    long Stake
);

/// <summary>
/// One matched chat input: the participant, the keyword that matched, the remaining tokens, and the pictures the
/// message carries (null when the input came from somewhere without them).
/// </summary>
public sealed record LiveGameInput(
    LiveGameParticipant Player,
    string Keyword,
    IReadOnlyList<string> Args,
    string RawMessage,
    LiveGameInputMedia? Media = null
);

/// <summary>
/// The pictures a chat input can point at: the sender's own profile image and each platform emote in the
/// message, with the image link the platform gave for it. A game picks from these; a viewer never sends a link.
/// </summary>
public sealed record LiveGameInputMedia(
    string? AvatarUrl,
    IReadOnlyList<LiveGameInputEmote> Emotes
);

/// <summary>One emote of the message: the text that names it in chat, and its image link.</summary>
public sealed record LiveGameInputEmote(string Code, string ImageUrl);

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
