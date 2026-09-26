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
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Enabled widgets whose NEWEST version failed to build (<see cref="WidgetVersion.BuildStatus"/> =
/// <c>error</c>): the streamer's latest edit is not on stream. Critical when the widget has no working version
/// at all (the overlay serves nothing), a warning when an older build is still being served. The key is the
/// failed version's id, so the next failed save surfaces again after a dismissal.
/// </summary>
public sealed class WidgetBuildFailureSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string KeyPrefix = "widget-build:";
    private const string FailedBuild = "error";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [
        nameof(WidgetBuildFailedEvent),
        nameof(WidgetBuildSucceededEvent),
        nameof(ChannelConfigChangedEvent),
    ];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<Widget> widgets = await db
            .Widgets.IgnoreQueryFilters()
            .Where(w => w.BroadcasterId == channelId && w.DeletedAt == null && w.IsEnabled)
            .ToListAsync(cancellationToken);
        if (widgets.Count == 0)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        List<Guid> widgetIds = [.. widgets.Select(w => w.Id)];
        List<VersionHead> versions = await db
            .WidgetVersions.IgnoreQueryFilters()
            .Where(v => v.BroadcasterId == channelId && widgetIds.Contains(v.WidgetId))
            .Select(v => new VersionHead(
                v.Id,
                v.WidgetId,
                v.VersionNumber,
                v.BuildStatus,
                v.CreatedAt
            ))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, VersionHead> newestByWidget = versions
            .GroupBy(v => v.WidgetId)
            .ToDictionary(g => g.Key, g => g.MaxBy(v => v.VersionNumber)!);

        List<ActionRequiredItemDto> items = [];
        foreach (Widget widget in widgets)
        {
            if (
                !newestByWidget.TryGetValue(widget.Id, out VersionHead? newest)
                || newest.BuildStatus != FailedBuild
            )
                continue;

            string key = $"{KeyPrefix}{newest.Id}";
            if (!dismissedKeys.Contains(key))
                items.Add(ToItem(key, widget, newest));
        }

        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static ActionRequiredItemDto ToItem(string key, Widget widget, VersionHead failed) =>
        new(
            Id: key,
            Kind: "widget_build_failed",
            Severity: widget.ActiveVersionId is null ? "critical" : "warning",
            TitleKey: "attention_widget_build_failed_title",
            MessageKey: widget.ActiveVersionId is null
                ? "attention_widget_build_failed_nothing_live_message"
                : "attention_widget_build_failed_message",
            Parameters: new()
            {
                ["widgetName"] = widget.Name,
                ["version"] = failed.VersionNumber.ToString(),
            },
            DetectedAt: failed.CreatedAt,
            DeepLinkRoute: "widgets",
            SourceUserId: null,
            SourceUserName: null,
            Count: 1,
            QueueItemIds: []
        );

    private sealed record VersionHead(
        Guid Id,
        Guid WidgetId,
        int VersionNumber,
        string BuildStatus,
        DateTime CreatedAt
    );
}
