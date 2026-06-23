// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Discord.Events;

/// <summary>
/// Published after a notification is posted to Discord (or deduped/failed). Mirrors the appended
/// <c>DiscordNotificationDispatch</c> row for the SignalR dashboard feed + audit. The publisher sets the
/// inherited <c>BroadcasterId</c> to the dispatching channel; tenant-scoped, never <c>Guid.Empty</c>.
/// </summary>
public sealed class DiscordNotificationDispatchedEvent : DomainEventBase
{
    public required Guid DispatchId { get; init; }
    public required Guid NotificationConfigId { get; init; }
    public required string TriggerType { get; init; }
    public required string DedupeKey { get; init; }

    /// <summary><c>sent</c> | <c>failed</c> | <c>skipped_dupe</c>.</summary>
    public required string Status { get; init; }
    public string? PostedMessageId { get; init; }
    public string? Error { get; init; }
}
