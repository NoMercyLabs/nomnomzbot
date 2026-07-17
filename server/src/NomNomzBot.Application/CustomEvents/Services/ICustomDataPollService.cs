// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
