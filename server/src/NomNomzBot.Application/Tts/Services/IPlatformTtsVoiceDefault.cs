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
/// The platform's default TTS voice (plan item A4): the voice every channel that never picked its own speaks
/// with. Runtime-editable by the platform admin, so the channel read model and the dispatcher resolve it here
/// instead of carrying a hardcoded voice id.
/// </summary>
public interface IPlatformTtsVoiceDefault
{
    /// <summary>
    /// The catalogue id of the platform default voice; the built-in fallback voice when the catalogue marks none,
    /// so the result is always speakable.
    /// </summary>
    Task<string> GetVoiceIdAsync(CancellationToken ct = default);
}
