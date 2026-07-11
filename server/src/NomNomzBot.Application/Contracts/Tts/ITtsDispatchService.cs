// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Tts;

/// <summary>
/// The TTS utterance orchestrator (tts.md §3.4): gate → (censor/queue — later slices) → synthesize → dispatch →
/// ledger. This slice implements the self-host dispatch leg — a request that passes the enabled + character-cap
/// gate is synthesized and pushed to the overlay to play, and a usage-ledger row is appended. The approval-queue
/// review methods (Approve/Reject/GetPendingQueue), the profanity censor, BYOK, and the client-edge mode land in
/// follow-on slices and EXTEND this interface then.
/// </summary>
public interface ITtsDispatchService
{
    /// <summary>
    /// Full utterance flow for one TTS request (self-host leg). Loads the channel's TTS config; a disabled channel,
    /// an over-cap or empty text, or a channel with no resolvable voice is rejected (a <c>TtsUtteranceRejectedEvent</c>
    /// fires and the result is a failure — nothing synthesized or charged). Otherwise it resolves the voice
    /// (per-viewer → channel default → first available), synthesizes the audio, stores it, pushes it to the overlay
    /// to play, appends a <c>TtsUsageRecord</c>, emits <c>TtsUtteranceDispatchedEvent</c>, and returns
    /// <see cref="TtsDispatchDisposition.Dispatched"/>.
    /// </summary>
    Task<Result<TtsDispatchOutcome>> RequestSpeakAsync(
        TtsSpeakRequest request,
        CancellationToken ct = default
    );
}

/// <summary>One TTS utterance request (tts.md §3.4). The caller resolves the requester + community standing.</summary>
public sealed record TtsSpeakRequest(
    Guid BroadcasterId,
    Guid RequestedByUserId,
    string RequestedByTwitchUserId,
    string RequestedByDisplayName,
    string Text,
    string? VoiceIdOverride,
    int BitsAmount,
    string CommunityStanding,
    string? SourceMessageId,
    Guid? StreamId
);

public enum TtsDispatchDisposition
{
    Dispatched,
    Queued,
}

/// <summary>The result of a dispatch: what happened, plus the play URL when it was dispatched.</summary>
public sealed record TtsDispatchOutcome(
    TtsDispatchDisposition Disposition,
    string VoiceId,
    string Provider,
    int CharacterCount,
    int DurationMs,
    string? PlaybackUrl
);
