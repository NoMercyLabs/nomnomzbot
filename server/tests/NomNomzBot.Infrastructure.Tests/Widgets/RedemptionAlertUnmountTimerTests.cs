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
using Jint;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// redemption_alert.vue fades a card out, then shows the next card 400 ms later. When the widget unmounts during
/// that fade, no timer may still fire and start a card on a dead component. The real <c>showNext</c> and the real
/// <c>onUnmounted</c> body are taken from the shipped asset and run in Jint with a fake clock.
/// </summary>
public sealed class RedemptionAlertUnmountTimerTests
{
    private static Engine CreateWidget()
    {
        string source = WidgetAssetSource.Load("redemption_alert.vue");
        string showNext = WidgetAssetSource
            .ExtractBlock(source, @"function\s+showNext\s*\(")
            .Replace("function showNext(): void {", "function showNext() {")
            .Replace(
                "const next: RedemptionCard | undefined = queue.shift()",
                "const next = queue.shift()"
            );
        string unmountBody = WidgetAssetSource.ExtractCallbackBody(source, "onUnmounted");

        Engine engine = new();
        engine.Execute(WidgetAssetSource.FakeClock);
        engine.Execute(
            """
            function requestAnimationFrame(fn) { fn(); }
            var NomNomz = { off: function () {} };
            function onRedeemed() {}
            var cfg = { durationMs: 1000 };
            var queue = [];
            var current = { value: null };
            var visible = { value: false };
            var cardKey = { value: 0 };
            var timer = 0;
            """
        );
        engine.Execute(showNext);
        engine.Execute($"function unmount() {{ {unmountBody} }}");
        return engine;
    }

    [Fact]
    public void An_unmount_during_the_fade_stops_the_next_card()
    {
        Engine engine = CreateWidget();
        engine.Execute(
            """
            queue.push({ user: 'a' }, { user: 'b' });
            showNext();
            advance(1000);
            """
        );
        engine
            .Evaluate("visible.value")
            .AsBoolean()
            .Should()
            .BeFalse("the first card is fading out");

        engine.Execute("unmount(); advance(5000);");

        engine
            .Evaluate("String(current.value && current.value.user)")
            .AsString()
            .Should()
            .Be("a", "after unmount no timer may start the next card");
        engine.Evaluate("queue.length").AsNumber().Should().Be(1);
        engine.Evaluate("Object.keys(clock.pending).length").AsNumber().Should().Be(0);
    }

    [Fact]
    public void Without_an_unmount_the_next_card_follows_the_fade()
    {
        Engine engine = CreateWidget();
        engine.Execute(
            """
            queue.push({ user: 'a' }, { user: 'b' });
            showNext();
            advance(1400);
            """
        );

        engine.Evaluate("String(current.value && current.value.user)").AsString().Should().Be("b");
        engine.Evaluate("visible.value").AsBoolean().Should().BeTrue();
    }
}
