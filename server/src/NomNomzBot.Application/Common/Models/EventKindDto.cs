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

namespace NomNomzBot.Application.Common.Models;

/// <summary>
/// One event kind an event-subscription mask can switch: its name, the single bit it owns in the mask,
/// and whether the default mask has it on. The dashboard draws its checkbox list from these.
/// </summary>
public sealed record EventKindDto(string Name, int Bit, bool OnByDefault);
