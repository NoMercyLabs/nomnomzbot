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
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Domain.Obs.Entities;
using NomNomzBot.Infrastructure.Obs;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>The connection read lists every event kind the mask can switch, so the dashboard never keeps its own list.</summary>
public sealed class ObsEventKindsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f6d2");

    [Fact]
    public async Task The_read_lists_each_kind_with_its_bit_and_whether_the_default_mask_has_it()
    {
        ObsConnectionDto dto = (
            await new ObsConnectionService(
                ObsTestDbContext.New(),
                Substitute.For<ITokenProtector>()
            ).GetAsync(Channel)
        ).Value;

        EventKindDto scenes = dto.EventKinds.Single(k => k.Name == "Scenes");
        scenes.Bit.Should().Be(1 << 2);
        scenes.OnByDefault.Should().BeTrue();

        EventKindDto meters = dto.EventKinds.Single(k => k.Name == "InputVolumeMeters");
        meters.Bit.Should().Be(1 << 16);
        meters.OnByDefault.Should().BeFalse();

        dto.EventKinds.Select(k => k.Bit).Should().OnlyHaveUniqueItems();
        dto.EventKinds.Should()
            .OnlyContain(k => (k.Bit & ~ObsConnectionDefaults.MaxEventSubscriptionsMask) == 0);
    }
}
