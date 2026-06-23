// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Auth;

/// <summary>
/// Twitch OAuth <em>Device Code Flow</em> (the no-secret public-client grant). NomNomzBot ships its own
/// public client id, so a self-host operator never registers a Twitch app or pastes a secret: the app asks
/// Twitch for a short user code, the operator approves it at <c>verification_uri</c>, and the app polls for the
/// issued tokens. Like <see cref="ITwitchAuthService.ExchangeCodeAsync"/>, this seam is pure HTTP — it acquires
/// the tokens and hands them back; the caller vaults and routes them (broadcaster vs bot). Refresh and revoke
/// stay on <see cref="ITwitchAuthService"/>: device-code tokens refresh identically to any other.
/// </summary>
public interface ITwitchDeviceCodeService
{
    /// <summary>
    /// Begin a device authorization: <c>POST https://id.twitch.tv/oauth2/device</c> with the shared client id
    /// and the requested <paramref name="scopes"/> — no client secret. Returns the user code, the verification
    /// URL to show the operator, the opaque device code to poll with, and the poll interval. Null when the
    /// client id is unconfigured (a fresh install with no shipped default and no BYOC override).
    /// </summary>
    Task<DeviceCodeResult?> RequestDeviceCodeAsync(
        IReadOnlyList<string> scopes,
        CancellationToken ct = default
    );

    /// <summary>
    /// Poll the token endpoint <em>once</em> for a pending device authorization. The caller drives the loop on
    /// the <see cref="DeviceCodeResult.Interval"/> (backing off on <see cref="DevicePollStatus.SlowDown"/>) so
    /// no request is held open server-side. On <see cref="DevicePollStatus.Authorized"/> the issued tokens ride
    /// the outcome for the caller to vault.
    /// </summary>
    Task<DevicePollOutcome> PollOnceAsync(
        string deviceCode,
        IReadOnlyList<string> scopes,
        CancellationToken ct = default
    );
}

/// <summary>
/// A started device authorization. <see cref="UserCode"/> is shown to the operator to enter at
/// <see cref="VerificationUri"/>; <see cref="DeviceCode"/> is the opaque handle the app polls with;
/// <see cref="Interval"/> is the minimum seconds between polls; <see cref="ExpiresAt"/> is when the code dies.
/// </summary>
public record DeviceCodeResult(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int Interval,
    DateTime ExpiresAt
);

/// <summary>One poll's result: the terminal/continuation status, plus the issued tokens when authorized.</summary>
public record DevicePollOutcome(DevicePollStatus Status, TokenResult? Tokens = null);

/// <summary>
/// The outcome of a single device-code token poll, mapped from Twitch's response: keep polling
/// (<see cref="Pending"/>), back off then keep polling (<see cref="SlowDown"/>), or stop —
/// <see cref="Authorized"/> (tokens issued), <see cref="Expired"/> (code timed out), <see cref="Denied"/>
/// (operator declined), or <see cref="Error"/> (unconfigured / malformed / transport failure).
/// </summary>
public enum DevicePollStatus
{
    Authorized,
    Pending,
    SlowDown,
    Expired,
    Denied,
    Error,
}
