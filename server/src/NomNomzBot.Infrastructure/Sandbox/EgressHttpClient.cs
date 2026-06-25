// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;

namespace NomNomzBot.Infrastructure.Sandbox;

/// <summary>
/// The single SSRF-hardened egress client (code-execution-sandbox.md §6.3/§7.1), shared by the sandbox and
/// outbound webhooks. The primary handler resolves the host ONCE, rejects if ANY resolved IP is non-public (via
/// <see cref="EgressAddressGuard"/>), then connects to the validated IP — resolve-then-pin, closing the DNS-rebind
/// /TOCTOU window that a handler-level pin leaves open. Redirects are off (no redirect-based pivot).
/// </summary>
public static class EgressHttpClient
{
    /// <summary>The named <c>HttpClient</c> registration key.</summary>
    public const string Name = "egress-allowlisted";

    public static SocketsHttpHandler CreateHandler() =>
        new()
        {
            AllowAutoRedirect = false, // a 3xx must not silently pivot to a fresh (unvalidated) host
            ConnectCallback = ConnectValidatedAsync,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        };

    private static async ValueTask<System.IO.Stream> ConnectValidatedAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken
    )
    {
        DnsEndPoint target = context.DnsEndPoint;
        IPAddress[] resolved = await Dns.GetHostAddressesAsync(target.Host, cancellationToken);
        if (resolved.Length == 0)
            throw new HttpRequestException("Egress host did not resolve.");

        // Fail closed if ANY resolved address is non-public — a split-horizon rebind cannot smuggle one past us.
        foreach (IPAddress address in resolved)
            if (EgressAddressGuard.IsBlocked(address))
                throw new HttpRequestException("Egress to a non-public address is blocked.");

        // Resolve-then-pin: connect to the validated IP, never re-resolving the hostname at connect time.
        Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(resolved[0], target.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>Defense-in-depth scheme gate — egress is <c>https</c> only (reject http/ws/file/...).</summary>
public sealed class EgressSchemeHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (request.RequestUri is null || request.RequestUri.Scheme != Uri.UriSchemeHttps)
            throw new HttpRequestException("Egress is restricted to https.");
        return base.SendAsync(request, cancellationToken);
    }
}
