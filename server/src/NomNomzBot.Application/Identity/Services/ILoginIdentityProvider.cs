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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// One device-flow login provider's OAuth mechanics (platform-identity §3.2), keyed by <see cref="Key"/>
/// (an <c>AuthEnums.LoginProvider</c> value). The generic <c>auth/{provider}/device[/poll]</c> routes dispatch
/// here; a successful poll yields an <see cref="ExternalIdentityProof"/> that <see cref="IExternalLoginService"/>
/// turns into a session. Auth-code + PKCE providers (Kick/Twitter) use a separate contract.
/// </summary>
public interface ILoginIdentityProvider
{
    /// <summary>The provider key this implementation serves (e.g. <c>youtube</c>).</summary>
    string Key { get; }

    /// <summary>Begin the device authorization grant: returns the user code + verification URL to display.</summary>
    Task<Result<DeviceCodeStartDto>> StartDeviceAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Poll the device grant once. On approval, returns the proven identity (tokens already vaulted). While
    /// pending, returns a failure whose <c>ErrorCode</c> is a <see cref="DeviceLoginStatus"/> value
    /// (<c>pending</c> / <c>slow_down</c> keep polling; <c>expired</c> / <c>denied</c> / <c>error</c> terminal).
    /// </summary>
    Task<Result<ExternalIdentityProof>> PollDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken = default
    );
}
