// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Raids;

/// <summary>
/// Raid history with one remote channel in one direction: how many raids happened (<see cref="Count"/>)
/// and when the latest one did (<see cref="LastAt"/>). Used both ways (us to them, them to us) so the
/// scorer can tell who owes whom a raid.
/// </summary>
public sealed record RaidStats(int Count, DateTimeOffset LastAt);
