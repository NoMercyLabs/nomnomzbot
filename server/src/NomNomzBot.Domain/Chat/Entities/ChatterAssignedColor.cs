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
