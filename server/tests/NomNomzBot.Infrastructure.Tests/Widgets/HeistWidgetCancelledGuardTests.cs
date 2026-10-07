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
using Jint;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The live-game engine sends a <c>cancelled</c> frame (as <c>game.resolved</c>) when a round is cancelled
/// (<c>LiveGameEngine.CancelInternalAsync</c>). The heist widget must leave the lobby, show the reason, and hide
/// after <c>hideAfterMs</c>. The real <c>onFrame</c>, <c>reset</c> and <c>scheduleHide</c> run in Jint.
/// </summary>
public sealed class HeistWidgetCancelledGuardTests
{
    private static Engine CreateWidget()
    {
        string source = WidgetAssetSource.Load("heist.vue");
        string reset = WidgetAssetSource
            .ExtractBlock(source, @"function\s+reset\s*\(")
            .Replace("function reset(): void {", "function reset() {");
        string onFrame = WidgetAssetSource
            .ExtractBlock(source, @"function\s+onFrame\s*\(")
            .Replace("function onFrame(d: GameFrame): void {", "function onFrame(d) {")
            .Replace("(r): HeistResult =>", "(r) =>");
        string scheduleHide = WidgetAssetSource
            .ExtractBlock(source, @"function\s+scheduleHide\s*\(")
            .Replace("function scheduleHide(): void {", "function scheduleHide() {");

        Engine engine = new();
        engine.Execute(WidgetAssetSource.FakeClock);
        engine.Execute(
            """
            var cfg = { accentColor: '#9146ff', hideAfterMs: 12000 };
            var visible = { value: false };
            var phase = { value: 'lobby' };
            var successChance = { value: 0 };
            var crew = { value: [] };
            var results = { value: [] };
            var cancelReason = { value: '' };
            var hideTimer = undefined;
            """
        );
        engine.Execute(reset);
        engine.Execute(onFrame);
        engine.Execute(scheduleHide);
        engine.Execute(
            """
            onFrame({ kind: 'round_open', successChance: 40 });
            onFrame({ kind: 'join', successChance: 40, crew: [{ player: 'a', stake: 100 }] });
            """
        );
        return engine;
    }

    [Fact]
    public void A_cancelled_round_shows_its_reason_and_then_hides()
    {
        Engine engine = CreateWidget();

        engine.Execute(
            "onFrame({ kind: 'cancelled', cancelled: true, reason: 'Not enough crew' });"
        );

        engine.Evaluate("phase.value").AsString().Should().Be("cancelled");
        engine.Evaluate("cancelReason.value").AsString().Should().Be("Not enough crew");
        engine.Evaluate("visible.value").AsBoolean().Should().BeTrue("the reason shows first");

        engine.Execute("advance(12000);");

        engine
            .Evaluate("visible.value")
            .AsBoolean()
            .Should()
            .BeFalse("a cancelled heist must not stay on screen");
    }

    [Fact]
    public void A_new_round_after_a_cancel_starts_a_clean_lobby()
    {
        Engine engine = CreateWidget();
        engine.Execute(
            """
            onFrame({ kind: 'cancelled', cancelled: true, reason: 'Not enough crew' });
            onFrame({ kind: 'round_open', successChance: 55 });
            advance(20000);
            """
        );

        engine.Evaluate("phase.value").AsString().Should().Be("lobby");
        engine.Evaluate("cancelReason.value").AsString().Should().BeEmpty();
        engine
            .Evaluate("visible.value")
            .AsBoolean()
            .Should()
            .BeTrue("the old hide timer was cleared by the new round");
    }
}
