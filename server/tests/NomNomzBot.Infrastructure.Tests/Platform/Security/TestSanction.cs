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
