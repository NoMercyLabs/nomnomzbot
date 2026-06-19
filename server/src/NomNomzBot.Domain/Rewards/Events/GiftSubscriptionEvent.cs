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

namespace NomNomzBot.Domain.Rewards.Events;

public sealed class GiftSubscriptionEvent : DomainEventBase
{
    public required string GifterUserId { get; init; }
    public required string GifterDisplayName { get; init; }

    /// <summary>"1000", "2000", or "3000"</summary>
    public required string Tier { get; init; }

    public required int GiftCount { get; init; }
    public required bool IsAnonymous { get; init; }
    public required IReadOnlyList<GiftRecipient> Recipients { get; init; }
}

public sealed record GiftRecipient(string UserId, string DisplayName);
