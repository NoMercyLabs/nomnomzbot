// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Import.Dtos;

/// <summary>
/// The per-provider import outcome: how many of each entity landed versus were skipped, plus any non-fatal
/// warnings. A skip is a duplicate that already exists (or a malformed entry) — counted, never fatal — so an
/// import is idempotent: re-running the same export imports nothing new instead of erroring.
/// </summary>
public sealed record ImportSummary
{
    public int CommandsImported { get; init; }
    public int CommandsSkipped { get; init; }
    public int QuotesImported { get; init; }
    public int QuotesSkipped { get; init; }
    public int TimersImported { get; init; }
    public int TimersSkipped { get; init; }

    /// <summary>Human-readable notes for entries that could not be imported (malformed, rejected by a service).</summary>
    public List<string> Warnings { get; init; } = [];
}
