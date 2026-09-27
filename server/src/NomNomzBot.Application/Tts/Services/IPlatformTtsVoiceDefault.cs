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
/// The platform's default TTS voice (plan item A4): the voice every channel that never picked its own speaks
/// with. Runtime-editable by the platform admin, so the channel read model and the dispatcher resolve it here
/// instead of carrying a hardcoded voice id.
/// </summary>
public interface IPlatformTtsVoiceDefault
{
    /// <summary>The catalogue id of the platform default voice; null only when the catalogue marks no voice as default.</summary>
    Task<string?> GetVoiceIdAsync(CancellationToken ct = default);
}
