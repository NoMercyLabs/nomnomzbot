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

namespace NomNomzBot.Application.DTOs.Billing;

// ── Priced-unit authoring (S-ADMIN-4d). Money is integer minor units, never a float. ──

/// <summary>
/// The owner-authored price of one usage unit. <see cref="PriceMinorUnitsPerBatch"/> is charged per
/// <see cref="BatchSize"/> raw units of <see cref="UnitKey"/> — a real cost of a fraction of a minor unit
/// per raw unit (e.g. TTS characters, sandbox milliseconds) is represented exactly by pricing a batch of
/// them, never by a float.
/// </summary>
public sealed record PricedUnitDto(
    Guid Id,
    string UnitKey,
    string Currency,
    long PriceMinorUnitsPerBatch,
    long BatchSize
);

/// <summary>
/// Author (create or reprice) one unit. Upsert by <see cref="UnitKey"/>: a key with no existing row is
/// created; an existing key has its price overwritten. There is no per-period price history — see
/// <c>PricedUnit</c>'s doc comment for the deliberate current-rate-only semantics this implies.
/// </summary>
public sealed record AuthorPricedUnitRequest(
    string UnitKey,
    string Currency,
    long PriceMinorUnitsPerBatch,
    long BatchSize
);
