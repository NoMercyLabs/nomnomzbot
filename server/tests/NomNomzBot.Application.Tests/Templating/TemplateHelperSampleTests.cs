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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Application.Tests.Templating;

/// <summary>
/// The variable picker shows a realistic example next to every helper, so a registry entry without a
/// sample must fail here by name rather than ship an empty hint.
/// </summary>
public sealed class TemplateHelperSampleTests
{
    [Fact]
    public void Every_registry_entry_has_a_non_blank_sample()
    {
        List<string> missing =
        [
            .. TemplateHelperRegistry
                .All.Where(e => string.IsNullOrWhiteSpace(e.Sample))
                .Select(e => e.Key),
        ];

        missing.Should().BeEmpty("every helper needs a sample value for the picker");
    }

    [Fact]
    public void Dto_carries_the_entry_sample_to_the_wire()
    {
        TemplateHelperEntry entry = TemplateHelperRegistry.All.Single(e => e.Key == "user.name");

        TemplateHelperDto dto = TemplateHelperDto.FromEntry(entry);

        dto.Sample.Should().Be(entry.Sample).And.NotBeNullOrWhiteSpace();
    }
}
