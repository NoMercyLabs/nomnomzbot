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

using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>Self-service pronoun read/write for the authenticated viewer (spec D5).</summary>
public interface IPronounSelfService
{
    /// <summary>Return the current pronoun state for <paramref name="userId"/>.</summary>
    Task<UserPronounDto?> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Update pronouns for <paramref name="userId"/>. Setting <see cref="SetPronounRequest.ManualOverride"/>
    /// to <c>true</c> locks the choice and prevents the resolution service from overwriting it.
    /// Clearing both PronounId and AltPronounId with ManualOverride false returns to automatic resolution.
    /// </summary>
    Task<UserPronounDto?> SetAsync(
        Guid userId,
        SetPronounRequest request,
        CancellationToken ct = default
    );
}
