// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
