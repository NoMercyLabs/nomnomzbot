// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
