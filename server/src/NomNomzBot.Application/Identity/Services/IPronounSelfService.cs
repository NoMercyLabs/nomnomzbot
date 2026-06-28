// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
