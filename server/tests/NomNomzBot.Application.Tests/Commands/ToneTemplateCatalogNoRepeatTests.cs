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
