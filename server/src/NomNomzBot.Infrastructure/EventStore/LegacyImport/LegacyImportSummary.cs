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

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// Outcome of a one-shot legacy <c>ChannelEvents</c> import. <see cref="Imported"/> counts the journal rows newly
/// appended; <see cref="SkippedUnmapped"/> counts rows whose type/shape is not an imported channel event;
/// <see cref="SkippedDuplicate"/> counts rows whose derived <c>EventId</c> already existed (re-run idempotency).
/// <see cref="SkippedByLegacyType"/> breaks the unmapped skips down by their legacy <c>Type</c> so the operator
/// sees precisely what was dropped and can confirm it is intended (definition CRUD, progress ticks, websocket
/// noise, …) rather than a silent loss — every legacy type is accounted for between imported and skipped.
/// </summary>
public sealed record LegacyImportSummary(
    long TotalRead,
    long Imported,
    long SkippedUnmapped,
    long SkippedDuplicate,
    IReadOnlyDictionary<string, long> SkippedByLegacyType
);
