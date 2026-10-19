// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
