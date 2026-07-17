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
/// Per-channel shared-ban policy (moderation.md §3.5, schema J.9) — the OPT-IN switches for the shared-chat
/// ban trust web. Both default OFF: <see cref="AcceptSharedChatBans"/> lets bans issued by a TRUSTED partner
/// channel during a verified shared-chat session apply here too; <see cref="ShareOutgoingBans"/> offers this
/// channel's own shared-chat bans to its partners. One row per channel.
/// </summary>
public class SharedBanSettings : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>One settings row per channel (unique).</summary>
    public Guid BroadcasterId { get; set; }

    public bool AcceptSharedChatBans { get; set; }

    public bool ShareOutgoingBans { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;
}
