// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
