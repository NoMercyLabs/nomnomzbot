// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>One viewer report in the moderation queue — who was reported, why, who reported them, and its state.</summary>
public sealed record ViewerReportDto(
    Guid Id,
    string ReportedTwitchUserId,
    string? ReportedUsername,
    string Reason,
    string Status,
    string? ReporterName,
    DateTime CreatedAt,
    DateTime? ResolvedAt,
    string? ResolvedByName
);

/// <summary>A viewer files a report about a chatter they saw (id + name come from the chat context).</summary>
public sealed record FileViewerReportRequest
{
    public required string ReportedTwitchUserId { get; init; }
    public required string ReportedUsername { get; init; }
    public string? ReportedDisplayName { get; init; }
    public required string Reason { get; init; }
}

/// <summary>A moderator resolves a report — <c>dismiss</c> (no action) or <c>escalate</c> (they then act via the ban/timeout tools).</summary>
public sealed record ResolveViewerReportRequest
{
    public required string Action { get; init; }
}
