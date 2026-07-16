// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Application.Contracts.Kick;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Kick inbound event webhooks (slice 3b-2c-2; this URL is the one configured on the Kick app in the
/// developer dashboard). Anonymous — authenticated by Kick's RSA signature over
/// <c>{message-id}.{timestamp}.{body}</c> (webhook-security, verified 2026-07-11) plus a freshness
/// window on the signed timestamp as replay protection. Unhandled event types acknowledge 200 so Kick
/// never retries them; a bad signature is 401; event-type routing lives in the ingest.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks/kick")]
[AllowAnonymous]
[Tags("Webhooks")]
public class KickWebhookController : BaseController
{
    /// <summary>Replay window on the SIGNED timestamp — generous enough for Kick's retry backoff,
    /// tight enough that a captured delivery cannot be replayed much later.</summary>
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(10);

    private readonly IKickWebhookVerifier _verifier;
    private readonly IKickWebhookIngest _ingest;
    private readonly TimeProvider _clock;

    public KickWebhookController(
        IKickWebhookVerifier verifier,
        IKickWebhookIngest ingest,
        TimeProvider clock
    )
    {
        _verifier = verifier;
        _ingest = ingest;
        _clock = clock;
    }

    /// <summary>Receive a Kick event delivery: verify the RSA signature + timestamp freshness, then dispatch by event type.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        string messageId = Request.Headers["Kick-Event-Message-Id"].ToString();
        string timestamp = Request.Headers["Kick-Event-Message-Timestamp"].ToString();
        string signature = Request.Headers["Kick-Event-Signature"].ToString();
        string eventType = Request.Headers["Kick-Event-Type"].ToString();
        if (
            string.IsNullOrEmpty(messageId)
            || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(signature)
        )
            return Unauthorized();

        // Replay protection: the timestamp is INSIDE the signed payload, so an attacker cannot forward
        // an old capture with a fresh timestamp — rejecting stale ones closes the replay window.
        if (
            !DateTimeOffset.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset sentAt
            )
            || (_clock.GetUtcNow() - sentAt).Duration() > FreshnessWindow
        )
            return Unauthorized();

        string body;
        using (StreamReader reader = new(Request.Body, Encoding.UTF8, leaveOpen: true))
            body = await reader.ReadToEndAsync(ct);

        if (!await _verifier.VerifyAsync(messageId, timestamp, body, signature, ct))
            return Unauthorized();

        // The ingest owns the event-type routing; a type without a consumer is a no-op there. Either
        // way the delivery acknowledges 200 — it IS an authenticated Kick delivery, and retrying an
        // event we deliberately ignore would change nothing.
        await _ingest.HandleAsync(eventType, body, ct);

        return Ok();
    }
}
