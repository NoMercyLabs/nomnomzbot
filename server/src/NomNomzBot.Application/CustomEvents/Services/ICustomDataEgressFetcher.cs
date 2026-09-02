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
/// The single SSRF-gated GET fetch used by both the poll ingress (<c>CustomDataPollService</c>) and the
/// dashboard's one-off "test fetch" (<c>CustomDataSourceService.TestFetchAsync</c>) — one place validates the
/// endpoint host against the channel's H.7 <c>HttpEgressAllowlist</c>, sends through the shared SSRF-hardened
/// <c>EgressHttpClient</c>, and reads the response into a fixed byte cap so a truncated fragment is never treated
/// as a whole payload.
/// </summary>
public interface ICustomDataEgressFetcher
{
    Task<CustomDataEgressFetchResult> FetchAsync(
        Guid broadcasterId,
        string? endpointUrl,
        string? bearerToken,
        CancellationToken ct = default
    );
}

/// <summary>Why a fetch did or did not produce a body.</summary>
public enum CustomDataEgressFetchOutcome
{
    /// <summary>No usable absolute URL was configured — no fetch was attempted.</summary>
    NoUrl,

    /// <summary>The endpoint host is not an enabled H.7 egress-allowlist row for this channel — no fetch was attempted.</summary>
    NotAllowlisted,

    /// <summary>The fetch reached the host but got a non-2xx status, or the request itself faulted.</summary>
    HttpError,

    /// <summary>The response body exceeded the byte cap.</summary>
    Oversize,

    /// <summary>A 2xx response was read in full (possibly empty).</summary>
    Success,
}

public sealed record CustomDataEgressFetchResult(
    CustomDataEgressFetchOutcome Outcome,
    string? Body,
    string? ErrorMessage
)
{
    public static CustomDataEgressFetchResult Ok(string body) =>
        new(CustomDataEgressFetchOutcome.Success, body, null);

    public static CustomDataEgressFetchResult Fail(
        CustomDataEgressFetchOutcome outcome,
        string message
    ) => new(outcome, null, message);
}
