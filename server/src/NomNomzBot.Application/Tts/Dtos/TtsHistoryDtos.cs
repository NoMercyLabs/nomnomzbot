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

namespace NomNomzBot.Application.Tts.Dtos;

/// <summary>
/// One spoken utterance in the channel's TTS history. <paramref name="Text"/> is null for rows written before the
/// text was stored. <paramref name="Id"/> is the row's cursor value. <paramref name="RequestedByName"/> and
/// <paramref name="RequestedByColor"/> come from the requester's user row, and are null when the bot has none.
/// </summary>
public sealed record TtsHistoryEntryDto(
    int Id,
    string? Text,
    string VoiceId,
    string Provider,
    string RequestedByUserId,
    DateTime OccurredAt,
    string? RequestedByName,
    string? RequestedByColor
);

/// <summary>
/// A newest-first page of the TTS history. <paramref name="NextBefore"/> is the <c>before</c> value that fetches
/// the next older page, or null when this page reaches the oldest row.
/// </summary>
public sealed record TtsHistoryPageDto(IReadOnlyList<TtsHistoryEntryDto> Items, int? NextBefore);
