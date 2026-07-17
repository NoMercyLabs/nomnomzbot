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
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// A custom per-channel chat filter (moderation.md J.6): a regex / blocklist / link-policy rule the bot runs
/// against every incoming chat message. On a match it applies <see cref="Action"/> — delete / timeout the
/// message directly, hold or flag it for review, or <see cref="ChatFilterAction.Escalate"/>, which hands the
/// punishment to the channel's escalation ladder (§3.11). Senders at or above <see cref="ExemptMinRoleLevel"/>
/// on the permission ladder are never filtered. Nav-free (convention-mapped) — the tenant is
/// <see cref="BroadcasterId"/>.
/// </summary>
public class ChatFilter : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning channel (tenant key).</summary>
    public Guid BroadcasterId { get; set; }

    public ChatFilterType FilterType { get; set; }

    /// <summary>Human-readable name shown in the dashboard.</summary>
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>The regex source for a <see cref="ChatFilterType.Regex"/> filter (null for the other types).</summary>
    [MaxLength(2000)]
    public string? Pattern { get; set; }

    /// <summary>Literal blocklist terms for a <see cref="ChatFilterType.Blocklist"/> filter [VC:JSON <c>List&lt;string&gt;</c>].</summary>
    public string? TermsJson { get; set; }

    /// <summary>The link-policy configuration for a <see cref="ChatFilterType.LinkPolicy"/> filter [VC:JSON].</summary>
    public string? LinkPolicyJson { get; set; }

    public ChatFilterAction Action { get; set; }

    /// <summary>Timeout length when <see cref="Action"/> is <see cref="ChatFilterAction.Timeout"/>.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Senders at or above this permission-ladder level are exempt (default = the moderator floor, 10).</summary>
    public int ExemptMinRoleLevel { get; set; } = 10;

    public bool IsEnabled { get; set; } = true;

    public bool IsCaseSensitive { get; set; }

    /// <summary>Running tally of how often this filter has matched — bumped on every enforcement.</summary>
    public long MatchCount { get; set; }
}
