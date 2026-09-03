// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Platform.Dtos;

/// <summary>
/// Where a resolved credential field actually came from. The operator surface has to say this out loud:
/// a stored value SHADOWS the environment, and a stale stored secret silently beating a corrected env var
/// is a real failure this instance has already hit (401s that look like a revoked app).
/// </summary>
public static class CredentialSource
{
    /// <summary>A vaulted <c>Configuration</c> row. Wins over the environment.</summary>
    public const string Stored = "stored";

    /// <summary>An <c>IConfiguration</c> / environment value. Used only when nothing is stored.</summary>
    public const string Environment = "environment";

    /// <summary>Neither source sets it.</summary>
    public const string Unset = "unset";
}

/// <summary>
/// One provider's app-credential state for the operator console.
///
/// <para><b>The secret is never in here, in any form.</b> It is sealed under an AAD tied to its provider and
/// field, and there is no read path that unseals it for display — only <see cref="SecretSource"/>, which says
/// whether one exists and which source would win. The client id IS carried: it is a public identifier that
/// appears in every OAuth URL the viewer's own browser sees, and hiding it would only stop the operator
/// checking the one value most likely to be wrong.</para>
/// </summary>
/// <param name="AppDecisionRecorded">
/// True when the operator made a DELIBERATE recorded choice for this provider — their own app, or an
/// explicit decision to use the shared one. A shipped config default alone is never enough.
/// </param>
/// <param name="Supported">False for providers whose credentials this build has no way to use.</param>
public sealed record ProviderCredentialDto(
    string Provider,
    string? ClientId,
    string ClientIdSource,
    string SecretSource,
    bool AppDecisionRecorded,
    bool Supported
);

/// <summary>
/// A credential write. Both fields optional and independent: sending only a secret rotates the secret and
/// leaves the id alone, which is the common case. A BLANK field means "leave it", never "clear it" — clearing
/// is <c>DELETE</c>, so a half-filled form can never silently wipe a working credential.
/// </summary>
public sealed record SaveProviderCredentialRequest(string? ClientId, string? ClientSecret);
