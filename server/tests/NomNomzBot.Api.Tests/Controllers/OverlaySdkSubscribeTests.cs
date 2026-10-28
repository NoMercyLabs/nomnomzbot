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

using System.Text.Json;
using FluentAssertions;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The served SDK tells the hub which event types the page registered (<c>Subscribe</c>), run for real in Jint
/// behind the fake browser. Proves what is sent and when: the whole set after a successful join and after every
/// re-join, a grown set once 250 ms later, never a smaller set, never an empty set, and never a name the hub
/// would refuse the whole set for.
/// </summary>
public sealed class OverlaySdkSubscribeTests
{
    private const string TimerQueue = """
        var __timers = [];
        setTimeout = function (fn, ms) { __timers.push({ fn: fn, ms: ms }); return __timers.length; };
        function __queued(ms) { return __timers.filter(function (t) { return t.ms === ms; }); }
        function __fire(ms) { var t = __queued(ms)[0]; __timers.splice(__timers.indexOf(t), 1); t.fn(); }
        """;

    private const string NoOp = "var noop = function () {};";

    // The page loads the SDK, which opens its socket and sends the join; the join answer is the test's to give.
    private static OverlaySdkRuntime Booted()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(TimerQueue);
        sdk.Evaluate(NoOp);
        return sdk;
    }

    private static void Join(OverlaySdkRuntime sdk, bool success = true) =>
        sdk.Complete(
            "join",
            new
            {
                success,
                initialState = (object?)null,
                seed = Array.Empty<object>(),
            }
        );

    // Every Subscribe call the page sent over any of its sockets, as the hub receives it: [widgetId, types].
    private static List<JsonElement> SubscribeCalls(OverlaySdkRuntime sdk)
    {
        string json = sdk.Text(
            """
            JSON.stringify(
              __sockets
                .reduce(function (all, s) { return all.concat(s.sent); }, [])
                .map(function (frame) { return JSON.parse(frame.slice(0, -1)); })
                .filter(function (m) { return m.target === 'Subscribe'; })
                .map(function (m) { return m.arguments; }))
            """
        );
        return JsonSerializer.Deserialize<List<JsonElement>>(json)!;
    }

    private static string[] Types(JsonElement call) =>
        call[1].EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public void Registered_types_and_onAny_are_sent_as_one_set_after_the_join()
    {
        OverlaySdkRuntime sdk = Booted();
        sdk.Evaluate(
            "['follow', 'raid'].forEach(function (type) { NomNomz.on(type, noop); }); NomNomz.onAny(noop);"
        );
        SubscribeCalls(sdk).Should().BeEmpty("the hub has not accepted the join yet");

        Join(sdk);

        List<JsonElement> calls = SubscribeCalls(sdk);
        calls.Should().HaveCount(1);
        calls[0][0].GetString().Should().Be("widget-1");
        Types(calls[0]).Should().Equal("follow", "raid", "*");
    }

    [Fact]
    public void A_handler_added_after_the_join_queues_one_timer_and_the_grown_set_goes_out_when_it_fires()
    {
        OverlaySdkRuntime sdk = Booted();
        sdk.Evaluate("NomNomz.on('follow', noop);");
        Join(sdk);

        sdk.Evaluate("NomNomz.on('raid', noop); NomNomz.on('cheer', noop);");

        sdk.Text("__queued(250).length").Should().Be("1", "two additions share one timer");
        SubscribeCalls(sdk).Should().HaveCount(1, "the grown set waits for its timer");

        sdk.Evaluate("__fire(250);");

        List<JsonElement> calls = SubscribeCalls(sdk);
        calls.Should().HaveCount(2);
        Types(calls[1]).Should().Equal("follow", "raid", "cheer");
    }

    [Fact]
    public void A_type_the_page_already_registered_queues_nothing()
    {
        OverlaySdkRuntime sdk = Booted();
        sdk.Evaluate("NomNomz.on('follow', noop);");
        Join(sdk);

        sdk.Evaluate("NomNomz.on('follow', function () {});");

        sdk.Text("__queued(250).length").Should().Be("0");
    }

    [Fact]
    public void Removing_a_handler_does_not_shrink_the_set()
    {
        OverlaySdkRuntime sdk = Booted();
        sdk.Evaluate(
            "var onRaid = function () {}; NomNomz.on('follow', noop); NomNomz.on('raid', onRaid);"
        );
        Join(sdk);

        sdk.Evaluate("NomNomz.off('raid', onRaid);");

        sdk.Text("__queued(250).length").Should().Be("0", "a removal sends nothing");

        sdk.Evaluate("NomNomz.on('cheer', noop); __fire(250);");

        List<JsonElement> calls = SubscribeCalls(sdk);
        calls.Should().HaveCount(2);
        Types(calls[1]).Should().Equal("follow", "raid", "cheer");
    }

    [Fact]
    public void A_reconnect_sends_the_whole_set_again_with_what_was_added_while_the_socket_was_down()
    {
        OverlaySdkRuntime sdk = Booted();
        sdk.Evaluate("NomNomz.on('follow', noop); NomNomz.on('raid', noop);");
        Join(sdk);
        SubscribeCalls(sdk).Should().HaveCount(1);

        sdk.Evaluate("__sockets[0].onclose();");
        sdk.Evaluate("NomNomz.on('cheer', noop);");

        sdk.Text("__queued(250).length").Should().Be("0", "nothing is sent over a dropped socket");

        sdk.Evaluate("__fire(1000);");
        sdk.Evaluate(
            """
            var next = __sockets[1];
            next.onopen();
            next.onmessage({ data: "{}" + String.fromCharCode(30) });
            next.onmessage({ data: JSON.stringify({ type: 3, invocationId: "join",
              result: { success: true, initialState: null, seed: [] } }) + String.fromCharCode(30) });
            """
        );

        List<JsonElement> calls = SubscribeCalls(sdk);
        calls.Should().HaveCount(2);
        Types(calls[1]).Should().Equal("follow", "raid", "cheer");
    }

    [Fact]
    public void An_empty_set_is_never_sent_and_the_first_type_after_the_join_is()
    {
        OverlaySdkRuntime sdk = Booted();

        Join(sdk);

        SubscribeCalls(sdk).Should().BeEmpty("an empty set would wipe the stored list");

        sdk.Evaluate("NomNomz.on('follow', noop); __fire(250);");

        List<JsonElement> calls = SubscribeCalls(sdk);
        calls.Should().HaveCount(1);
        Types(calls[0]).Should().Equal("follow");
    }

    [Fact]
    public void A_join_the_hub_refused_sends_nothing_and_queues_nothing()
    {
        OverlaySdkRuntime sdk = Booted();
        sdk.Evaluate("NomNomz.on('follow', noop);");

        Join(sdk, success: false);
        sdk.Evaluate("NomNomz.on('raid', noop);");

        SubscribeCalls(sdk).Should().BeEmpty();
        sdk.Text("__queued(250).length").Should().Be("0");
    }

    [Fact]
    public void A_name_the_hub_would_refuse_is_left_out_of_the_set()
    {
        OverlaySdkRuntime sdk = Booted();
        sdk.Evaluate(
            "['has space', 'semi;colon', '', 'follow'].forEach(function (type) { NomNomz.on(type, noop); });"
        );

        Join(sdk);

        List<JsonElement> calls = SubscribeCalls(sdk);
        calls.Should().HaveCount(1);
        Types(calls[0]).Should().Equal("follow");
    }
}
