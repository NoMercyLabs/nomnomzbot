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

namespace NomNomzBot.Application.Dashboard.Dtos;

/// <summary>
/// What one activity-feed replay actually re-performed. Every count is real work done — zero is a truthful
/// outcome (a disabled response, a widget since removed), never dressed up as success.
/// </summary>
/// <param name="EventsReplayed">Events in the replayed chain: 1, or 1 + each named recipient of a gift bomb.</param>
/// <param name="ChatMessagesSent">Chat lines the bot sent through its normal send path.</param>
/// <param name="TtsQueued">TTS utterances the replayed responses queued.</param>
/// <param name="OverlaysShown">Overlay-type event responses pushed to the overlay.</param>
/// <param name="WidgetsNotified">Widget pushes of the captured alert payloads.</param>
public sealed record ActivityReplayResult(
    int EventsReplayed,
    int ChatMessagesSent,
    int TtsQueued,
    int OverlaysShown,
    int WidgetsNotified
);
