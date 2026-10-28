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

namespace NomNomzBot.Application.Contracts.YouTube;

/// <summary>
/// The ban-id bookkeeping behind YouTube unban (BUILD slice 3b): <c>liveChatBans.delete</c> only accepts the
/// ban resource id returned by the insert, so the platform records every ban/timeout it issues and consumes
/// the newest record when an unban is requested. Persisted — a permanent ban outlives the live session and
/// the process.
/// </summary>
public interface IYouTubeLiveChatBanLedger
{
    /// <summary>
    /// Records a ban the platform just issued, keyed to the tenant and the banned viewer.
    /// <paramref name="primaryBroadcasterId"/> is the channel whose token issued it — the unban resolves
    /// the same token later, including offline.
    /// </summary>
    Task RecordAsync(
        Guid broadcasterId,
        Guid primaryBroadcasterId,
        string liveChatId,
        string bannedChannelId,
        string banId,
        int? durationSeconds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Consumes (soft-deletes) the NEWEST recorded ban for the viewer and returns it — null when nothing is
    /// recorded (never banned by the bot, or already unbanned). Consuming up front is safe in both outcomes:
    /// a successful delete used it, and a NOT_FOUND means YouTube no longer has the ban either way.
    /// </summary>
    Task<YouTubeConsumedBan?> ConsumeLatestAsync(
        Guid broadcasterId,
        string bannedChannelId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>A consumed ledger entry: the deletable ban id and the channel whose token can delete it.</summary>
public sealed record YouTubeConsumedBan(string BanId, Guid PrimaryBroadcasterId);
