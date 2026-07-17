// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
