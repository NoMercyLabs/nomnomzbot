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
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
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
/// grant row. Pushed as a dashboard alert to the affected tenant only, never platform-wide; a grant with no
/// channel scope (platform-wide grant, not a per-tenant support session) is not this handler's concern and
/// is silently skipped.
/// </summary>
public sealed class ImpersonationStartedBroadcastHandler(
    IDashboardNotifier notifier,
    IApplicationDbContext db
) : IEventHandler<ImpersonationStartedEvent>
{
    public async Task HandleAsync(ImpersonationStartedEvent @event, CancellationToken ct = default)
    {
        var grant = await db
            .IamRoleAssignments.Where(a => a.Id == @event.AccessGrantId)
            .Select(a => new { a.ScopeChannelId, a.Reason })
            .FirstOrDefaultAsync(ct);

        if (grant is null || grant.ScopeChannelId is null || grant.ScopeChannelId == Guid.Empty)
            return;

        await notifier.SendAlertAsync(
            grant.ScopeChannelId.Value.ToString(),
            new AlertDto(
                "impersonation_started",
                "A NomNomzBot operator started acting as a user on your channel.",
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
/// end of session). Pushed as a dashboard alert to the affected tenant only.
/// </summary>
public sealed class ImpersonationEndedBroadcastHandler(
    IDashboardNotifier notifier,
    IApplicationDbContext db
) : IEventHandler<ImpersonationEndedEvent>
{
    public async Task HandleAsync(ImpersonationEndedEvent @event, CancellationToken ct = default)
    {
        var grant = await db
            .IamRoleAssignments.Where(a => a.Id == @event.AccessGrantId)
            .Select(a => new { a.ScopeChannelId, a.Reason })
            .FirstOrDefaultAsync(ct);

        if (grant is null || grant.ScopeChannelId is null || grant.ScopeChannelId == Guid.Empty)
            return;

        await notifier.SendAlertAsync(
            grant.ScopeChannelId.Value.ToString(),
            new AlertDto(
                "impersonation_ended",
                "A NomNomzBot operator stopped acting as a user on your channel.",
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
