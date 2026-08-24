// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
//
// Local hot-reload dev loop. The web build is single-origin (main.kt: baseUrl = window.location.origin),
// so when the browser dev server serves the app at http://localhost:5173 the app also sends /api + /hubs
// there. This forwards those to the live dev backend so a hot-reloading local build runs against REAL data
// (emotes, chat, channels) with no CORS wall — the browser only ever talks to its own origin, webpack does
// the cross-origin hop server-side. Only the two backend prefixes are proxied; the app's own JS + compose
// resources keep being served locally, so a Kotlin edit hot-reloads in seconds instead of a 25-min image build.
//
// Auth: mint a session and open http://localhost:5173/#access_token=<jwt>&expires_in=3600 (the OAuth-return
// arm in main.kt bootstraps the session from that fragment). Point NNZ_DEV_BACKEND elsewhere (e.g. a remote
// NoMercy-hosted instance) only if you explicitly want that; the default targets your OWN local API.
//
// Local dev port layout (see start.sh / CLAUDE.md): dashboard dev server = 5173 (build.gradle.kts),
// API = 5080 — its committed, documented default (no appsettings.Development.json edit needed). The
// two run side by side with zero flags/env vars — no collision.

const target = process.env.NNZ_DEV_BACKEND || "http://localhost:5080";

// changeOrigin rewrites the Host header to the TARGET (5080), so ResolvePublicOrigin on the API side sees
// 5080 and hands the owner a redirect URI he never actually browses (5173). Attach X-Forwarded-Host/Proto
// from the ORIGINAL browser request so the API's forwarded-header resolution reports the origin the owner
// is really on, matching every other reverse-proxy deployment (Cloudflare Tunnel, Proxmox) this resolver
// already supports.
config.devServer = config.devServer || {};
config.devServer.proxy = [
    {
        context: ["/api", "/hubs"],
        target: target,
        changeOrigin: true,
        secure: true,
        ws: true,
        // The installed http-proxy-middleware is 2.x (webpack-dev-server 4.x's dependency), whose hook is the
        // top-level `onProxyReq` option — the nested `on: { proxyReq }` shape is a v3+/webpack-dev-server-5-only
        // API and is silently ignored here (verified live: without this exact shape the header never reaches
        // the API and ResolvePublicOrigin kept reporting :5080).
        onProxyReq: (proxyReq, req) => {
            proxyReq.setHeader("X-Forwarded-Host", req.headers.host);
            proxyReq.setHeader("X-Forwarded-Proto", "http");
        },
    },
];
