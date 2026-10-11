// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// What retracting one song request did. The provider (Spotify) cannot delete from its own queue, so a
/// request it already holds can only be skipped when it starts — callers must tell the viewer which of the
/// two happened.
/// </summary>
public enum RequestRetraction
{
    /// <summary>No request at that position; nothing changed.</summary>
    NotRemoved,

    /// <summary>Dropped from the fair queue. The provider does not hold it, so it will never play.</summary>
    Removed,

    /// <summary>
    /// Dropped from the fair queue, and the provider's live queue still lists it. It is armed to be skipped
    /// the moment it starts; every other song in the provider's queue is left alone.
    /// </summary>
    RemovedWillSkip,
}
