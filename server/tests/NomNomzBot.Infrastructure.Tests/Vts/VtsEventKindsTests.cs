// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
