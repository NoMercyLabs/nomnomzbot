// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Rewards.Services;

/// <summary>
/// Switches a channel's channel-point rewards off at Twitch when the stream ends and back on when it starts,
/// for a channel that has not opted to keep them active while offline (<c>Channel.KeepActiveWhileOffline</c>).
/// Only rewards the bot turned off itself (<c>Reward.DisabledForOffline</c>) are ever turned back on.
/// </summary>
public interface IOfflineRewardService
{
    /// <summary>
    /// Stream ended: disables, at Twitch, every reward that is enabled and that the bot can manage, and marks
    /// each one. Does nothing when the channel keeps its rewards active while offline. A reward Twitch refuses is
    /// skipped and logged; it never fails the run.
    /// </summary>
    Task DisableForOfflineAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream started (or the keep-active setting was turned on): enables, at Twitch, only the rewards carrying
    /// the offline marker, then clears the marker. A reward the streamer turned off by hand has no marker and
    /// stays off.
    /// </summary>
    Task EnableMarkedAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The keep-active setting changed: when the channel is offline and now keeps its rewards active, turns the
    /// marked rewards back on at once.
    /// </summary>
    Task ApplyKeepActiveSettingAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
