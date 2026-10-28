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

namespace NomNomzBot.Application.CustomEvents.Services;

/// <summary>
/// The <c>poll</c> ingress runner (custom-events.md §6). One scan pass walks every enabled poll-kind
/// <c>CustomDataSource</c> whose interval has elapsed, fetches its user-supplied <c>EndpointUrl</c>
/// through the SSRF-hardened egress client — but only when the host is on an enabled H.7 egress
/// allowlist row for that channel — and feeds a successful body into the single ingest path.
/// </summary>
public interface ICustomDataPollService
{
    /// <summary>
    /// Fetches every due poll source once. Per-source faults are isolated; one bad source never aborts
    /// the pass. Non-allowlisted hosts are skipped without a fetch (the SSRF gate).
    /// </summary>
    Task PollDueSourcesAsync(CancellationToken ct = default);
}
