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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Tells the affected tenant owner that a NomNomzBot operator began act-as impersonation of one of their
/// users under an open support session (S089d — <see cref="ImpersonationStartedEvent"/> used to have zero
/// consumers, same class of gap S086f closed for <c>TenantAccessGrantedEvent</c>). The impersonation
/// session rides on an <c>IamRoleAssignment</c> (<see cref="ImpersonationStartedEvent.AccessGrantId"/>)
/// whose <c>ScopeChannelId</c> names the affected tenant and whose <c>Reason</c> carries the support
/// session's justification — neither field is on the event itself, so the handler resolves both from the
/// grant row via <see cref="ImpersonationGrantLookup"/>.
/// </summary>
public sealed class ImpersonationStartedBroadcastHandler(
    IDashboardNotifier notifier,
    ISecurityNoticeService securityNotices,
    IApplicationDbContext db,
    ILogger<ImpersonationStartedBroadcastHandler> logger
) : IEventHandler<ImpersonationStartedEvent>
{
    public async Task HandleAsync(ImpersonationStartedEvent @event, CancellationToken ct = default)
    {
        ImpersonationGrantLookup.Grant? grant = await ImpersonationGrantLookup.ResolveAsync(
            db,
            @event.AccessGrantId,
            logger,
            ct
        );

        if (grant is null)
            return;

        const string summary = "A NomNomzBot operator started acting as a user on your channel.";

        // Durable half first: this is the one that must survive the owner being offline for the whole
        // session, so it must not depend on the transient SignalR push below ever landing.
        await securityNotices.RecordAsync(
            new RecordSecurityNoticeRequest(
                grant.ScopeChannelId,
                "impersonation_started",
                summary,
                @event.OperatorPrincipalId,
                @event.TargetUserId,
                @event.AccessGrantId,
                grant.Reason,
                "channel",
                @event.ExpiresAt
            ),
            ct
        );

        await notifier.SendAlertAsync(
            grant.ScopeChannelId.ToString(),
            new(
                "impersonation_started",
                summary,
                new
                {
                    @event.OperatorPrincipalId,
                    @event.TargetUserId,
                    @event.AccessGrantId,
                    @event.ExpiresAt,
                    grant.Reason,
                }
            ),
            ct
        );
    }
}

/// <summary>
/// Tells the affected tenant owner that the operator's act-as impersonation session ended
/// (<see cref="ImpersonationStartedBroadcastHandler"/> — same event/grant relationship, mirrored for the
/// end of session). Ending impersonation is precisely when the backing grant gets revoked, so the lookup
/// reads with <see cref="ImpersonationGrantLookup.ResolveAsync"/> which bypasses global query filters —
/// a revoked (or otherwise filtered) grant row must still resolve, since a notification that silently
/// disappears exactly when it matters is worse than none: the audit trail would otherwise imply the owner
/// was told when they were not.
/// </summary>
public sealed class ImpersonationEndedBroadcastHandler(
    IDashboardNotifier notifier,
    ISecurityNoticeService securityNotices,
    IApplicationDbContext db,
    ILogger<ImpersonationEndedBroadcastHandler> logger
) : IEventHandler<ImpersonationEndedEvent>
{
    public async Task HandleAsync(ImpersonationEndedEvent @event, CancellationToken ct = default)
    {
        ImpersonationGrantLookup.Grant? grant = await ImpersonationGrantLookup.ResolveAsync(
            db,
            @event.AccessGrantId,
            logger,
            ct
        );

        if (grant is null)
            return;

        const string summary = "A NomNomzBot operator stopped acting as a user on your channel.";

        await securityNotices.RecordAsync(
            new RecordSecurityNoticeRequest(
                grant.ScopeChannelId,
                "impersonation_ended",
                summary,
                @event.OperatorPrincipalId,
                @event.TargetUserId,
                @event.AccessGrantId,
                grant.Reason,
                "channel",
                null
            ),
            ct
        );

        await notifier.SendAlertAsync(
            grant.ScopeChannelId.ToString(),
            new(
                "impersonation_ended",
                summary,
                new
                {
                    @event.OperatorPrincipalId,
                    @event.TargetUserId,
                    @event.AccessGrantId,
                    grant.Reason,
                }
            ),
            ct
        );
    }
}

/// <summary>
/// Shared grant resolution for both impersonation broadcast handlers — neither
/// <see cref="ImpersonationStartedEvent"/> nor <see cref="ImpersonationEndedEvent"/> carries the affected
/// tenant or the support session's reason directly, so both are read off the backing
/// <see cref="IamRoleAssignment"/> by <c>AccessGrantId</c>. <c>IgnoreQueryFilters()</c>
/// is used deliberately: the grant row for an ENDED session has typically just been revoked (and could in
/// future gain a soft-delete or tenant-scope filter), and the whole point of this lookup is the owner
/// notification must not depend on the row still being "live" by whatever filter the model applies.
/// </summary>
internal static class ImpersonationGrantLookup
{
    public sealed record Grant(Guid ScopeChannelId, string? Reason);

    public static async Task<Grant?> ResolveAsync(
        IApplicationDbContext db,
        Guid accessGrantId,
        ILogger logger,
        CancellationToken ct
    )
    {
        var row = await db
            .IamRoleAssignments.IgnoreQueryFilters()
            .Where(a => a.Id == accessGrantId)
            .Select(a => new { a.ScopeChannelId, a.Reason })
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            logger.LogWarning(
                "Impersonation broadcast: no IamRoleAssignment found for AccessGrantId {AccessGrantId} — "
                    + "the tenant owner was NOT notified.",
                accessGrantId
            );
            return null;
        }

        if (row.ScopeChannelId is null || row.ScopeChannelId == Guid.Empty)
            return null;

        return new(row.ScopeChannelId.Value, row.Reason);
    }
}
