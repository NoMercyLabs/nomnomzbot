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

using FluentAssertions;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Infrastructure.Vts;
using NomNomzBot.Infrastructure.Vts.Transport;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Vts;

/// <summary>The connection read lists every VTS event the mask can switch, bit for bit as the transport subscribes them.</summary>
public sealed class VtsEventKindsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000fad2");

    [Fact]
    public async Task The_read_lists_each_kind_at_the_bit_the_transport_subscribes()
    {
        VtsConnectionDto dto = (
            await new VtsConnectionService(
                VtsTestDbContext.New(),
                Substitute.For<ITokenProtector>()
            ).GetAsync(Channel)
        ).Value;

        dto.EventKinds.Select(k => k.Name).Should().Equal(DirectVtsTransport.EventBits);
        foreach (EventKindDto kind in dto.EventKinds)
        {
            DirectVtsTransport.MaskedEvents(kind.Bit).Should().Equal(kind.Name);
        }
        dto.EventKinds.Single(k => k.Name == "ModelLoadedEvent").OnByDefault.Should().BeTrue();
        dto.EventKinds.Single(k => k.Name == "ModelAnimationEvent").OnByDefault.Should().BeFalse();
    }
}
