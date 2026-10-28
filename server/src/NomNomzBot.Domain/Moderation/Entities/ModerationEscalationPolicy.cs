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

    public bool IsEnabled { get; set; } = EscalationPolicyDefaults.IsEnabled;

    /// <summary>The ordered ladder steps [VC:JSON <c>List&lt;EscalationLadderStep&gt;</c>].</summary>
    public string LadderJson { get; set; } = "[]";

    /// <summary>The decaying offense window — a tally older than this restarts at offense 1.</summary>
    public int OffenseWindowHours { get; set; } = EscalationPolicyDefaults.OffenseWindowHours;

    /// <summary>Whether AutoMod violations count as ladder offenses too.</summary>
    public bool CountAutoModViolations { get; set; } =
        EscalationPolicyDefaults.CountAutoModViolations;

    /// <summary>
    /// The timeout length, in seconds, a one-click chat Timeout uses (1..1209600 — Twitch's maximum).
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = EscalationPolicyDefaults.DefaultTimeoutSeconds;

    public int ConfigSchemaVersion { get; set; } = 1;
}
