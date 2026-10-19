// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Tts.Dtos;

/// <summary>
/// One spoken utterance in the channel's TTS history. <paramref name="Text"/> is null for rows written before the
/// text was stored. <paramref name="Id"/> is the row's cursor value.
/// </summary>
public sealed record TtsHistoryEntryDto(
    int Id,
    string? Text,
    string VoiceId,
    string Provider,
    string RequestedByUserId,
    DateTime OccurredAt
);

/// <summary>
/// A newest-first page of the TTS history. <paramref name="NextBefore"/> is the <c>before</c> value that fetches
/// the next older page, or null when this page reaches the oldest row.
/// </summary>
public sealed record TtsHistoryPageDto(IReadOnlyList<TtsHistoryEntryDto> Items, int? NextBefore);
