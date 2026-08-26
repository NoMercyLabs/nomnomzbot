// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
//
// Local hot-reload dev loop. The web build is single-origin (main.kt: baseUrl = window.location.origin),
// so when the browser dev server serves the app at http://localhost:5090 the app also sends /api + /hubs
// there. This forwards those to the live dev backend so a hot-reloading local build runs against REAL data
// (emotes, chat, channels) with no CORS wall — the browser only ever talks to its own origin, webpack does
// the cross-origin hop server-side. Only the two backend prefixes are proxied; the app's own JS + compose
// resources keep being served locally, so a Kotlin edit hot-reloads in seconds instead of a 25-min image build.
//
// Auth: mint a session and open http://localhost:5090/#access_token=<jwt>&expires_in=3600 (the OAuth-return
// arm in main.kt bootstraps the session from that fragment). Point NNZ_DEV_BACKEND elsewhere (e.g. a remote
// NoMercy-hosted instance) only if you want to pin one; otherwise the target is chosen automatically below.
//
// Local dev port layout (see start.sh / CLAUDE.md): dashboard dev server = 5090 (build.gradle.kts),
// API = 5080 — its committed, documented default (no appsettings.Development.json edit needed). The
// two run side by side with zero flags/env vars — no collision.

// Target selection. A backend dev runs the API locally and wants their own edits proxied; a frontend-only
// dev never runs `dotnet` at all, so hardcoding the local API left them with a 504 on every /api call and
// no way to work. So: use the local API when it is actually listening, otherwise fall back to the deployed
// dev backend. Both tracks get a working dev loop with zero flags, and the chosen target is printed so it is
// never a guess. NNZ_DEV_BACKEND still overrides both.
const LOCAL_API = "http://localhost:5080";
const DEPLOYED_API = "https://dev.nomnomz.bot";

function localApiIsListening() {
    try {
        require("child_process").execFileSync(
            process.execPath,
            [require("path").resolve(__dirname, "../../dev/probe-local-api.cjs")],
            { stdio: "ignore" }
        );
        return true;
    } catch {
        return false;
    }
}

const target = process.env.NNZ_DEV_BACKEND || (localApiIsListening() ? LOCAL_API : DEPLOYED_API);

console.log(
    "[nnz] dev proxy -> " + target + (target === DEPLOYED_API ? "  (nothing listening on 5080)" : "")
);

// No X-Forwarded-Host/Proto here on purpose: the OAuth redirect_uri must match the URI registered
// with the provider, which for local dev is the API's own origin (http://localhost:5080), not the
// dev-server port the browser happens to be on. changeOrigin alone is enough for the proxy itself.
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
