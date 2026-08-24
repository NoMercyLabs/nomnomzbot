// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Discord;

/// <summary>
/// The "currently live" Discord role rule (discord.md live-role extension): applies/removes
/// <c>DiscordLiveRoleConfig.RoleId</c> on the streamer's own guild member as their channel goes online/offline.
/// Best-effort like <c>DiscordGoLiveNotificationHandler</c> — a Discord-side failure is logged (never thrown,
/// never silent) and must never disturb the live flow. Idempotent: a duplicate online event for the same
/// stream session is a no-op; <see cref="ReconcileStaleAsync"/> clears a role stranded by a missed offline
/// event (bot restart/crash) by comparing applied state against the channel's actual live status.
/// </summary>
public interface IDiscordLiveRoleService
{
    /// <summary>Applies every enabled, actively-linked live-role rule for the channel that just went online.
    /// <paramref name="dedupeKey"/> mirrors the go-live handler's per-session key — a repeat for the same
    /// session is skipped, not re-applied.</summary>
    Task ApplyForOnlineAsync(Guid broadcasterId, string dedupeKey, CancellationToken ct = default);

    /// <summary>Removes the role for every currently-applied live-role rule of the channel that just went
    /// offline, regardless of its current Enabled flag (a disabled-mid-stream rule still gets cleaned up).</summary>
    Task RemoveForOfflineAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Startup self-heal: clears (removes the Discord role + resets state for) every rule whose
    /// <c>IsCurrentlyApplied</c> is stale against its channel's actual <c>IsLive</c>.</summary>
    Task ReconcileStaleAsync(CancellationToken ct = default);
}
