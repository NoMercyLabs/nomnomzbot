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
