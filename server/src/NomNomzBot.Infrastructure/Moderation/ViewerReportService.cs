// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Viewer reports + moderator triage (moderation.md J.8). Backed by the <see cref="ViewerReport"/> table: a viewer
/// files a report (the reported chatter is get-or-created as a user), moderators list the open queue and resolve
/// each. The row is a truthful record; enforcement stays with the normal ban/timeout tools once a report escalates.
/// </summary>
public sealed class ViewerReportService : IViewerReportService
{
    private const int ReasonMaxLength = 500;

    // The queue states a report can be listed by; a bad filter 400s locally instead of returning a doomed query.
    private static readonly HashSet<string> ReportStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "open",
        "dismissed",
        "escalated",
    };

    private readonly IApplicationDbContext _db;
    private readonly IUserService _users;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public ViewerReportService(
        IApplicationDbContext db,
        IUserService users,
        IEventBus eventBus,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _users = users;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ViewerReportDto>> FileReportAsync(
        string broadcasterId,
        FileViewerReportRequest request,
        string? reporterUserId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<ViewerReportDto>(broadcasterId);

        string reason = request.Reason?.Trim() ?? "";
        if (reason.Length == 0)
            return Result.Failure<ViewerReportDto>("A report needs a reason.", "VALIDATION_FAILED");
        if (reason.Length > ReasonMaxLength)
            return Result.Failure<ViewerReportDto>(
                $"A report reason can't exceed {ReasonMaxLength} characters.",
                "VALIDATION_FAILED"
            );
        if (string.IsNullOrWhiteSpace(request.ReportedTwitchUserId))
            return Result.Failure<ViewerReportDto>(
                "A report needs a reported user.",
                "VALIDATION_FAILED"
            );

        bool channelExists = await _db.Channels.AnyAsync(c => c.Id == tenantId, cancellationToken);
        if (!channelExists)
            return Errors.ChannelNotFound<ViewerReportDto>(broadcasterId);

        // Resolve (get-or-create) the reported chatter — viewers ARE users, so a report ties to a real Users row.
        Result<UserDto> reported = await _users.GetOrCreateAsync(
            request.ReportedTwitchUserId,
            request.ReportedUsername,
            string.IsNullOrWhiteSpace(request.ReportedDisplayName)
                ? request.ReportedUsername
                : request.ReportedDisplayName!,
            cancellationToken: cancellationToken
        );
        if (reported.IsFailure)
            return Result.Failure<ViewerReportDto>(reported.ErrorMessage!, reported.ErrorCode!);
        if (!Guid.TryParse(reported.Value.Id, out Guid reportedUserId))
            return Result.Failure<ViewerReportDto>(
                "Could not resolve the reported user.",
                "INTERNAL_ERROR"
            );

        ViewerReport report = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = tenantId,
            ReportedUserId = reportedUserId,
            ReportedTwitchUserId = request.ReportedTwitchUserId,
            ReporterUserId = Guid.TryParse(reporterUserId, out Guid reporterGuid)
                ? reporterGuid
                : null,
            Reason = reason,
            Status = "open",
        };
        _db.ViewerReports.Add(report);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishAsync(tenantId, report.Id, "created", cancellationToken);

        Dictionary<Guid, string> names = await ResolveNamesAsync(
            [report.ReportedUserId, report.ReporterUserId],
            cancellationToken
        );
        return Result.Success(ToDto(report, request.ReportedUsername, names));
    }

    public async Task<Result<List<ViewerReportDto>>> ListReportsAsync(
        string broadcasterId,
        string status,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<List<ViewerReportDto>>(broadcasterId);
        if (!ReportStatuses.Contains(status))
            return Result.Failure<List<ViewerReportDto>>(
                $"Unknown report status '{status}'. Valid: {string.Join(", ", ReportStatuses)}.",
                "VALIDATION_FAILED"
            );

        List<ViewerReport> reports = await _db
            .ViewerReports.Where(r => r.BroadcasterId == tenantId && r.Status == status)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> names = await ResolveNamesAsync(
            reports.SelectMany(r =>
                new Guid?[] { r.ReportedUserId, r.ReporterUserId, r.ResolvedByUserId }
            ),
            cancellationToken
        );

        List<ViewerReportDto> dtos = reports
            .Select(r => ToDto(r, names.GetValueOrDefault(r.ReportedUserId), names))
            .ToList();
        return Result.Success(dtos);
    }

    public async Task<Result<ViewerReportDto>> ResolveReportAsync(
        string broadcasterId,
        Guid reportId,
        string action,
        string? resolverUserId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.NotFound<ViewerReportDto>("Viewer report", reportId.ToString());

        string status = action?.Trim().ToLowerInvariant() switch
        {
            "dismiss" => "dismissed",
            "escalate" => "escalated",
            _ => "",
        };
        if (status.Length == 0)
            return Result.Failure<ViewerReportDto>(
                "Unknown action. Supported: dismiss, escalate.",
                "VALIDATION_FAILED"
            );

        ViewerReport? report = await _db.ViewerReports.FirstOrDefaultAsync(
            r => r.Id == reportId && r.BroadcasterId == tenantId,
            cancellationToken
        );
        if (report is null)
            return Errors.NotFound<ViewerReportDto>("Viewer report", reportId.ToString());

        report.Status = status;
        report.ResolvedByUserId = Guid.TryParse(resolverUserId, out Guid resolverGuid)
            ? resolverGuid
            : null;
        report.ResolvedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);
        await PublishAsync(tenantId, report.Id, "updated", cancellationToken);

        Dictionary<Guid, string> names = await ResolveNamesAsync(
            [report.ReportedUserId, report.ReporterUserId, report.ResolvedByUserId],
            cancellationToken
        );
        return Result.Success(ToDto(report, names.GetValueOrDefault(report.ReportedUserId), names));
    }

    private async Task<Dictionary<Guid, string>> ResolveNamesAsync(
        IEnumerable<Guid?> ids,
        CancellationToken cancellationToken
    )
    {
        List<Guid> distinct = ids.Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (distinct.Count == 0)
            return new();

        return await _db
            .Users.Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    private Task PublishAsync(
        Guid broadcasterId,
        Guid reportId,
        string action,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "viewer-reports",
                EntityId = reportId.ToString(),
                Action = action,
            },
            ct
        );

    private static ViewerReportDto ToDto(
        ViewerReport report,
        string? reportedUsername,
        Dictionary<Guid, string> names
    ) =>
        new(
            report.Id,
            report.ReportedTwitchUserId,
            reportedUsername,
            report.Reason,
            report.Status,
            report.ReporterUserId is Guid reporter ? names.GetValueOrDefault(reporter) : null,
            report.CreatedAt,
            report.ResolvedAt,
            report.ResolvedByUserId is Guid resolver ? names.GetValueOrDefault(resolver) : null
        );
}
