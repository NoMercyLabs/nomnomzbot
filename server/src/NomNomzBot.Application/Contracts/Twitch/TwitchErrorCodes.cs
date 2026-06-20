// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The closed set of <c>Result.ErrorCode</c> values Helix calls fail with (twitch-helix.md §3). Controllers
/// translate these to problem-details status codes (<c>missing_scope</c>→403, <c>no_token</c>→409,
/// <c>rate_limited</c>→429, <c>not_found</c>→404, others→502/500). One source of truth — no string literals.
/// </summary>
public static class TwitchErrorCodes
{
    public const string NoToken = "no_token";
    public const string MissingScope = "missing_scope";
    public const string Unauthorized = "unauthorized";
    public const string RateLimited = "rate_limited";
    public const string NotFound = "not_found";
    public const string TwitchError = "twitch_error";
    public const string Transport = "transport";
}
