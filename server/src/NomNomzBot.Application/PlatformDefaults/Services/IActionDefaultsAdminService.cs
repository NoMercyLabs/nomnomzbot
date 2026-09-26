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
/// The platform admin's runtime editor for Gate-2 action defaults (plan item A4): replaces an action's shipped
/// default level for every channel that has no override of its own, without a redeploy. Never below the
/// action's floor; a dangerous (Critical/ToS) action needs an explicit confirmation; every change is audited
/// and read back.
/// </summary>
public interface IActionDefaultsAdminService
{
    /// <summary>Every gateable action with its shipped, platform and effective default.</summary>
    Task<Result<IReadOnlyList<ActionDefaultDto>>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// The counted blast radius of moving <paramref name="actionKey"/>'s default to <paramref name="level"/>
    /// (null = back to the shipped default): the channels without an override that would feel it.
    /// </summary>
    Task<Result<PlatformDefaultBlastRadiusDto>> PreviewAsync(
        string actionKey,
        int? level,
        CancellationToken ct = default
    );

    /// <summary>Applies the previewed change and returns the row read back from the database.</summary>
    Task<Result<ActionDefaultDto>> SetAsync(
        string actionKey,
        SetActionDefaultRequest request,
        Guid actorUserId,
        CancellationToken ct = default
    );
}
