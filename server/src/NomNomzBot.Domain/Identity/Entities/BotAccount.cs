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

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A bot identity (schema E.3) — the one shared platform bot (<c>IdentityType=shared</c>) every channel
/// uses by default, plus optional per-channel custom (white-label) bots. GLOBAL: not tenant-scoped. Its
/// vaulted OAuth tokens live on the linked platform/tenant <see cref="ConnectionId"/> integration
/// connection, not here.
/// </summary>
public class BotAccount : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [MaxLength(10)]
    public string IdentityType { get; set; } = null!;

    [MaxLength(20)]
    public string Platform { get; set; } = null!;

    // External bot user id on the platform (e.g. Twitch user id) — an indexed attribute, not the key.
    [MaxLength(50)]
    public string BotUserId { get; set; } = null!;

    [MaxLength(255)]
    public string BotUsername { get; set; } = null!;

    // The IntegrationConnection holding this bot's vaulted tokens (null until connected).
    public Guid? ConnectionId { get; set; }

    public bool IsActive { get; set; } = true;
}
