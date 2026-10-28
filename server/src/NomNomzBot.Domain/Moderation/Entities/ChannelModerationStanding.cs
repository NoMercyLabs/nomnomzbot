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

using System.ComponentModel.DataAnnotations;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// The NEGATIVE, bot-side moderation axis (moderation.md §1 J.12, §9 decision 3): a graduated ignore
/// tier the bot enforces itself — deliberately distinct from <c>ChannelCommunityStanding</c> (positive,
/// badge-sourced) and from Twitch-native ban/timeout (Helix-enforced, never mirrored here). An ABSENT
/// row means normal; deleting the row is the "restore" semantic. Per-platform: a Twitch mute does not
/// mute the same human's Kick identity. <c>UserId</c> is stored RAW (operational enforcement row matched
/// against live inbound chat ids — the ChatPollVote posture, not the [PII-hash] history tables).
/// </summary>
public class ChannelModerationStanding : ITenantScoped
{
    public Guid Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>Platform key: twitch | youtube | kick.</summary>
    [MaxLength(20)]
    public string Provider { get; set; } = null!;

    /// <summary>That platform's user id (raw — the hot path equality-matches inbound chat ids).</summary>
    [MaxLength(64)]
    public string UserId { get; set; } = null!;

    /// <summary>muted | shadowbanned | blacklisted (see <see cref="ModerationStanding"/>).</summary>
    [MaxLength(20)]
    public string Standing { get; set; } = null!;

    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>The acting operator (Users.Id), when known.</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// The <see cref="ChannelModerationStanding.Standing"/> vocabulary. <c>muted</c>: the bot's features
/// ignore the user (commands, triggers, welcomes, votes, giveaway entries, earning) while their chat
/// still displays and persists. <c>shadowbanned</c>: muted PLUS excluded from bot-driven overlay pushes.
/// <c>blacklisted</c>: chat events dropped at the publisher — no persistence, no display, nothing.
/// </summary>
public static class ModerationStanding
{
    public const string Muted = "muted";
    public const string Shadowbanned = "shadowbanned";
    public const string Blacklisted = "blacklisted";

    public static bool IsValid(string standing) => standing is Muted or Shadowbanned or Blacklisted;
}
