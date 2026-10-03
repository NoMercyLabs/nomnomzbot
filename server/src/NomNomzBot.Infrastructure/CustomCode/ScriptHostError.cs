// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Why the LAST host call of a script failed: a stable machine <see cref="Code"/> (one of
/// <see cref="ScriptHostErrorCodes"/>) plus a plain <see cref="Message"/> for the script author. The guest reads
/// it through the <see cref="ScriptHostErrorCodes.LastErrorKey"/> host call.
/// </summary>
public sealed record ScriptHostError(string Code, string Message)
{
    public string ToJson() => JsonConvert.SerializeObject(new { code = Code, message = Message });

    /// <summary>Maps a failed service <see cref="Result"/> onto the stable guest-facing codes.</summary>
    public static ScriptHostError FromResult(Result failure) =>
        new(
            failure.ErrorCode switch
            {
                "NOT_FOUND" => ScriptHostErrorCodes.NotFound,
                "VALIDATION_FAILED" => ScriptHostErrorCodes.InvalidArgument,
                "RATE_LIMITED" => ScriptHostErrorCodes.RateLimited,
                "LIMIT_EXCEEDED" or "QUOTA_EXCEEDED" => ScriptHostErrorCodes.LimitExceeded,
                "FORBIDDEN" => ScriptHostErrorCodes.Refused,
                _ => ScriptHostErrorCodes.UpstreamFailed,
            },
            failure.ErrorMessage ?? "The host call failed."
        );
}

/// <summary>The stable, documented error codes a script can branch on.</summary>
public static class ScriptHostErrorCodes
{
    /// <summary>The host call that reads the last error (JSON <c>{code,message}</c>, or null after a success).</summary>
    public const string LastErrorKey = "last.error";

    public const string InvalidArgument = "invalid_argument";
    public const string NotFound = "not_found";
    public const string Refused = "refused";
    public const string RateLimited = "rate_limited";
    public const string LimitExceeded = "limit_exceeded";
    public const string UpstreamFailed = "upstream_failed";
}
