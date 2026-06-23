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
/// The actual port the host bound at boot, after smart self-host port resolution (deployment-distribution §6). The
/// API layer resolves the listen port before the host binds and publishes it here; the self-host mDNS advertiser
/// reads it so the LAN advertisement carries the <b>real</b> port, not the configured-but-maybe-overridden one.
/// Registered as a singleton; the port is set once during boot.
/// </summary>
public interface IListenEndpointAccessor
{
    /// <summary>The bound TCP port. Throws if read before the host resolved it (fail-closed boot ordering).</summary>
    int Port { get; }

    /// <summary>True once the port has been set (the advertiser checks this before announcing).</summary>
    bool IsResolved { get; }

    /// <summary>Publish the bound port. Called once by the API host immediately after the listen port is resolved.</summary>
    void SetPort(int port);
}
