// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.E2E.Tests.Harness;

/// <summary>
/// Runtime configuration for the end-to-end harness, read from the environment so the same tests can
/// target the deployed dev instance, a local <c>dotnet run</c>, or a CI-provisioned instance without a
/// code change.
/// </summary>
internal static class E2ESettings
{
    /// <summary>
    /// Set <c>NOMNOMZ_E2E=1</c> to actually run the browser tests. When unset (the default, including CI)
    /// every <see cref="E2EFactAttribute"/> test skips itself, so <c>dotnet test</c> needs neither a live
    /// server nor an installed browser to stay green.
    /// </summary>
    internal const string EnableVariable = "NOMNOMZ_E2E";

    /// <summary>Override the target instance with <c>NOMNOMZ_E2E_BASE_URL</c>; defaults to the deployed dev box.</summary>
    internal const string BaseUrlVariable = "NOMNOMZ_E2E_BASE_URL";

    private const string DefaultBaseUrl = "http://192.168.2.60:5080";

    /// <summary>True only when the operator opted in via <see cref="EnableVariable"/>.</summary>
    internal static bool Enabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnableVariable));

    /// <summary>The instance under test, without a trailing slash.</summary>
    internal static string BaseUrl
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable(BaseUrlVariable);
            return string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured.TrimEnd('/');
        }
    }
}
