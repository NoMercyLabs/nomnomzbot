// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

    /// <summary>Device Code Flow polling — generous, partitioned per IP (legitimate ~5s poll cadence).</summary>
    public const string DevicePoll = "device-poll";

    /// <summary>Unauthenticated public surfaces (overlays, webhooks, song-request) — partitioned per IP.</summary>
    public const string Anonymous = "anonymous";

    /// <summary>Platform-admin reads/non-destructive writes — partitioned per principal.</summary>
    public const string Admin = "admin";
}
