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

using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Tells the tenant owner an operator gained support access to THEIR channel (S086f — the grant used to
/// happen silently; S-IMPERSONATION-NOTICE — the same class of gap, durable half added so the owner can
/// see it after the fact, not only while their dashboard happened to be open). Pushed as a dashboard alert
/// to the affected tenant only, never platform-wide; also persisted as a durable
/// <see cref="Domain.Identity.Entities.SecurityNotice"/>.
/// </summary>
public sealed class TenantAccessGrantedBroadcastHandler(
    IDashboardNotifier notifier,
    ISecurityNoticeService securityNotices
) : IEventHandler<TenantAccessGrantedEvent>
{
    public async Task HandleAsync(TenantAccessGrantedEvent @event, CancellationToken ct = default)
    {
        string summary = @event.BreakGlass
            ? "A NomNomzBot operator used break-glass access on your channel."
            : "A NomNomzBot operator was granted temporary support access to your channel.";

        await securityNotices.RecordAsync(
            new RecordSecurityNoticeRequest(
                @event.TargetBroadcasterId,
                "tenant_access_granted",
                summary,
                @event.PrincipalId,
                null,
                @event.AccessGrantId,
                null,
                @event.BreakGlass ? "break_glass" : "support_grant",
                @event.ExpiresAt
            ),
            ct
        );

        await notifier.SendAlertAsync(
            @event.TargetBroadcasterId.ToString(),
            new(
                "tenant_access_granted",
                summary,
                new
                {
                    @event.PrincipalId,
                    @event.AccessGrantId,
                    @event.BreakGlass,
                    @event.ExpiresAt,
                }
            ),
            ct
        );
    }
}
