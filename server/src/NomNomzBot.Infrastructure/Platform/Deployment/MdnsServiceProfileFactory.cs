// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using Makaretu.Dns;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// Builds the DNS-SD <see cref="ServiceProfile"/> the self-host bot advertises on the LAN (deployment-distribution
/// §6). Pure and side-effect-free — it constructs the announcement (service type, instance name, bound port, and
/// the TXT records the native connection switcher uses to de-dupe + label the discovered bot) without touching the
/// network, so the announcement shape is unit-testable. The socket lifecycle lives in
/// <see cref="MdnsAdvertiserHostedService"/>.
/// </summary>
public static class MdnsServiceProfileFactory
{
    /// <summary>The DNS-SD service type the native app browses for (deployment-distribution §6, frontend §6).</summary>
    public const string ServiceType = "_nomnomz._tcp";

    /// <summary>
    /// Build the advertisement for this instance.
    /// </summary>
    /// <param name="instanceId">The stable <c>DeploymentProfile.InstanceId</c> — the TXT <c>instance</c> de-dupe key.</param>
    /// <param name="displayName">The bot's display/instance name (defaults to the machine name when blank).</param>
    /// <param name="port">The <b>actual</b> bound API port.</param>
    public static ServiceProfile Build(Guid instanceId, string? displayName, int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(port, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        string instanceName = string.IsNullOrWhiteSpace(displayName)
            ? Environment.MachineName
            : displayName.Trim();

        // ServiceProfile fills in the host A/AAAA + SRV + PTR records; passing no explicit addresses lets it bind to
        // the host's link-local interfaces at announce time (re-resolved on interface change by the hosted service).
        ServiceProfile profile = new(
            instanceName: instanceName,
            serviceName: ServiceType,
            port: (ushort)port
        );

        // TXT records the native switcher reads (frontend §6): a stable de-dupe id, the scheme/base path to build the
        // baseUrl, and the running build version so the UI can show what it found.
        profile.AddProperty("instance", instanceId.ToString());
        profile.AddProperty("scheme", "http");
        profile.AddProperty("path", "/");
        profile.AddProperty("version", BuildVersion());

        return profile;
    }

    private static string BuildVersion()
    {
        Assembly assembly = typeof(MdnsServiceProfileFactory).Assembly;
        string version =
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        int plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
