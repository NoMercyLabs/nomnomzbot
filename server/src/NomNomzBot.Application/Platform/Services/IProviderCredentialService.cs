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
using NomNomzBot.Application.Platform.Dtos;

namespace NomNomzBot.Application.Platform.Services;

/// <summary>
/// The operator console's view of the platform's OAuth app credentials, and the only way to change them
/// after setup.
///
/// <para>The setup wizard writes these once during onboarding and then has no further say. Nothing else
/// could read back which providers are configured, from which source, or rotate a leaked secret — the only
/// remedy was editing the database by hand. This is that surface.</para>
///
/// <para><b>Secrets are write-only here, deliberately.</b> They are sealed under an AAD tied to their
/// provider and field, and this contract has no operation that unseals one for display: reads report only
/// whether a secret exists and which source would win. Client ids ARE readable — a client id is a public
/// identifier that appears in every OAuth URL a viewer's browser already sees, and withholding it would only
/// stop an operator from checking the value most likely to be wrong.</para>
/// </summary>
public interface IProviderCredentialService
{
    /// <summary>
    /// Every provider this build can hold app credentials for, with what is currently set and where it came
    /// from. Enumerated from the provider catalogue rather than a hand-kept list, so a provider added to the
    /// product cannot quietly go unmanageable.
    /// </summary>
    Task<Result<IReadOnlyList<ProviderCredentialDto>>> ListAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stores a client id and/or secret for <paramref name="provider"/>. Blank fields are left untouched, so
    /// rotating a secret never has to re-send the id and a half-filled form cannot wipe a working credential.
    /// Returns the provider's resulting state.
    /// </summary>
    Task<Result<ProviderCredentialDto>> SaveAsync(
        string provider,
        SaveProviderCredentialRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Removes the STORED rows for <paramref name="provider"/>, handing resolution back to the environment.
    ///
    /// <para>This is the repair path for the failure that motivated the surface: a stored secret shadows the
    /// environment, so an operator who corrects a rotated secret in their <c>.env</c> keeps getting 401s from
    /// a stale stored value they cannot see. Clearing is the only way back, and it must be explicit —
    /// never a side effect of saving a blank field.</para>
    /// </summary>
    Task<Result<ProviderCredentialDto>> ClearAsync(
        string provider,
        CancellationToken cancellationToken = default
    );
}
