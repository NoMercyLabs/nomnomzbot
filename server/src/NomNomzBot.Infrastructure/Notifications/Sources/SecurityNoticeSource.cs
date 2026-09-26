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

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Unread security notices: a NomNomzBot operator opened support access to the channel, or started or stopped
/// acting as one of its people. The owner must see these even when they were offline while it happened, so every
/// notice the owner has not acknowledged or dismissed stays in the inbox. The key is the notice id.
/// </summary>
public sealed class SecurityNoticeSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string KeyPrefix = "security-notice:";

    private static readonly Dictionary<string, NoticeCopy> CopyByType = new(StringComparer.Ordinal)
    {
        ["impersonation_started"] = new(
            "warning",
            "attention_security_impersonation_started_title",
            "attention_security_impersonation_started_message"
        ),
        ["impersonation_ended"] = new(
            "info",
            "attention_security_impersonation_ended_title",
            "attention_security_impersonation_ended_message"
        ),
        ["tenant_access_granted"] = new(
            "warning",
            "attention_security_access_granted_title",
            "attention_security_access_granted_message"
        ),
    };

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    // The notices are written by the impersonation/access broadcast handlers, which push the inbox change
    // themselves through SecurityNoticeService; the causing events carry no channel, so none is listed here.
    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<string> types = [.. CopyByType.Keys];
        List<NoticeRow> notices = await db
            .SecurityNotices.IgnoreQueryFilters()
            .Where(n =>
                n.BroadcasterId == channelId
                && n.DeletedAt == null
                && n.AcknowledgedAt == null
                && types.Contains(n.NoticeType)
            )
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoticeRow(
                n.Id,
                n.NoticeType,
                n.ActorPrincipalId,
                n.TargetUserId,
                n.Reason,
                n.CreatedAt
            ))
            .ToListAsync(cancellationToken);
        notices = [.. notices.Where(n => !dismissedKeys.Contains(KeyFor(n.Id)))];
        if (notices.Count == 0)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        List<Guid> actorIds =
        [
            .. notices
                .Where(n => n.ActorPrincipalId is not null)
                .Select(n => n.ActorPrincipalId!.Value),
        ];
        List<Guid> targetIds =
        [
            .. notices.Where(n => n.TargetUserId is not null).Select(n => n.TargetUserId!.Value),
        ];
        Dictionary<Guid, string> operatorNames = await db
            .IamPrincipals.IgnoreQueryFilters()
            .Where(p => actorIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        Dictionary<Guid, string> targetNames = await db
            .Users.IgnoreQueryFilters()
            .Where(u => targetIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        List<ActionRequiredItemDto> items =
        [
            .. notices.Select(n => ToItem(n, operatorNames, targetNames)),
        ];
        return Result.Success(items);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static string KeyFor(Guid noticeId) => $"{KeyPrefix}{noticeId}";

    private static ActionRequiredItemDto ToItem(
        NoticeRow notice,
        Dictionary<Guid, string> operatorNames,
        Dictionary<Guid, string> targetNames
    )
    {
        NoticeCopy copy = CopyByType[notice.NoticeType];
        string operatorName =
            notice.ActorPrincipalId is Guid actor
            && operatorNames.TryGetValue(actor, out string? name)
                ? name
                : "";
        string targetName =
            notice.TargetUserId is Guid target
            && targetNames.TryGetValue(target, out string? display)
                ? display
                : "";

        return new ActionRequiredItemDto(
            Id: KeyFor(notice.Id),
            Kind: notice.NoticeType,
            Severity: copy.Severity,
            TitleKey: copy.TitleKey,
            MessageKey: copy.MessageKey,
            Parameters: new()
            {
                ["operatorName"] = operatorName,
                ["targetName"] = targetName,
                ["reason"] = notice.Reason ?? "",
            },
            DetectedAt: notice.CreatedAt,
            // Who can act on the channel lives on the Roles page — the place an owner reviews access.
            DeepLinkRoute: "roles",
            SourceUserId: notice.TargetUserId?.ToString(),
            SourceUserName: targetName.Length == 0 ? null : targetName,
            Count: 1,
            QueueItemIds: []
        );
    }

    private sealed record NoticeCopy(string Severity, string TitleKey, string MessageKey);

    private sealed record NoticeRow(
        Guid Id,
        string NoticeType,
        Guid? ActorPrincipalId,
        Guid? TargetUserId,
        string? Reason,
        DateTime CreatedAt
    );
}
