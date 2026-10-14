// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Viewer;

/// <summary>One of the caller's own chat lines. <c>DeletedByModerator</c> is true when a moderator removed it.</summary>
public sealed record ViewerMessageDto(
    string Id,
    DateTime SentAt,
    string Text,
    bool DeletedByModerator
);

/// <summary>
/// A command the caller may run. <c>CooldownSeconds</c> is null when none is stored; <c>LastUsedAt</c> is the
/// caller's own newest run of it, null if never.
/// </summary>
public sealed record ViewerCommandDto(
    string Name,
    string? Description,
    int? CooldownSeconds,
    DateTime? LastUsedAt
);
