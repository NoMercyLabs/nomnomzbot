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

namespace NomNomzBot.Api.Hubs.Dtos;

/// <summary>
/// A TTS utterance was spoken and recorded. The same shape as one row of GET tts/history (the audio is never
/// sent), so the history list prepends it with no refetch.
/// </summary>
public record TtsUtteranceSpokenAlertDto(
    int Id,
    string Text,
    string VoiceId,
    string Provider,
    string RequestedByUserId,
    DateTime OccurredAt
);
