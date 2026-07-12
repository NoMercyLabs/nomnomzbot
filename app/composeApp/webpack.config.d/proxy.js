// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
//
// Local hot-reload dev loop. The web build is single-origin (main.kt: baseUrl = window.location.origin),
// so when the browser dev server serves the app at http://localhost:5080 the app also sends /api + /hubs
// there. This forwards those to the live dev backend so a hot-reloading local build runs against REAL data
// (emotes, chat, channels) with no CORS wall — the browser only ever talks to its own origin, webpack does
// the cross-origin hop server-side. Only the two backend prefixes are proxied; the app's own JS + compose
// resources keep being served locally, so a Kotlin edit hot-reloads in seconds instead of a 25-min image build.
//
// Auth: mint a session and open http://localhost:5080/#access_token=<jwt>&expires_in=3600 (the OAuth-return
// arm in main.kt bootstraps the session from that fragment). Point NNZ_DEV_BACKEND elsewhere to target a
// different backend (e.g. a local `dotnet run` on another port).

const target = process.env.NNZ_DEV_BACKEND || "https://dev.nomnomz.bot";

config.devServer = config.devServer || {};
config.devServer.proxy = [
    {
        context: ["/api", "/hubs"],
        target: target,
        changeOrigin: true,
        secure: true,
        ws: true,
    },
];
