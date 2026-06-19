// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Eventing;

public interface ITwitchEventSubService
{
    Task SubscribeAsync(string broadcasterId, string eventType, CancellationToken ct = default);
    Task UnsubscribeAsync(string subscriptionId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetActiveSubscriptionsAsync(
        string broadcasterId,
        CancellationToken ct = default
    );
}
