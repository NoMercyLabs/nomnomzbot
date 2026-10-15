// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// Opens or closes the channel's song queue to new requests (<c>!closequeue</c> / <c>!openqueue</c> and the
/// dashboard button). The music keeps playing and the queued songs stay; only new requests are refused.
/// </summary>
public interface ISongRequestIntakeService
{
    /// <summary>
    /// Sets the open state. The value is <c>true</c> when the state changed and <c>false</c> when the queue
    /// was already in the asked state.
    /// </summary>
    Task<Result<bool>> SetRequestsOpenAsync(
        string broadcasterId,
        bool open,
        CancellationToken cancellationToken = default
    );
}
