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
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// A keyword auto-reply: fires on ordinary chat lines (no <c>!</c> prefix) whose text matches
/// <see cref="Pattern"/> under <see cref="MatchType"/> — the "someone says X → the bot reacts" behavior
/// commands can't cover. Responds with a template line OR a bound pipeline (the full reaction chain),
/// spam-guarded by a per-trigger channel cooldown and an optional role floor.
/// </summary>
public class ChatTrigger : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>The text (or regex) to match against the chat line.</summary>
    [MaxLength(200)]
    public string Pattern { get; set; } = null!;

    /// <summary>contains | exact | starts_with | regex (see <see cref="ChatTriggerMatchType"/>).</summary>
    [MaxLength(20)]
    public string MatchType { get; set; } = ChatTriggerMatchType.Contains;

    public bool CaseSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Template response (the same variables a command template sees). Null when a pipeline is bound.</summary>
    [MaxLength(500)]
    public string? Response { get; set; }

    /// <summary>Bound pipeline for chained reactions; wins over <see cref="Response"/> when set.</summary>
    public Guid? PipelineId { get; set; }

    [ForeignKey(nameof(PipelineId))]
    public virtual Pipeline? Pipeline { get; set; }

    /// <summary>Channel-wide cooldown between fires of THIS trigger — the spam guard.</summary>
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>Minimum unified-ladder level of the speaker (0 = everyone).</summary>
    public int MinPermissionLevel { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}

/// <summary>The <see cref="ChatTrigger.MatchType"/> vocabulary.</summary>
public static class ChatTriggerMatchType
{
    public const string Contains = "contains";
    public const string Exact = "exact";
    public const string StartsWith = "starts_with";
    public const string Regex = "regex";
}
