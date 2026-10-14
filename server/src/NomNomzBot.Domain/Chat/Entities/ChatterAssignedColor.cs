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

namespace NomNomzBot.Domain.Chat.Entities;

/// <summary>
/// The chat color the bot gave a chatter who has none of their own, kept per channel so the same name shows
/// the same color on every line and after every restart.
/// </summary>
public class ChatterAssignedColor : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>The platform the chatter writes on (<c>twitch</c>, <c>youtube</c>, <c>kick</c>).</summary>
    public string Provider { get; set; } = null!;

    public string UserId { get; set; } = null!;

    /// <summary>One of <see cref="ValueObjects.ChatColorPalette.Colors"/>.</summary>
    public string ColorHex { get; set; } = null!;
}
