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
using NomNomzBot.Application.Contracts.Billing;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Stripe inbound webhooks (monetization-billing.md §5.2). Anonymous — authenticated by the HMAC signature in the
/// <c>Stripe-Signature</c> header against the webhook secret (invalid → 400, unconfigured → 503). The handler
/// owns verification + dispatch.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/billing/webhooks/stripe")]
[AllowAnonymous]
[Tags("Billing")]
public class BillingWebhookController(IStripeWebhookHandler handler) : BaseController
{
    /// <summary>Receive a Stripe webhook event, passing the raw payload and Stripe-Signature header to the handler for HMAC verification and dispatch.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        string payload;
        using (StreamReader reader = new(Request.Body, Encoding.UTF8, leaveOpen: true))
            payload = await reader.ReadToEndAsync(ct);
        string signature = Request.Headers["Stripe-Signature"].ToString();
        return ResultResponse(await handler.HandleAsync(payload, signature, ct));
    }
}
