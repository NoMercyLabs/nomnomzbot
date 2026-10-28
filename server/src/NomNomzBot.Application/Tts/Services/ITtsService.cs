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

using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Application.Tts.Services;

public interface ITtsService
{
    /// <summary>
    /// Synthesizes <paramref name="text"/> as <paramref name="voiceId"/>. <paramref name="ratePercent"/> and
    /// <paramref name="pitchPercent"/> are optional per-call SSML prosody overrides (percent, matching
    /// <c>rate='+N%'</c>/<c>pitch='+N%'</c>) — <c>null</c> means the provider's default, i.e. today's
    /// unchanged behavior. Never persisted; a one-off override for a single call.
    /// </summary>
    Task<TtsResult> SynthesizeAsync(
        string text,
        string voiceId,
        double? ratePercent = null,
        double? pitchPercent = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Synthesizes an ordered list of <paramref name="segments"/> (own voice, prosody and trailing silence each)
    /// as ONE audio clip, on the provider that owns the first segment's voice.
    /// </summary>
    Task<TtsResult> SynthesizeSegmentsAsync(
        IReadOnlyList<TtsSegment> segments,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<TtsVoiceInfo>> GetAvailableVoicesAsync(CancellationToken ct = default);
}

public record TtsResult(byte[] AudioData, int DurationMs, string VoiceId, string Provider);
