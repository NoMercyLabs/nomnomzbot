// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Interfaces;

/// <summary>
/// The single resolution path for the platform's OAuth <em>app</em> credentials (the onboarding keystone):
/// the wizard vaults them to the DB (system-scoped <c>Configuration</c> rows, <c>BroadcasterId == null</c>),
/// so a self-host operator configures everything through the dashboard and never edits a config file. Each
/// field resolves DB-first then <c>{Provider}:ClientId/ClientSecret</c> from <c>IConfiguration</c> as
/// the env/appsettings fallback — so a credential-less boot drops into setup mode, and saved wizard creds
/// actually drive the live OAuth flows. The secret is sealed under the AAD <c>("system", provider, field)</c>,
/// so a sealed value for one provider can never be unprotected as another's, and a raw DB read yields only
/// sealed bytes.
/// </summary>
public interface ISystemCredentialsProvider
{
    /// <summary>
    /// The resolved client id + secret for <paramref name="provider"/> (<c>twitch</c> / <c>spotify</c> /
    /// <c>youtube</c> / <c>discord</c>), DB-vaulted first then config fallback. Returns null when neither
    /// source configures both fields — the OAuth surfaces then report "configure your app credentials first"
    /// rather than issuing a malformed request.
    /// </summary>
    Task<SystemAppCredentials?> GetAsync(
        string provider,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The resolved client id for <paramref name="provider"/> alone — no secret — DB-vaulted
    /// (<c>"{provider}.client_id"</c>) first, then <c>{Provider}:ClientId</c> from config. This is the no-secret
    /// path: NomNomzBot ships its own public client id as the config default (zero-setup Device Code Flow), and a
    /// self-host operator's own app (BYOC) is a DB row that overrides it. Null only when neither source sets it.
    /// </summary>
    Task<string?> GetClientIdAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// A single non-secret system value (e.g. <c>twitch.bot_username</c>), resolved DB-first then config
    /// fallback (<c>{Provider}:{Field}</c> with the field PascalCased). Returns null when unset by either
    /// source. Use for the non-secret config the wizard also captures; secrets go through <see cref="GetAsync"/>.
    /// </summary>
    Task<string?> GetValueAsync(
        string provider,
        string field,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The resolved platform app credentials for one provider — both fields present by construction.</summary>
public sealed record SystemAppCredentials(string ClientId, string ClientSecret);
