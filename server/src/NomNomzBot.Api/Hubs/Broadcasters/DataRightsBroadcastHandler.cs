// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// The payload of a <c>DataRightsChanged</c> push: what happened to a data request or a consent
/// (requested, completed, failed, exported, consent). It names no person and carries no personal data;
/// the page reads its own rows again.
/// </summary>
public record DataRightsChangedAlertDto(string Change);

/// <summary>
/// Pushes data request and consent changes (export, erasure, consent) to the channel's dashboard clients as
/// <c>DataRightsChanged</c>, so the "Your data" page shows the new state with no reload.
/// </summary>
public sealed class DataRightsBroadcastHandler
    : IEventHandler<SubjectErasureRequestedEvent>,
        IEventHandler<SubjectErasureCompletedEvent>,
        IEventHandler<SubjectErasureFailedEvent>,
        IEventHandler<SubjectDataExportedEvent>,
        IEventHandler<ConsentChangedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public DataRightsBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(SubjectErasureRequestedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event.BroadcasterId, "requested", ct);

    public Task HandleAsync(SubjectErasureCompletedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event.BroadcasterId, "completed", ct);

    public Task HandleAsync(SubjectErasureFailedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event.BroadcasterId, "failed", ct);

    public Task HandleAsync(SubjectDataExportedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event.BroadcasterId, "exported", ct);

    public Task HandleAsync(ConsentChangedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event.BroadcasterId, "consent", ct);

    private Task PushAsync(Guid broadcasterId, string change, CancellationToken ct)
    {
        if (broadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            broadcasterId.ToString(),
            "DataRightsChanged",
            new DataRightsChangedAlertDto(change),
            ct
        );
    }
}
