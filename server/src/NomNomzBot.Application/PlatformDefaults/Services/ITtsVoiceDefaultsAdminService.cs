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

    /// <summary>The voices the default may be set to, ordered by locale then display name.</summary>
    Task<Result<IReadOnlyList<TtsVoiceCandidateDto>>> CandidatesAsync(
        CancellationToken ct = default
    );

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
