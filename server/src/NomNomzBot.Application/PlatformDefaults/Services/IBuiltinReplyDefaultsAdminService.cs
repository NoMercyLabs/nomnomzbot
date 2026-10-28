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
