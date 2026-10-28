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

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>How fresh a helper's value is, and so what a render must fetch (catalog section 4).</summary>
public enum TemplateHelperFreshness
{
    /// <summary>Computed from the arguments alone; no fetch.</summary>
    Pure,

    /// <summary>Supplied by the caller's seed variables; no fetch.</summary>
    Seed,

    /// <summary>Read from the in-memory channel state.</summary>
    Memory,

    /// <summary>Read through the cache, keyed by channel and source.</summary>
    Cached,

    /// <summary>Fetched on every render.</summary>
    Live,
}
