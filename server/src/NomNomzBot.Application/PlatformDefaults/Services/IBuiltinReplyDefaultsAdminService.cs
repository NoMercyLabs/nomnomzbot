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
/// The platform admin's runtime editor for built-in reply texts (plan item A4): the wording of one built-in
/// response slot for every channel, replacing the shipped tone lines. A channel's own response override keeps
/// winning on the slots that take one. Every change is previewed with its counted blast radius, audited and
/// read back.
/// </summary>
public interface IBuiltinReplyDefaultsAdminService
{
    Task<Result<IReadOnlyList<BuiltinReplyDefaultDto>>> ListAsync(CancellationToken ct = default);

    Task<Result<PlatformDefaultBlastRadiusDto>> PreviewAsync(
        string builtinKey,
        string slot,
        BuiltinReplyDefaultChange change,
        CancellationToken ct = default
    );

    Task<Result<BuiltinReplyDefaultDto>> SetAsync(
        string builtinKey,
        string slot,
        SetBuiltinReplyDefaultRequest request,
        Guid actorUserId,
        CancellationToken ct = default
    );
}
