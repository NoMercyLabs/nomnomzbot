// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// Throttles overlay ticket-issuance attempts per source (S035 item 3, U·B7) — a runaway/misconfigured OBS
/// browser source, or a leaked overlay token, must not be able to hammer the ticket endpoint unbounded.
/// </summary>
public interface IOverlayConnectionThrottle
{
    /// <summary>Records one attempt for <paramref name="key"/> and reports whether it is allowed to proceed.
    /// Returns <c>false</c> once the caller has exceeded the window's attempt budget.</summary>
    bool TryAcquire(string key);
}
