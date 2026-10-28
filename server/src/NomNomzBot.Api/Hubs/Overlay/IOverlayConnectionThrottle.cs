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
