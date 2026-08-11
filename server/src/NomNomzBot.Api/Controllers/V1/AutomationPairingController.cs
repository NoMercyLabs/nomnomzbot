// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Device pairing, management side (stream-deck.md §4): the dashboard mints a short-lived single-use
/// code under the caller's channel. The device's anonymous redeem lives on the data plane
/// (<c>POST /automation/v1/pair</c>, <see cref="AutomationDataController.RedeemPairing"/>). The
/// paired device then appears in the normal token list; revoking it unpairs (D3).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/automation")]
[Authorize]
[Tags("Automation")]
public class AutomationPairingController(
    IAutomationPairingService pairing,
    ICurrentTenantService currentTenant,
    ICurrentUserService currentUser
) : BaseController
{
    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);

    /// <summary>Mint a pairing code for the caller's channel (single-use, ~5 minutes).</summary>
    [HttpPost("pair-codes")]
    [RequireAction("automation:tokens:write")]
    [ProducesResponseType<StatusResponseDto<PairingCodeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MintCode(
        [FromBody] MintPairingCodeRequest request,
        CancellationToken ct
    )
    {
        if (currentTenant.BroadcasterId is not Guid broadcasterId)
            return BadRequestResponse("No channel resolved for this request.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await pairing.MintCodeAsync(broadcasterId, caller, request, ct));
    }

    /// <summary>
    /// Device-initiated pairing, step 2 (stream-deck.md D9): the human-friendly landing page a device's
    /// verification URL points at. <c>[AllowAnonymous]</c> on the class-level JWT auth so a logged-out
    /// visitor gets a friendly "log in first" page instead of a bare 401 — checks
    /// <c>User.Identity.IsAuthenticated</c> itself rather than relying on the automatic challenge.
    /// </summary>
    [HttpGet("pair/device/approve")]
    [AllowAnonymous]
    public IActionResult ApproveDevicePage([FromQuery] string? code) =>
        Content(
            DevicePairingHtml.ApprovalPage(User.Identity?.IsAuthenticated ?? false, code),
            "text/html"
        );

    /// <summary>Device-initiated pairing, step 2b: the approval page's form POSTs here.</summary>
    [HttpPost("pair/device/approve")]
    [RequireAction("automation:tokens:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveDevice(
        [FromBody] ApproveDeviceRequest request,
        CancellationToken ct
    )
    {
        if (currentTenant.BroadcasterId is not Guid broadcasterId)
            return BadRequestResponse("No channel resolved for this request.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        Result result = await pairing.ApproveDeviceAsync(
            broadcasterId,
            caller,
            request.UserCode,
            ct
        );
        return ResultResponse(result);
    }
}

/// <summary>
/// The device-pairing approval page's markup (<see cref="AutomationPairingController.ApproveDevicePage"/>).
/// A plain server-rendered page, not the Compose/Wasm dashboard: the JWT access token never rides an
/// ambient cookie the way the refresh token does (identity-auth.md — web keeps ONLY the refresh token
/// in an HttpOnly cookie), so this page's own inline JS exchanges that refresh cookie for a fresh access
/// token via <c>POST /api/v1/auth/refresh?client=web</c> before calling the approve endpoint itself.
/// </summary>
internal static class DevicePairingHtml
{
    public static string ApprovalPage(bool isAuthenticated, string? code)
    {
        string safeCode = System.Net.WebUtility.HtmlEncode(code ?? "");
        string codeJs = System.Text.Json.JsonSerializer.Serialize(code ?? "");
        return $$"""
            <!DOCTYPE html><html><head><meta charset="utf-8"><title>Approve device — NomNomzBot</title>
            <style>
            body{background:#141125;color:#f4f5fa;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}
            .card{background:#1A1530;border:1px solid #1e1a35;border-radius:16px;padding:48px;text-align:center;max-width:420px}
            h1{font-size:24px;margin:0 0 8px}p{color:#8889a0;font-size:14px;margin:0 0 16px}
            .code{font-family:monospace;font-size:20px;letter-spacing:2px;color:#a78bfa;margin:0 0 24px}
            button{background:#7c3aed;color:#fff;border:none;border-radius:8px;padding:12px 24px;font-size:15px;cursor:pointer}
            button:disabled{opacity:.5;cursor:default}
            .status{margin-top:16px;font-size:14px}
            </style></head><body>
            <div class="card">
              <h1 id="title">Approve this device?</h1>
              <p id="subtitle">A NomNomzBot Stream Deck plugin wants to pair with your channel.</p>
              <p class="code">{{safeCode}}</p>
              <button id="approve" disabled>Checking your login…</button>
              <div class="status" id="status"></div>
            </div>
            <script>
              var code = {{codeJs}};
              var accessToken = null;
              var btn = document.getElementById('approve');
              var status = document.getElementById('status');

              async function tryAuth() {
                try {
                  var res = await fetch('/api/v1/auth/refresh?client=web', { method: 'POST', credentials: 'include' });
                  if (!res.ok) throw new Error('not logged in');
                  var body = await res.json();
                  accessToken = body.data.accessToken;
                  btn.disabled = !code;
                  btn.textContent = code ? 'Approve' : 'Missing code';
                } catch (e) {
                  btn.textContent = 'Log in first';
                  status.textContent = 'Open your NomNomzBot dashboard in this browser, log in, then reload this page.';
                }
              }

              btn.addEventListener('click', async function () {
                btn.disabled = true;
                btn.textContent = 'Approving…';
                try {
                  var res = await fetch('/api/v1/automation/pair/device/approve', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + accessToken },
                    body: JSON.stringify({ userCode: code }),
                  });
                  var body = await res.json();
                  if (res.ok && body.status !== 'error') {
                    document.getElementById('title').textContent = 'Device approved';
                    document.getElementById('subtitle').textContent = 'You can close this tab and return to your Stream Deck.';
                    btn.remove();
                  } else {
                    status.textContent = body.message || 'Approval failed — the code may have expired.';
                    btn.disabled = false;
                    btn.textContent = 'Try again';
                  }
                } catch (e) {
                  status.textContent = 'Network error — try again.';
                  btn.disabled = false;
                  btn.textContent = 'Try again';
                }
              });

              tryAuth();
            </script>
            </body></html>
            """;
    }
}
