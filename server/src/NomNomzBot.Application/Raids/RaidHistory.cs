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

/// <summary>Raid counts and latest times per remote Twitch user id, split by direction.</summary>
public sealed record RaidHistory(
    IReadOnlyDictionary<string, RaidStats> Outgoing,
    IReadOnlyDictionary<string, RaidStats> Incoming
);
