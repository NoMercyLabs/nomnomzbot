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

    /// <summary>
    /// A confirmed read of the live state at Twitch (the startup check): offline turns the rewards off, live turns
    /// the marked rewards back on. Covers a channel whose stream edge the bot never saw.
    /// </summary>
    Task ApplyLiveStateAsync(
        Guid broadcasterId,
        bool isLive,
        CancellationToken cancellationToken = default
    );
}
