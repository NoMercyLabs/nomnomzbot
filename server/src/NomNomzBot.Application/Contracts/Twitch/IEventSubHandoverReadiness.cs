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
