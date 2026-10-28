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

using System.Text.Json;
using NomNomzBot.Infrastructure.Platform.Http;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// The one way a lookup source is read: a GET with the product <c>User-Agent</c> that gives up after the
/// source's timeout (catalog section 9, rule 1). Every failure (timeout, network, a non-success status, a body
/// that is not JSON) is a null, so the caller renders its fallback string and chat is never blocked.
/// </summary>
internal sealed class LookupHttp(Func<HttpClient> createClient)
{
    public async Task<JsonDocument?> GetJsonAsync(
        Uri uri,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        limit.CancelAfter(timeout);
        try
        {
            using HttpClient http = createClient();
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(AppUserAgent.Value);
            request.Headers.Accept.ParseAdd("application/json");
            using HttpResponseMessage response = await http.SendAsync(request, limit.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            await using System.IO.Stream body = await response.Content.ReadAsStreamAsync(
                limit.Token
            );
            return await JsonDocument.ParseAsync(body, cancellationToken: limit.Token);
        }
        catch (Exception exception)
            when (!cancellationToken.IsCancellationRequested
                && exception is HttpRequestException or OperationCanceledException or JsonException
            )
        {
            return null;
        }
    }
}
