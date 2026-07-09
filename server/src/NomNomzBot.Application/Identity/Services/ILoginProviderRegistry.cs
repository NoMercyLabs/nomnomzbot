// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// The registry of login-provider descriptors (platform-identity §3.2). Twitch is always on; YouTube and Kick
/// are registered with their descriptors but feature-flagged off until their chat/API seams ship — so the
/// login screen never shows a dead button and no rewrite is needed to enable one. A provider is <c>Enabled</c>
/// when its descriptor is registered AND its feature flag resolves true for this deployment.
/// </summary>
public interface ILoginProviderRegistry
{
    /// <summary>Every registered descriptor, regardless of its feature flag.</summary>
    IReadOnlyList<LoginProviderDescriptor> All { get; }

    /// <summary>The descriptors whose feature flag resolves true for this deployment (login-screen list).</summary>
    Task<IReadOnlyList<LoginProviderDescriptor>> EnabledAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>Resolve one descriptor by key. Failure <c>UNKNOWN_PROVIDER</c> when not registered.</summary>
    Result<LoginProviderDescriptor> Get(string key);
}
