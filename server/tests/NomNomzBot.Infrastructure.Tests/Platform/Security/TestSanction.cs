// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

/// <summary>
/// A sanction accessor already holding one, for tests about a handler's own decisions rather than about the
/// transport gate — the gate has its own tests in <see cref="OutboundSanctionGateTests"/>.
/// </summary>
internal static class TestSanction
{
    public static IOutboundSanctionAccessor Held()
    {
        OutboundSanctionAccessor accessor = new();
        accessor.Begin(OutboundSanction.ChannelConfiguration("test"));
        return accessor;
    }
}
