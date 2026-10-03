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
    /// <summary>The internal id of this send attempt.</summary>
    public required Guid DispatchId { get; init; }

    /// <summary>The internal id of the Discord notification setting that caused this send.</summary>
    public required Guid NotificationConfigId { get; init; }

    /// <summary>The kind of trigger behind the notification: go_live, new_clip, schedule or milestone.</summary>
    public required string TriggerType { get; init; }

    /// <summary>A key that stops the same notification being sent twice.</summary>
    public required string DedupeKey { get; init; }

    /// <summary><c>sent</c> | <c>failed</c> | <c>skipped_dupe</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The Discord message id of the posted notification (a number as text). Empty when nothing was posted.</summary>
    public string? PostedMessageId { get; init; }

    /// <summary>The reason the send failed. Empty when it did not fail.</summary>
    public string? Error { get; init; }
}
