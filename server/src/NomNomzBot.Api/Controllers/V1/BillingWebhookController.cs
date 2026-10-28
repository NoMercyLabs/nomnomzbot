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
