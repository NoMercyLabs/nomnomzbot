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
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.RateLimiting;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Lightweight OAuth relay page. Integration and bot-connect callbacks bounce through here so
/// popup windows can signal the parent tab and close without loading the full dashboard.
/// <para>
/// When opened as a popup (<c>window.opener</c> is set): sends a <c>postMessage</c> to the
/// opener with the query-string params as the payload, then closes itself.
/// When opened without an opener (full-page redirect fallback): continues to the <c>?return=</c>
/// path (same-origin paths only) so the dashboard picks up the markers.
/// </para>
/// <para>
/// A callback that failed carries <c>error</c> or a <c>*_error</c> param; the page then says so
/// instead of "Connected". The page follows the dashboard's dark tokens (tokens.css) and uses
/// no external assets, so it renders at once.
/// </para>
/// </summary>
[ApiController]
[Route("oauth-relay")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicyNames.Anonymous)]
public class OAuthRelayController : ControllerBase
{
    private const string Html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <meta name="color-scheme" content="dark light" />
          <title>NomNomzBot</title>
          <style>
            :root {
              --background: oklch(0.177638 0 0);
              --foreground: oklch(0.949119 0 0);
              --card: oklch(0.213423 0 0);
              --border: oklch(0.239292 0 0);
              --muted-foreground: oklch(0.773104 0 0);
              --success: oklch(0.795095 0.234563 145.534);
              --destructive: oklch(0.710627 0.166148 22.216);
              --primary: oklch(0.692953 0.114282 309.045);
            }
            @media (prefers-color-scheme: light) {
              :root {
                --background: oklch(1 0 0);
                --foreground: oklch(0.145 0 0);
                --card: oklch(1 0 0);
                --border: oklch(0.922 0 0);
                --muted-foreground: oklch(0.5 0 0);
                --success: oklch(0.627 0.194 142.5);
                --destructive: oklch(0.577 0.245 27.325);
                --primary: oklch(0.205 0 0);
              }
            }
            * { box-sizing: border-box; }
            body {
              margin: 0; min-height: 100vh; padding: 16px;
              display: flex; align-items: center; justify-content: center;
              background: var(--background); color: var(--foreground);
              font-family: 'Plus Jakarta Sans Variable', 'Plus Jakarta Sans', system-ui, sans-serif;
            }
            .card {
              width: 100%; max-width: 360px; padding: 28px 24px; text-align: center;
              background: var(--card); border: 1px solid var(--border); border-radius: 20px;
            }
            .logo { width: 28px; height: 28px; margin: 0 auto 20px; color: var(--muted-foreground); }
            .state {
              width: 48px; height: 48px; margin: 0 auto 16px; border-radius: 50%;
              display: flex; align-items: center; justify-content: center;
              color: var(--tone); background: color-mix(in oklch, var(--tone) 15%, transparent);
            }
            .state svg { width: 24px; height: 24px; }
            h1 { margin: 0 0 6px; font-size: 18px; font-weight: 600; }
            p { margin: 0; font-size: 14px; line-height: 1.5; color: var(--muted-foreground); }
            a.back {
              display: inline-block; margin-top: 20px; padding: 8px 16px; border-radius: 16px;
              background: var(--primary); color: var(--background);
              font-size: 14px; font-weight: 600; text-decoration: none;
            }
            [hidden] { display: none; }
          </style>
        </head>
        <body>
          <main class="card">
            <svg class="logo" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"
                 stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
              <path d="M22 12V15M2 12V15M9 8H7C5.34315 8 4 9.34315 4 11V16C4 17.6569 5.34315 19 7 19H10L12 21L14 19H17C18.6569 19 20 17.6569 20 16V11C20 9.34315 18.6569 8 17 8H15M9 8L8 5M9 8H15M8 5C8.55228 5 9 4.55228 9 4C9 3.44772 8.55228 3 8 3C7.44772 3 7 3.44772 7 4C7 4.55228 7.44772 5 8 5ZM15 8L16 5M16 5C16.5523 5 17 4.55228 17 4C17 3.44772 16.5523 3 16 3C15.4477 3 15 3.44772 15 4C15 4.55228 15.4477 5 16 5ZM10 14V13.5C10 12.6716 9.32843 12 8.5 12C7.67157 12 7 12.6716 7 13.5V14M17 14V13.5C17 12.6716 16.3284 12 15.5 12C14.6716 12 14 12.6716 14 13.5V14" />
            </svg>
            <div class="state" id="state" style="--tone: var(--success)">
              <svg id="ok" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
                   stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5" /></svg>
              <svg id="bad" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
                   stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" hidden><path d="M6 6l12 12M18 6L6 18" /></svg>
            </div>
            <h1 id="title" role="status">Connected</h1>
            <p id="detail">You can close this window.</p>
            <a class="back" id="back" href="/" hidden>Back to the dashboard</a>
          </main>
          <script>
            (function () {
              var params = Object.fromEntries(new URLSearchParams(window.location.search));
              var failed = Object.keys(params).some(function (key) {
                return key === 'error' || /_error$/.test(key);
              });
              var back = params['return'];
              var safeBack = back && back.charAt(0) === '/' && back.charAt(1) !== '/' ? back : '/';

              if (failed) {
                document.getElementById('state').style.setProperty('--tone', 'var(--destructive)');
                // SVG elements have no hidden property, so the attribute is set directly.
                document.getElementById('ok').setAttribute('hidden', '');
                document.getElementById('bad').removeAttribute('hidden');
                document.getElementById('title').textContent = 'That did not work';
                document.getElementById('detail').textContent = window.opener
                  ? 'This window closes. The dashboard shows what went wrong.'
                  : 'The dashboard shows what went wrong.';
              }

              if (window.opener) {
                try {
                  window.opener.postMessage(
                    Object.assign({ type: 'oauth_relay' }, params),
                    window.location.origin
                  );
                } catch (_) {}
                // The dashboard must never load in this window: it would refresh the sign-in at the same time as
                // the window that opened it, and the two refreshes sign the person out. A window the browser
                // will not close just stays, and says so.
                var leave = function () {
                  window.close();
                  setTimeout(function () {
                    document.getElementById('detail').textContent = 'You can close this window now.';
                    link.hidden = true;
                  }, 400);
                };
                var link = document.getElementById('back');
                link.href = safeBack;
                link.textContent = 'Close this window';
                link.hidden = false;
                link.addEventListener('click', function (event) {
                  event.preventDefault();
                  leave();
                });
                setTimeout(leave, failed ? 1500 : 900);
              } else {
                window.location.replace(safeBack);
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
