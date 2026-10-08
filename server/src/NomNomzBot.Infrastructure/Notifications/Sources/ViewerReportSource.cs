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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Platform.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Viewer reports still awaiting moderator triage (<c>Status = "open"</c>), one item per report. A report leaves
/// the inbox when a moderator dismisses or escalates it, since the item is derived from the row's status. The
/// report service signals every change through <see cref="ChannelConfigChangedEvent"/>, so that is the event
/// that refreshes this source live.
/// </summary>
public sealed class ViewerReportSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string KeyPrefix = "report:";
    private const string OpenStatus = "open";

    public string SourceKey => "viewer_reports";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [nameof(ChannelConfigChangedEvent)];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<ViewerReport> open = await db
            .ViewerReports.Where(r => r.BroadcasterId == channelId && r.Status == OpenStatus)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        List<ViewerReport> visible = [.. open.Where(r => !dismissedKeys.Contains(KeyOf(r.Id)))];
        if (visible.Count == 0)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        List<Guid> reportedIds = [.. visible.Select(r => r.ReportedUserId).Distinct()];
        Dictionary<Guid, string> names = await db
            .Users.Where(u => reportedIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        List<ActionRequiredItemDto> items =
        [
            .. visible.Select(r => ToItem(r, names.GetValueOrDefault(r.ReportedUserId))),
        ];
        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static ActionRequiredItemDto ToItem(ViewerReport report, string? displayName)
    {
        string username = displayName ?? report.ReportedTwitchUserId;
        return new ActionRequiredItemDto(
            Id: KeyOf(report.Id),
            Kind: "viewer_report",
            Severity: "warning",
            TitleKey: "attention_report_title",
            MessageKey: "attention_report_message",
            Parameters: new() { ["username"] = username, ["reason"] = report.Reason },
            DetectedAt: report.CreatedAt,
            DeepLinkRoute: "moderationqueue",
            SourceUserId: report.ReportedTwitchUserId,
            SourceUserName: username,
            Count: 1,
            QueueItemIds: []
        );
    }

    private static string KeyOf(Guid reportId) => $"{KeyPrefix}{reportId}";
}
