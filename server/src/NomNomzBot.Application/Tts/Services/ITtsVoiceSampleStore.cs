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

namespace NomNomzBot.Application.Tts.Services;

/// <summary>
/// The pre-made introduction clip of each voice. A voice introduces itself the same way for every channel, so the
/// clip is made once and played from storage, and pressing play on a voice never waits for a provider.
/// </summary>
public interface ITtsVoiceSampleStore
{
    /// <summary>The stored introduction clip of the voice, or <c>null</c> when none was made yet.</summary>
    Task<TtsResult?> GetAsync(string voiceId, CancellationToken cancellationToken = default);

    /// <summary>Stores the introduction clip of the voice. A clip stored earlier is replaced.</summary>
    Task SaveAsync(string voiceId, TtsResult clip, CancellationToken cancellationToken = default);
}
