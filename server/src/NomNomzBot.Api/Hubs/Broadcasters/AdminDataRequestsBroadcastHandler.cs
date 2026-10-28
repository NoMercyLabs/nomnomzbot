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

using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Data request changes (erasure requested, completed or failed; data exported) reach every open operator
/// data requests page live, so the ledger and its counts reload. The payload names the change and the request,
/// never the subject.
/// </summary>
public sealed class AdminDataRequestsBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<SubjectErasureRequestedEvent>,
        IEventHandler<SubjectErasureCompletedEvent>,
        IEventHandler<SubjectErasureFailedEvent>,
        IEventHandler<SubjectDataExportedEvent>
{
    public Task HandleAsync(SubjectErasureRequestedEvent @event, CancellationToken ct = default) =>
        PushAsync("requested", @event.ErasureRequestId);

    public Task HandleAsync(SubjectErasureCompletedEvent @event, CancellationToken ct = default) =>
        PushAsync("completed", @event.ErasureRequestId);

    public Task HandleAsync(SubjectErasureFailedEvent @event, CancellationToken ct = default) =>
        PushAsync("failed", @event.ErasureRequestId);

    public Task HandleAsync(SubjectDataExportedEvent @event, CancellationToken ct = default) =>
        PushAsync("exported", @event.ErasureRequestId);

    private Task PushAsync(string change, Guid erasureRequestId) =>
        hub.Clients.All.ReceiveDataRequestsChanged(
            new { Change = change, ErasureRequestId = erasureRequestId }
        );
}
