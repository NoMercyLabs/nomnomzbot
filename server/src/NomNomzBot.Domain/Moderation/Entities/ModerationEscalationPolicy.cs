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
/// The channel's escalation ladder (moderation.md §3.11, schema J.10): repeat offenders climb a
/// configurable punishment ladder (warn → growing timeouts → ban) inside a decaying offense window.
/// One policy per channel; <see cref="LadderJson"/> is the ordered <c>List&lt;EscalationLadderStep&gt;</c>.
/// </summary>
public class ModerationEscalationPolicy : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One policy per channel (unique).</summary>
    public Guid BroadcasterId { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>The ordered ladder steps [VC:JSON <c>List&lt;EscalationLadderStep&gt;</c>].</summary>
    public string LadderJson { get; set; } = "[]";

    /// <summary>The decaying offense window — a tally older than this restarts at offense 1.</summary>
    public int OffenseWindowHours { get; set; } = 168;

    /// <summary>Whether AutoMod violations count as ladder offenses too.</summary>
    public bool CountAutoModViolations { get; set; }

    public int ConfigSchemaVersion { get; set; } = 1;
}
