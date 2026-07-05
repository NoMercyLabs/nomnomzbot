// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The Discord interactions webhook — the URL registered as the application's Interactions Endpoint URL in the
/// Discord Developer Portal. <c>[AllowAnonymous]</c> is the explicit gate here (Discord calls unauthenticated;
/// the endpoint-authorization invariant counts it as gated): security is the MANDATORY Ed25519 verification of
/// <c>X-Signature-Ed25519</c> over <c>X-Signature-Timestamp + raw body bytes</c> against the app public key —
/// any failure answers 401 before the body is parsed, because Discord probes the endpoint with invalid
/// signatures at registration and routinely afterwards. The raw bytes are read straight off the request stream
/// (no model binding), so the signature covers exactly what Discord sent. Unconfigured
/// <c>Discord:PublicKey</c> → 503 (feature not configured — graceful, never a crash). The verified payload is
/// routed by <see cref="IDiscordInteractionService"/> (PING→PONG handshake, opt-in button toggles), whose
/// interaction-response JSON is returned verbatim within Discord's 3-second deadline.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/discord/interactions")]
[AllowAnonymous]
[Tags("Discord")]
public class DiscordInteractionsController : ControllerBase
{
    /// <summary>Interaction payloads are small JSON; anything bigger is not Discord.</summary>
    private const int MaxBodyBytes = 256 * 1024;
    private const string SignatureHeader = "X-Signature-Ed25519";
    private const string TimestampHeader = "X-Signature-Timestamp";

    private readonly IDiscordInteractionVerifier _verifier;
    private readonly IDiscordInteractionService _interactions;

    public DiscordInteractionsController(
        IDiscordInteractionVerifier verifier,
        IDiscordInteractionService interactions
    )
    {
        _verifier = verifier;
        _interactions = interactions;
    }

    /// <summary>Receive one Discord interaction: verify the Ed25519 signature over the raw bytes (401 on any failure), then route and answer with the interaction-response JSON.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!_verifier.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);

        string signature = Request.Headers[SignatureHeader].ToString();
        string timestamp = Request.Headers[TimestampHeader].ToString();
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp))
            return Unauthorized();

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

        if (!_verifier.Verify(signature, timestamp, body))
            return Unauthorized();

        Result<string> reply = await _interactions.HandleAsync(Encoding.UTF8.GetString(body), ct);
        if (reply.IsFailure)
            return BadRequest();

        return Content(reply.Value, "application/json");
    }
}
