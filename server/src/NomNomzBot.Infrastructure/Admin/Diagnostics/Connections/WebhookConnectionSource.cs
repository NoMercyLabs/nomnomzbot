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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics.Connections;

/// <summary>
/// The channel's outbound and inbound webhook endpoints. An outbound endpoint is connected while enabled and not
/// auto-disabled; its disable reason is the last error. Signing secrets, verification secrets and the URL token of
/// an inbound endpoint never leave the row.
/// </summary>
public sealed class WebhookConnectionSource(IApplicationDbContext db) : IChannelConnectionSource
{
    public async Task<IReadOnlyList<ChannelDiagnosticConnectionDto>> ReadAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<ChannelDiagnosticConnectionDto> rows = await ReadOutboundAsync(
            broadcasterId,
            cancellationToken
        );
        rows.AddRange(await ReadInboundAsync(broadcasterId, cancellationToken));
        return rows;
    }

    private async Task<List<ChannelDiagnosticConnectionDto>> ReadOutboundAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<OutboundRow> rows = await db
            .OutboundWebhookEndpoints.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.BroadcasterId == broadcasterId && e.DeletedAt == null)
            .Select(e => new OutboundRow(
                e.Id,
                e.Name,
                e.Fqdn,
                e.IsEnabled,
                e.DisabledAt,
                e.DisabledReason,
                e.ConsecutiveFailureCount,
                e.LastDeliveryAt
            ))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new ChannelDiagnosticConnectionDto
            {
                Kind = ChannelConnectionKinds.Webhook,
                Provider = "webhook_outbound",
                Id = r.Id.ToString(),
                AccountName = r.Name,
                AccountId = r.Fqdn,
                Connected = r is { IsEnabled: true, DisabledAt: null },
                Status = r.IsEnabled ? "enabled" : "disabled",
                LastError = r.DisabledReason,
                LastErrorAt = r.DisabledAt,
                ConsecutiveFailures = r.ConsecutiveFailureCount,
                LastActivityAt = r.LastDeliveryAt,
            }),
        ];
    }

    private async Task<List<ChannelDiagnosticConnectionDto>> ReadInboundAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        List<InboundRow> rows = await db
            .InboundWebhookEndpoints.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.BroadcasterId == broadcasterId && e.DeletedAt == null)
            .Select(e => new InboundRow(e.Id, e.Name, e.AdapterKind, e.IsEnabled, e.LastReceivedAt))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(r => new ChannelDiagnosticConnectionDto
            {
                Kind = ChannelConnectionKinds.Webhook,
                Provider = "webhook_inbound",
                Id = r.Id.ToString(),
                AccountName = r.Name,
                AccountId = r.AdapterKind.ToString(),
                Connected = r.IsEnabled,
                Status = r.IsEnabled ? "enabled" : "disabled",
                LastActivityAt = r.LastReceivedAt,
            }),
        ];
    }

    private sealed record OutboundRow(
        Guid Id,
        string Name,
        string Fqdn,
        bool IsEnabled,
        DateTime? DisabledAt,
        string? DisabledReason,
        int ConsecutiveFailureCount,
        DateTime? LastDeliveryAt
    );

    private sealed record InboundRow(
        Guid Id,
        string Name,
        WebhookAdapterKind AdapterKind,
        bool IsEnabled,
        DateTime? LastReceivedAt
    );
}
