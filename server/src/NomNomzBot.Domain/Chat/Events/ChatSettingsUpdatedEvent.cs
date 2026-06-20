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

namespace NomNomzBot.Domain.Chat.Events;

/// <summary>
/// Published for EventSub <c>channel.chat_settings.update</c>: the channel's chat moderation modes changed.
/// Mirrors the payload's mode toggles; the duration/wait fields are null when their mode is off.
/// </summary>
public sealed class ChatSettingsUpdatedEvent : DomainEventBase
{
    public required bool EmoteMode { get; init; }
    public required bool FollowerMode { get; init; }

    /// <summary>Minimum follow age (minutes) when <see cref="FollowerMode"/> is on; null otherwise.</summary>
    public int? FollowerModeDurationMinutes { get; init; }

    public required bool SlowMode { get; init; }

    /// <summary>Seconds a chatter must wait between messages when <see cref="SlowMode"/> is on; null otherwise.</summary>
    public int? SlowModeWaitSeconds { get; init; }

    public required bool SubscriberMode { get; init; }
    public required bool UniqueChatMode { get; init; }
}
