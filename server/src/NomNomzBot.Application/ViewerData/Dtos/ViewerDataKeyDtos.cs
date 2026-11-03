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

namespace NomNomzBot.Application.ViewerData.Dtos;

/// <summary>One viewer-data key that at least one viewer of a channel holds.</summary>
/// <param name="Key">The key (a lowercase slug).</param>
/// <param name="ViewerCount">How many viewers of the channel hold this key.</param>
public sealed record ViewerDataKeySummary(string Key, int ViewerCount);
