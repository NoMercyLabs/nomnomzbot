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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// Proves <see cref="ToneTemplateCatalog.Pick"/> never speaks the same personality-flavored line twice in a
/// row for the same (tone, builtin, slot) — it delegates to the shared
/// <see cref="Common.Picking.NoImmediateRepeatPicker"/>, the same guarantee <c>ChatMessageHandler.PickResponse</c>
/// gives authored random-response commands.
/// </summary>
public sealed class ToneTemplateCatalogNoRepeatTests
{
    [Fact]
    public void A_sassy_uptime_line_is_never_spoken_twice_in_a_row()
    {
        // BuiltinResponseSlots.Uptime.Live/"sassy" has 4 variations declared in the catalog — enough to prove
        // non-repetition without ever running dry.
        string? previous = ToneTemplateCatalog.Pick(
            PersonalityTone.Sassy,
            BuiltinResponseSlots.Uptime.Key,
            BuiltinResponseSlots.Uptime.Live
        );
        previous.Should().NotBeNull();

        for (int i = 0; i < 200; i++)
        {
            string? next = ToneTemplateCatalog.Pick(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.Uptime.Key,
                BuiltinResponseSlots.Uptime.Live
            );
            next.Should()
                .NotBe(previous, "an immediately repeated personality line reads as a broken bot");
            previous = next;
        }
    }

    [Fact]
    public void An_unknown_slot_returns_null_without_touching_the_picker()
    {
        ToneTemplateCatalog
            .Pick(PersonalityTone.Sassy, "no-such-builtin", "no-such-slot")
            .Should()
            .BeNull();
    }
}
