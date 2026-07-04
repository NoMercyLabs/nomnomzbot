// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Lightweight OAuth relay page. Integration and bot-connect callbacks bounce through here so
/// popup windows can signal the parent tab and close without loading the full Wasm app.
/// <para>
/// When opened as a popup (<c>window.opener</c> is set): sends a <c>postMessage</c> to the
/// opener with the query-string params as the payload, then auto-closes after 800 ms.
/// When opened without an opener (full-page redirect fallback): navigates to the <c>?return=</c>
/// param (the original connect-completion URL) so <c>readReturnedConnect()</c> in main.kt
/// picks up the markers as before.
/// </para>
/// </summary>
[Route("oauth-relay")]
[AllowAnonymous]
public class OAuthRelayController : ControllerBase
{
    private const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <title>Connected</title>
          <style>
            body {
              background: #141125; color: #f4f5fa;
              font-family: system-ui, sans-serif;
              display: flex; align-items: center; justify-content: center;
              min-height: 100vh; margin: 0;
            }
            .card { text-align: center; padding: 40px; }
            .icon {
              width: 64px; height: 64px; border-radius: 50%;
              background: rgba(74,222,128,.15);
              display: flex; align-items: center; justify-content: center;
              margin: 0 auto 20px; font-size: 32px; color: #4ade80;
            }
            h2 { margin: 0 0 8px; font-size: 20px; }
            p  { color: #8889a0; font-size: 14px; margin: 0; }
          </style>
        </head>
        <body>
          <div class="card">
            <div class="icon">&#x2713;</div>
            <h2>Connected</h2>
            <p>You can close this window.</p>
          </div>
          <script>
            (function () {
              const params = Object.fromEntries(new URLSearchParams(window.location.search));
              if (window.opener) {
                try {
                  window.opener.postMessage(
                    { type: 'oauth_relay', ...params },
                    window.location.origin
                  );
                } catch (_) {}
                setTimeout(function () { window.close(); }, 800);
              } else {
                // Non-popup fallback: navigate to the original target so main.kt picks it up.
                const ret = params['return'];
                window.location.href = ret ? decodeURIComponent(ret) : '/';
              }
            })();
          </script>
        </body>
        </html>
        """;

    /// <summary>Serve the relay page that posts the OAuth callback params to the opener window and auto-closes.</summary>
    [HttpGet]
    public IActionResult Get() => Content(Html, "text/html");
}
