// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.Security;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// The same sanction rule as the Helix transport, applied to providers whose calls are scattered across many
/// call sites instead of funnelling through one send. Registered on a named <c>HttpClient</c>, it covers every
/// request through that client — including ones nobody has written yet, which is the point: a future Discord
/// or Spotify write cannot be added without either declaring its basis or failing loudly the first time it runs.
///
/// <para>
/// A refused call throws rather than returning a typed failure, because there is no shared result type down
/// here. That is deliberate: the alternative to a loud failure is a silent one, and this whole mechanism
/// exists because a silent, unattributed write reached real broadcasters.
/// </para>
/// </summary>
public sealed class OutboundSanctionHandler(
    IOutboundSanctionAccessor sanctions,
    ILogger<OutboundSanctionHandler> logger
) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (!IsWrite(request.Method) || sanctions.Current is not null)
            return base.SendAsync(request, cancellationToken);

        logger.LogError(
            "Refused an unsanctioned outbound {Method} {Uri}: no user action or configured basis claimed it.",
            request.Method,
            request.RequestUri
        );

        throw new UnsanctionedOutboundCallException(request.Method, request.RequestUri);
    }

    private static bool IsWrite(HttpMethod method) =>
        method == HttpMethod.Post
        || method == HttpMethod.Patch
        || method == HttpMethod.Put
        || method == HttpMethod.Delete;
}

/// <summary>Thrown when a write to a third party had nothing authorising it.</summary>
public sealed class UnsanctionedOutboundCallException(HttpMethod method, Uri? uri)
    : InvalidOperationException(
        $"Refused an unsanctioned outbound {method} to {uri}: nothing recorded who or what authorised "
            + "changing another platform's state."
    );
