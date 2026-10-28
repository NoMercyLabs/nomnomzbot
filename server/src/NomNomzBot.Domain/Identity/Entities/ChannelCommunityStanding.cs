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

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A viewer's community standing (Plane A) in one channel (roles-permissions schema B.2) — the chat-badge /
/// EventSub-badge sourced axis (Everyone &lt; Subscriber &lt; Vip &lt; Artist &lt; Moderator). <c>LevelValue</c>
/// is the denormalized ladder value of <see cref="CommunityStanding"/>; <c>SubTier</c> records the Twitch sub
/// tier (1000/2000/3000) when subscribed. Unique per <c>(BroadcasterId, UserId)</c>.
/// </summary>
public class ChannelCommunityStanding : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid UserId { get; set; }
    public CommunityStanding Standing { get; set; }
    public int LevelValue { get; set; }
    public StandingSource Source { get; set; }
    public string? SubTier { get; set; }
    public DateTime? LastSeenAt { get; set; }
}
