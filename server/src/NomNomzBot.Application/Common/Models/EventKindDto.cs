// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Models;

/// <summary>
/// One event kind an event-subscription mask can switch: its name, the single bit it owns in the mask,
/// and whether the default mask has it on. The dashboard draws its checkbox list from these.
/// </summary>
public sealed record EventKindDto(string Name, int Bit, bool OnByDefault);
