// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// The process-wide holder for the host's bound listen port (deployment-distribution §6). Set once by the API host
/// after smart port resolution; read by the self-host mDNS advertiser. Fail-closed: reading <see cref="Port"/>
/// before it is set throws, so the advertiser can never announce a placeholder port.
/// </summary>
public sealed class ListenEndpointAccessor : IListenEndpointAccessor
{
    private int? _port;

    public int Port =>
        _port
        ?? throw new InvalidOperationException(
            "Listen port read before the host resolved it (fail-closed boot ordering)."
        );

    public bool IsResolved => _port is not null;

    public void SetPort(int port)
    {
        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(
                nameof(port),
                port,
                "Listen port must be in 1..65535."
            );
        _port = port;
    }
}
