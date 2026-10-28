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
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// Viewer-filed reports and moderator triage (moderation.md J.8). A viewer files a report about a chatter; a
/// moderator lists the open queue and resolves each — <c>dismiss</c> or <c>escalate</c>. The report is a truthful
/// record; enforcement (ban/timeout) stays with the normal moderation tools once a report is escalated.
/// </summary>
public interface IViewerReportService
{
    /// <summary>File a report about a chatter. The reported user is get-or-created (viewers ARE users).</summary>
    Task<Result<ViewerReportDto>> FileReportAsync(
        string broadcasterId,
        FileViewerReportRequest request,
        string? reporterUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>List the channel's reports filtered by <paramref name="status"/> (open / dismissed / escalated).</summary>
    Task<Result<List<ViewerReportDto>>> ListReportsAsync(
        string broadcasterId,
        string status,
        CancellationToken cancellationToken = default
    );

    /// <summary>Resolve a report — <c>dismiss</c> or <c>escalate</c> — recording the acting moderator.</summary>
    Task<Result<ViewerReportDto>> ResolveReportAsync(
        string broadcasterId,
        Guid reportId,
        string action,
        string? resolverUserId,
        CancellationToken cancellationToken = default
    );
}
