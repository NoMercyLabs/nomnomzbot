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

namespace NomNomzBot.Api.RateLimiting;

/// <summary>
/// Named rate-limit policy tiers (S114). One generic "api" bucket shared by every action was the bug:
/// a burst of cheap config toggles competed with the same caller's background dashboard polling for the
/// same budget and could 429 a harmless toggle. Each tier below is its own bucket so a cheap write never
/// contends with an expensive one, and a read never contends with a write.
/// </summary>
public static class RateLimitPolicyNames
{
    /// <summary>Authenticated GET/HEAD reads — generous, partitioned per user (falls back to IP).</summary>
    public const string Read = "read";

    /// <summary>Cheap authenticated writes (toggles, config, small CRUD) — generous, partitioned per user.</summary>
    public const string WriteCheap = "write-cheap";

    /// <summary>Expensive authenticated writes (synthesis, uploads, fan-out sends) — partitioned per channel
    /// so one tenant's heavy action cannot starve another tenant sharing the caller's account.</summary>
    public const string WriteExpensive = "write-expensive";

    /// <summary>Login / credential-exchange endpoints — strict, partitioned per IP (brute-force protection).</summary>
    public const string Auth = "auth";

    /// <summary>Session renewal (<c>POST auth/refresh</c>) — generous, partitioned per IP. The refresh token is a
    /// random httpOnly cookie, so the strict <see cref="Auth"/> brute-force budget protects nothing there.</summary>
    public const string Session = "session";

    /// <summary>Device Code Flow polling — generous, partitioned per IP (legitimate ~5s poll cadence).</summary>
    public const string DevicePoll = "device-poll";

    /// <summary>Unauthenticated public surfaces (overlays, webhooks, song-request) — partitioned per IP.</summary>
    public const string Anonymous = "anonymous";

    /// <summary>The public Read Aloud voice — tighter than <see cref="Anonymous"/> because a clip that is not
    /// cached costs a real synthesis; partitioned per IP.</summary>
    public const string ReadAloud = "read-aloud";

    /// <summary>Platform-admin reads/non-destructive writes — partitioned per principal.</summary>
    public const string Admin = "admin";

    /// <summary>A viewer's own chat send — Twitch's 20 per 30 s, partitioned per user and channel.</summary>
    public const string ViewerChatSend = "viewer-chat-send";
}
