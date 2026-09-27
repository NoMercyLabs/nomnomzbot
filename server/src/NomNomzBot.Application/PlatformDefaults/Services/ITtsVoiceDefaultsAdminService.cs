// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PlatformDefaults.Dtos;

namespace NomNomzBot.Application.PlatformDefaults.Services;

/// <summary>
/// The platform admin's runtime editor for the default TTS voice (plan item A4): the voice every channel that
/// never picked its own speaks with. A channel's own pick keeps winning. Every change is previewed with its
/// counted blast radius, audited, and read back.
/// </summary>
public interface ITtsVoiceDefaultsAdminService
{
    Task<Result<TtsVoiceDefaultDto>> GetAsync(CancellationToken ct = default);

    /// <summary>The counted blast radius of making <paramref name="change"/> the platform default.</summary>
    Task<Result<PlatformDefaultBlastRadiusDto>> PreviewAsync(
        TtsVoiceDefaultChange change,
        CancellationToken ct = default
    );

    /// <summary>Applies the previewed change and returns the default read back from the database.</summary>
    Task<Result<TtsVoiceDefaultDto>> SetAsync(
        SetTtsVoiceDefaultRequest request,
        Guid actorUserId,
        CancellationToken ct = default
    );
}
