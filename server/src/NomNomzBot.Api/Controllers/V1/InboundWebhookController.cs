// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Public inbound webhook ingest (webhooks.md §5.2). Anonymous — authenticated by the opaque URL token + the
/// per-adapter signature (the dispatcher owns both). Reads the raw buffered body (the signature is over the exact
/// bytes), guards method/size/content-type cheaply, then hands off. The response is a minimal status ack, never
/// problem-details (so senders don't retry on a body they can't parse) and never leaks internal detail.
/// (Deferred: the 2-tier IP/endpoint rate limiter awaits the partitioned rate-limit store; the token + signature
/// remain the security boundary.)
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks/in")]
[AllowAnonymous]
[Tags("Webhooks")]
public class InboundWebhookController(IInboundWebhookDispatcher dispatcher, TimeProvider clock)
    : ControllerBase
{
    private const int MaxBodyBytes = 256 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "application/json",
        "application/x-www-form-urlencoded",
    };

    [HttpPost("{token}")]
    public async Task<IActionResult> Receive(string token, CancellationToken ct)
    {
        string contentType = (Request.ContentType ?? string.Empty).Split(';')[0].Trim();
        if (!AllowedContentTypes.Contains(contentType))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);

        if (Request.ContentLength is > MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        byte[] body;
        using (MemoryStream buffer = new())
        {
            await Request.Body.CopyToAsync(buffer, ct);
            if (buffer.Length > MaxBodyBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            body = buffer.ToArray();
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (
            KeyValuePair<
                string,
                Microsoft.Extensions.Primitives.StringValues
            > header in Request.Headers
        )
            headers[header.Key] = header.Value.ToString();

        InboundWebhookRequest request = new()
        {
            Token = token,
            Method = Request.Method,
            ContentType = contentType,
            Headers = headers,
            RawBody = body,
            ReceivedAtUtc = clock.GetUtcNow().UtcDateTime,
            RemoteIpHash = HashIp(HttpContext.Connection.RemoteIpAddress?.ToString()),
        };

        Result<InboundDispatchResult> result = await dispatcher.DispatchAsync(request, ct);
        // Generic-only response: a status code, no internal detail, no problem-details JSON.
        return StatusCode(
            result.IsFailure ? StatusCodes.Status500InternalServerError : result.Value.HttpStatus
        );
    }

    private static string HashIp(string? ip) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ip ?? string.Empty)));
}
