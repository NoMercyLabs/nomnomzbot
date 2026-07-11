// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.Interfaces;

/// <summary>
/// One streaming platform's chat surface (BUILD slice 3 — the thin multi-platform seam from the
/// 2026-06-16 rebuild decision): the full <see cref="IChatProvider"/> operation set plus the platform
/// key it serves. Implementations register multi-bound; the platform-routing <c>IChatProvider</c>
/// selects one per send by the tenant channel's <c>Channel.Provider</c>, so every existing send site
/// (commands, pipelines, timers, dashboard) reaches the right platform with zero call-site changes.
/// An operation a platform cannot perform degrades gracefully (logged no-op or honest failure) —
/// it never throws into the hot chat path.
/// </summary>
public interface IChatPlatform : IChatProvider
{
    /// <summary>The <c>Channel.Provider</c> key this platform serves — "twitch", "youtube", …</summary>
    string Provider { get; }
}
