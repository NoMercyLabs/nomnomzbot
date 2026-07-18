// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// The visibility tier of a domain event on the developer-platform surface (dev-platform.md §1.2). The tier
/// decides which SDK contexts may see an event and its payload:
/// <list type="bullet">
///   <item><see cref="Public"/> — safe for untrusted, viewer-facing widget code.</item>
///   <item><see cref="Moderator"/> — the channel's moderator-trusted scripts.</item>
///   <item><see cref="Broadcaster"/> — the channel owner's own scripts. This is the SAFE DEFAULT: an event
///   with no <c>[Event(...)]</c> tier is visible to the owner's code but NOT to untrusted widgets
///   (default-deny toward viewers).</item>
///   <item><see cref="Internal"/> — never leaves the server; excluded from every SDK context.</item>
/// </list>
/// The tiers are ordered ascending in trust; a context that admits a higher tier admits every lower one.
/// </summary>
public enum EventVisibility
{
    /// <summary>Safe for untrusted, viewer-facing widget code (browser-side, null-origin iframe).</summary>
    Public = 0,

    /// <summary>Visible to the channel's moderator-trusted scripts, and to everything above.</summary>
    Moderator = 1,

    /// <summary>The safe default — the channel owner's own scripts, and above. Not visible to widgets.</summary>
    Broadcaster = 2,

    /// <summary>Server-internal plumbing — excluded from every SDK context.</summary>
    Internal = 3,
}
