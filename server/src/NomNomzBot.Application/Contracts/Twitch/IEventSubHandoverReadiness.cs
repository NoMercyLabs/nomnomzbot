// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Whether this instance can take EventSub over from the instance a deploy is about to stop
/// (twitch-eventsub §10). In conduit mode that means its conduit shard is bound; outside conduit mode there
/// is nothing to wait for. A latch: once true it stays true, so a later shard reconnect never pulls a live
/// instance out of the proxy pool.
/// </summary>
public interface IEventSubHandoverReadiness
{
    /// <summary>
    /// Always true for an instance that is not a standby (a lone instance is the only upstream; holding it
    /// back would 503 the port). For a standby behind a live lease holder: true once the shard is bound, or
    /// once <c>ShardReadyTimeout</c> has passed since start — a Twitch outage must not hold a deploy forever.
    /// </summary>
    bool IsReadyForHandover { get; }
}
