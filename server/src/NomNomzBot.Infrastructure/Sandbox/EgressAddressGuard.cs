// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;

namespace NomNomzBot.Infrastructure.Sandbox;

/// <summary>
/// The SSRF resolved-IP guard (code-execution-sandbox.md §7.1 step 4). After resolve-then-pin, EVERY resolved IP
/// of an egress target must pass this before the socket opens. Fail-closed: an internal / link-local / cloud-
/// metadata / CGNAT / ULA / unspecified / unknown-family address is blocked, including the IPv4-mapped-IPv6 form
/// (the classic bypass). Pure — no DNS, no I/O.
/// </summary>
public static class EgressAddressGuard
{
    /// <summary>True iff connecting to <paramref name="address"/> must be denied (it reaches internal infrastructure).</summary>
    public static bool IsBlocked(IPAddress address)
    {
        // Normalize the IPv4-mapped-IPv6 form (e.g. ::ffff:169.254.169.254) so it can't smuggle a blocked v4.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsBlockedV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsBlockedV6(address),
            _ => true, // unknown family — fail closed
        };
    }

    private static bool IsBlockedV4(byte[] b) =>
        b[0] == 0 // 0.0.0.0/8 this-network
        || b[0] == 127 // 127.0.0.0/8 loopback
        || b[0] == 10 // 10.0.0.0/8 private
        || (b[0] == 172 && b[1] is >= 16 and <= 31) // 172.16.0.0/12 private
        || (b[0] == 192 && b[1] == 168) // 192.168.0.0/16 private
        || (b[0] == 169 && b[1] == 254) // 169.254.0.0/16 link-local (incl. 169.254.169.254 metadata)
        || (b[0] == 100 && b[1] is >= 64 and <= 127) // 100.64.0.0/10 CGNAT
        || (b[0] == 255 && b[1] == 255 && b[2] == 255 && b[3] == 255); // broadcast

    private static bool IsBlockedV6(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) // ::1
            return true;
        if (address.Equals(IPAddress.IPv6Any)) // :: unspecified
            return true;

        byte[] b = address.GetAddressBytes();
        return (b[0] & 0xFE) == 0xFC // fc00::/7 unique-local
            || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80); // fe80::/10 link-local
    }
}
