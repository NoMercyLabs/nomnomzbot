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
/// Pulls each registered provider's live voice list (Edge from its static set, Azure/ElevenLabs when a key is
/// configured) into the <c>TtsVoice</c> catalogue (tts.md §7, decision 7). Runs on startup after seeding and is
/// re-runnable for an operator-triggered refresh. UPSERTS by the voice's natural id — updating the rich metadata
/// (accent/age/styles/tags/description/preview url) on an existing row, inserting a new row otherwise — and never
/// deletes, so a provider that returns nothing (no key) leaves the seeded Edge catalogue untouched.
/// </summary>
public interface ITtsVoiceCatalogSync
{
    Task SyncAsync(CancellationToken cancellationToken = default);
}
