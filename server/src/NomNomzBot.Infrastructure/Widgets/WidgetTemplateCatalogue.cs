// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// The starter templates offered when creating a new custom widget — working, SDK-using HTML so a new widget is
/// never a blank editor. All are <c>vanilla</c> (browser-ready, zero build dependency); they call the overlay SDK
/// global (<c>window.NomNomz</c>) the host injects. React/Vue/Svelte starters follow with the framework build.
/// </summary>
public static class WidgetTemplateCatalogue
{
    private const string Blank = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
          html, body { margin: 0; height: 100%; overflow: hidden; background: transparent;
            font-family: system-ui, -apple-system, sans-serif; color: #fff; }
          #app { display: grid; place-items: center; height: 100vh; text-align: center; }
          h1 { font-size: 42px; margin: 0; text-shadow: 0 2px 8px rgba(0,0,0,.5); }
        </style>
        </head>
        <body>
          <div id="app"><h1>Your widget is live 🎉</h1></div>
          <script>
            // The NomNomz overlay SDK is a global. A few things you can do:
            //   NomNomz.on('follow', function (d) { /* d.user */ });
            //   NomNomz.on('cheer',  function (d) { /* d.user, d.amount */ });
            //   NomNomz.onAny(function (type, d) { /* every event */ });
            //   NomNomz.onSettings(function (s) { /* dashboard settings changed */ });
            // Edit this code, hit Save, and the overlay hot-reloads automatically.
            var app = document.getElementById('app');
            NomNomz.on('follow', function (d) {
              app.innerHTML = '<h1>💜 ' + (d.user || 'Someone') + ' followed!</h1>';
            });
          </script>
        </body>
        </html>
        """;

    private const string Alerts = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
          html, body { margin: 0; height: 100%; overflow: hidden; background: transparent;
            font-family: system-ui, sans-serif; }
          #alert { position: fixed; top: 14%; left: 50%; transform: translate(-50%, -20px) scale(.96);
            min-width: 280px; max-width: 70vw; padding: 22px 40px; text-align: center; border-radius: 16px;
            background: rgba(12,12,18,.9); border: 2px solid var(--accent, #9146ff);
            box-shadow: 0 8px 40px rgba(0,0,0,.5); color: #fff; opacity: 0;
            transition: opacity .35s ease, transform .35s ease; }
          #alert.show { opacity: 1; transform: translate(-50%, 0) scale(1); }
          #alert .title { font-size: 28px; font-weight: 800; color: var(--accent, #9146ff); }
          #alert .detail { font-size: 18px; margin-top: 6px; opacity: .85; }
        </style>
        </head>
        <body>
          <div id="alert"><div class="title"></div><div class="detail"></div></div>
          <script>
            var box = document.getElementById('alert');
            var title = box.querySelector('.title');
            var detail = box.querySelector('.detail');
            var queue = [], busy = false, DURATION = 6000;

            NomNomz.onSettings(function (s) {
              if (s.accentColor) document.documentElement.style.setProperty('--accent', s.accentColor);
              var d = Number(s.durationMs);
              if (d >= 1000 && d <= 60000) DURATION = d;
            });

            function show(t, d) { queue.push([t, d]); if (!busy) next(); }
            function next() {
              var item = queue.shift();
              if (!item) { busy = false; return; }
              busy = true;
              title.textContent = item[0];
              detail.textContent = item[1] || '';
              detail.style.display = item[1] ? 'block' : 'none';
              box.classList.add('show');
              setTimeout(function () { box.classList.remove('show'); setTimeout(next, 450); }, DURATION);
            }

            NomNomz.on('follow', function (d) { show((d.user || 'Someone') + ' followed!', ''); });
            NomNomz.on('subscription', function (d) { show((d.user || 'Someone') + ' subscribed!', ''); });
            NomNomz.on('resub', function (d) { show((d.user || 'Someone') + ' resubscribed!', (d.months || 0) + ' months'); });
            NomNomz.on('gift', function (d) { show((d.user || 'Someone') + ' gifted ' + (d.amount || 1) + ' subs!', ''); });
            NomNomz.on('cheer', function (d) { show((d.user || 'Someone') + ' cheered ' + (d.amount || 0) + ' bits!', ''); });
            NomNomz.on('raid', function (d) { show((d.user || 'Someone') + ' is raiding!', (d.viewers || 0) + ' viewers'); });
          </script>
        </body>
        </html>
        """;

    private const string Label = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
          html, body { margin: 0; height: 100%; overflow: hidden; background: transparent;
            font-family: system-ui, sans-serif; color: #fff; }
          #label { position: fixed; left: 16px; bottom: 16px; padding: 8px 18px; border-radius: 999px;
            background: rgba(12,12,18,.85); border: 1px solid var(--accent, #9146ff); font-size: 16px;
            white-space: nowrap; }
          #label b { color: var(--accent, #9146ff); }
        </style>
        </head>
        <body>
          <div id="label"><span class="text">Latest follower:</span> <b class="value">-</b></div>
          <script>
            var textEl = document.querySelector('#label .text');
            var valueEl = document.querySelector('#label .value');
            NomNomz.onSettings(function (s) {
              if (s.accentColor) document.documentElement.style.setProperty('--accent', s.accentColor);
              if (typeof s.label === 'string') textEl.textContent = s.label;
            });
            // Update the value on each new follower — swap the event to track a different stat.
            NomNomz.on('follow', function (d) { valueEl.textContent = d.user || '-'; });
          </script>
        </body>
        </html>
        """;

    public static readonly IReadOnlyList<WidgetTemplate> All =
    [
        new WidgetTemplate(
            "blank",
            "Blank widget",
            "A minimal starting point that greets your newest follower — the place to build anything from.",
            "vanilla",
            Blank
        ),
        new WidgetTemplate(
            "alerts",
            "Alert box",
            "A centered, animated alert card for follows, subs, resubs, gifts, cheers, and raids.",
            "vanilla",
            Alerts
        ),
        new WidgetTemplate(
            "label",
            "Stat label",
            "A small corner label — shows your latest follower out of the box; retarget it to any stat.",
            "vanilla",
            Label
        ),
    ];
}
