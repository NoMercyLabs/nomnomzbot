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
