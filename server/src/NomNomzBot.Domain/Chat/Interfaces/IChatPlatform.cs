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
