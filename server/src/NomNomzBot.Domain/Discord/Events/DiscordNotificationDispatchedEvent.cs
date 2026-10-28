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
