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
