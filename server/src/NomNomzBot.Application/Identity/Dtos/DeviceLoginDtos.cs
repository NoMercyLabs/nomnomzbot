// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>
/// A started Device Code Flow authorization (the no-secret login). The client shows <see cref="UserCode"/> and
/// sends the operator to <see cref="VerificationUri"/>, then polls with <see cref="DeviceCode"/> every
/// <see cref="Interval"/> seconds until <see cref="ExpiresIn"/> elapses.
/// </summary>
public sealed record DeviceCodeStartDto(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int Interval,
    int ExpiresIn
);

/// <summary>One streamer device-login poll: the loop <see cref="Status"/>, plus the issued auth on "authorized".</summary>
public sealed record DeviceLoginPollDto(string Status, AuthResultDto? Auth = null);

/// <summary>One bot device-login poll: the loop <see cref="Status"/>, plus the bot connection on "authorized".</summary>
public sealed record DeviceBotPollDto(string Status, BotStatusDto? Bot = null);

/// <summary>
/// The poll status the client loops on. <c>pending</c>/<c>slow_down</c> → keep polling (back off on slow_down);
/// <c>authorized</c>/<c>expired</c>/<c>denied</c>/<c>error</c> are terminal. Mirrors the transport's
/// <c>DevicePollStatus</c>, surfaced as wire strings.
/// </summary>
public static class DeviceLoginStatus
{
    public const string Authorized = "authorized";
    public const string Pending = "pending";
    public const string SlowDown = "slow_down";
    public const string Expired = "expired";
    public const string Denied = "denied";
    public const string Error = "error";
}

/// <summary>The body of a device-login poll request: the opaque device code from the start call.</summary>
public sealed record DevicePollRequest(string DeviceCode);
