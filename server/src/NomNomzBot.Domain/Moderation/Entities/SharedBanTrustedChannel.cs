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

using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// One channel this channel TRUSTS for inbound shared-chat bans (moderation.md §3.5, schema J.9a) — the
/// allow-list <c>ApplyInboundSharedBanAsync</c> checks the ban's origin against. Unique per
/// (BroadcasterId, TrustedChannelId); trust is directional, never mutual by implication.
/// </summary>
public class SharedBanTrustedChannel : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The TRUSTING channel — inbound bans apply here.</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>The trusted partner whose shared-chat bans are accepted.</summary>
    public Guid TrustedChannelId { get; set; }

    /// <summary>Who added the trust entry (audit; null for system writes).</summary>
    public Guid? AddedByUserId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(TrustedChannelId))]
    public virtual Channel TrustedChannel { get; set; } = null!;
}
