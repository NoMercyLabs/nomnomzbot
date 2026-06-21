// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A thin 1:1 cache (economy.md K.8) over the authoritative <c>ConsentRecords</c> (O.5,
/// <c>ConsentType=age_18_gambling</c>) recording whether a viewer has confirmed they are 18+ for gambling in
/// this channel. <c>Granted</c> + <c>RevokedAt == null</c> means the gate is open. One per
/// <c>(BroadcasterId, ViewerUserId)</c>.
/// </summary>
public class ViewerAgeConsent : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid ViewerUserId { get; set; }
    public string ViewerTwitchUserId { get; set; } = null!;
    public Guid ConsentRecordId { get; set; }
    public bool Granted { get; set; }
    public DateTime ConfirmedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string ConfirmationMethod { get; set; } = null!;
}
