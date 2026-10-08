// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Moderation.MassBan;

/// <summary>
/// Which failed ban calls are worth another try, and how long to wait. A rate limit (429), a server error (5xx) and
/// a transport failure say nothing about the account, so the call is repeated; every other answer (a refusal, a
/// missing scope, a rejected token) would come back the same, so it is final.
/// </summary>
internal static partial class MassBanRetryPolicy
{
    /// <summary>The ban call is made at most this many times for one account.</summary>
    public const int MaxAttempts = 5;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);

    /// <summary>True when the failure is a 429, a 5xx or a transport failure.</summary>
    public static bool IsTransient(Result failure)
    {
        if (failure.ErrorCode is TwitchErrorCodes.RateLimited or TwitchErrorCodes.Transport)
            return true;

        Match status = StatusInMessage().Match(failure.ErrorMessage ?? string.Empty);
        return status.Success && int.Parse(status.Groups[1].Value) is >= 500 and <= 599;
    }

    /// <summary>The wait after the <paramref name="attempts"/>-th failed call: 30 s, 1 min, 2 min, 4 min.</summary>
    public static TimeSpan DelayAfter(int attempts) => BaseDelay * Math.Pow(2, attempts - 1);

    [GeneratedRegex(@"\((\d{3})\)")]
    private static partial Regex StatusInMessage();
}
