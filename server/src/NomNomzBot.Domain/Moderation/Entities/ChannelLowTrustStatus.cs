// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// Twitch's suspicious-user flag for one chatter in one channel (<c>channel.suspicious_user.update</c> and
/// <c>channel.suspicious_user.message</c>). An ABSENT row means <see cref="LowTrustStatuses.None"/>; a change
/// back to none deletes the row. <c>TwitchUserId</c> is stored RAW: the spam-defence hot path matches it
/// against live inbound chat ids.
/// </summary>
public class ChannelLowTrustStatus : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    [MaxLength(64)]
    public string TwitchUserId { get; set; } = null!;

    /// <summary>active_monitoring or restricted (see <see cref="LowTrustStatuses"/>).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    /// <summary>Twitch's ban-evasion likelihood (likely, possible, unlikely), when a message carried it.</summary>
    [MaxLength(20)]
    public string? BanEvasionEvaluation { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>The Twitch <c>low_trust_status</c> vocabulary.</summary>
public static class LowTrustStatuses
{
    public const string None = "none";
    public const string ActiveMonitoring = "active_monitoring";
    public const string Restricted = "restricted";

    public static bool IsValid(string status) => status is None or ActiveMonitoring or Restricted;

    /// <summary>Folds an unknown or missing value to <see cref="None"/>.</summary>
    public static string Normalize(string? status)
    {
        string trimmed = status?.Trim().ToLowerInvariant() ?? None;
        return IsValid(trimmed) ? trimmed : None;
    }
}
