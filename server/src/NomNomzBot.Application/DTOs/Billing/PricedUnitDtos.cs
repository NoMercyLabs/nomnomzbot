// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
