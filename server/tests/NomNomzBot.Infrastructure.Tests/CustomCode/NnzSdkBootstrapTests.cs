// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using FluentAssertions;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode.Jint;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the batteries-included <c>nnz</c> SDK (dev-platform.md §3.1) as it actually runs inside the real Jint
/// executor: the pure-JS batteries compute correct values with no host call or budget cost, and each
/// <c>nnz.api.*</c> wrapper reaches the capability broker over the same <c>bot.call</c> bridge — granted calls hit
/// the host with the right key + args, ungranted calls are denied. Also proves the save-time capability
/// declaration discovers <c>nnz.api.*</c> usage so the broker can gate it.
/// </summary>
public sealed class NnzSdkBootstrapTests
{
    private sealed record HostCall(string Key, IReadOnlyList<string> Args);

    // Records every host call it receives; the handler decides the primitive returned to the guest.
    private sealed class RecordingBridge(Func<string, IReadOnlyList<string>, string?> handler)
        : IScriptHostBridge
    {
        public List<HostCall> Calls { get; } = [];

        public HostImportDelegate Resolve(string capabilityKey) =>
            (key, args, ct) =>
            {
                Calls.Add(new HostCall(key, args));
                return handler(key, args);
            };
    }

    private static readonly IScriptHostBridge NoBridge = new RecordingBridge((_, _) => null);

    // A generous wall clock so engine construction never races the outcome on a loaded box (same rationale as
    // JintScriptExecutorTests). The batteries finish in microseconds; the ceiling is never actually consumed.
    private static readonly ScriptResourceBudget Generous = ScriptResourceBudget.Baseline with
    {
        WallClockMs = 30_000,
    };

    private static ScriptExecutionRequest Request(string js) =>
        new(
            "exec-1",
            js,
            "hash",
            new ScriptInputs("u1", "User", [], new Dictionary<string, string>()),
            Generous
        );

    private static ScriptCapabilityGrant Grant(params string[] keys) =>
        new(
            Guid.NewGuid(),
            [.. keys.Select(k => new ScriptCapabilityDescriptor(k, "tos", "ff", true))]
        );

    private static async Task<ScriptExecutionOutcomeResult> Run(
        string js,
        ScriptCapabilityGrant grant,
        IScriptHostBridge bridge
    ) => (await new JintScriptExecutor().ExecuteAsync(Request(js), grant, bridge)).Value;

    private static double Number(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Units_convert_km_to_mi_returns_the_real_value()
    {
        ScriptExecutionOutcomeResult r = await Run(
            "bot.setVar('r', String(nnz.units.convert(5, 'km', 'mi')));",
            Grant(),
            NoBridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        // 5 km = 3.10686… mi. A real conversion, not a stub — assert the value, within a mile-of-a-thousandth.
        Number(r.VariablesOut["r"]).Should().BeApproximately(3.10686, 0.001);
        // Pure JS: no host call was made, so no host-call budget was spent.
        r.HostCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Units_convert_temperature_is_affine_not_linear()
    {
        ScriptExecutionOutcomeResult r = await Run(
            "bot.setVar('r', String(nnz.units.convert(100, 'c', 'f')));",
            Grant(),
            NoBridge
        );

        Number(r.VariablesOut["r"]).Should().BeApproximately(212, 0.0001);
    }

    [Fact]
    public async Task Str_and_math_and_time_batteries_return_correct_values()
    {
        ScriptExecutionOutcomeResult r = await Run(
            """
            bot.setVar('upper', nnz.str.upper('hi'));
            bot.setVar('slug', nnz.str.slugify('Hello World!'));
            bot.setVar('trunc', nnz.str.truncate('hello world', 8, '...'));
            bot.setVar('clamp', String(nnz.math.clamp(15, 0, 10)));
            bot.setVar('sum', String(nnz.math.sum([1, 2, 3, 4])));
            bot.setVar('avg', String(nnz.math.avg([2, 4, 6])));
            bot.setVar('round', String(nnz.math.round(3.14159, 2)));
            bot.setVar('diff', String(nnz.time.diff('2020-01-01T00:00:01Z', '2020-01-01T00:00:00Z')));
            bot.setVar('json', nnz.json.stringify({ a: 1 }));
            """,
            Grant(),
            NoBridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["upper"].Should().Be("HI");
        r.VariablesOut["slug"].Should().Be("hello-world");
        r.VariablesOut["trunc"].Should().Be("hello...");
        r.VariablesOut["clamp"].Should().Be("10");
        r.VariablesOut["sum"].Should().Be("10");
        r.VariablesOut["avg"].Should().Be("4");
        r.VariablesOut["round"].Should().Be("3.14");
        r.VariablesOut["diff"].Should().Be("1000");
        r.VariablesOut["json"].Should().Be("{\"a\":1}");
        r.HostCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Api_chat_send_reaches_the_host_with_the_right_key_and_args_when_granted()
    {
        RecordingBridge bridge = new((_, _) => null);

        ScriptExecutionOutcomeResult r = await Run(
            "nnz.api.chat.send('hi chat');",
            Grant("chat.send"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.HostCallCount.Should().Be(1);
        bridge.Calls.Should().ContainSingle();
        bridge.Calls[0].Key.Should().Be("chat.send");
        bridge.Calls[0].Args.Should().ContainSingle().Which.Should().Be("hi chat");
    }

    [Fact]
    public async Task Api_chat_send_is_denied_when_the_capability_is_not_granted()
    {
        RecordingBridge bridge = new((_, _) => null);

        ScriptExecutionOutcomeResult r = await Run(
            "nnz.api.chat.send('hi chat');",
            Grant(), // nothing granted
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Denied);
        // Denied at the bridge boundary — the host never saw the call.
        bridge.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Api_user_get_parses_the_hosts_json_into_an_object()
    {
        RecordingBridge bridge = new(
            (key, _) =>
                key == "user.get"
                    ? "{\"id\":\"u9\",\"username\":\"nomz\",\"displayName\":\"Nomz\",\"avatarUrl\":null}"
                    : null
        );

        ScriptExecutionOutcomeResult r = await Run(
            "var u = nnz.api.user.get('u9'); bot.setVar('name', u.username); bot.setVar('id', u.id);",
            Grant("user.get"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        bridge.Calls[0].Key.Should().Be("user.get");
        bridge.Calls[0].Args.Should().ContainSingle().Which.Should().Be("u9");
        r.VariablesOut["name"].Should().Be("nomz");
        r.VariablesOut["id"].Should().Be("u9");
    }

    [Fact]
    public async Task Api_music_queue_maps_the_hosts_boolean_string_to_a_boolean()
    {
        RecordingBridge bridge = new((key, _) => key == "music.queue" ? "true" : null);

        ScriptExecutionOutcomeResult r = await Run(
            "bot.setVar('ok', String(nnz.api.music.queue('never gonna give you up')));",
            Grant("music.queue"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        bridge.Calls[0].Key.Should().Be("music.queue");
        r.VariablesOut["ok"].Should().Be("true");
    }

    [Fact]
    public async Task Compile_declares_capabilities_from_nnz_api_usage()
    {
        ScriptCompilation compilation = (
            await new JintScriptExecutor().CompileAsync(
                "nnz.api.chat.send('hi'); nnz.api.economy.balance(); nnz.api.music.nowPlaying();"
            )
        ).Value;

        // economy.balance maps to the economy.read catalogue key; chat.send + music.nowPlaying map 1:1.
        compilation
            .DeclaredCapabilities.Should()
            .BeEquivalentTo("chat.send", "economy.read", "music.nowPlaying");
    }

    [Fact]
    public async Task Api_storage_wrappers_round_trip_through_the_bridge_with_the_right_keys()
    {
        // The handler is a tiny KV store, so set → get proves the wrapper passes both args and returns
        // the host's value (and that set maps the host's "ok" to a boolean).
        Dictionary<string, string> kv = new();
        RecordingBridge bridge = new(
            (key, args) =>
            {
                if (key == "storage.set")
                {
                    kv[args[0]] = args[1];
                    return "ok";
                }
                return key == "storage.get" ? kv.GetValueOrDefault(args[0]) : null;
            }
        );

        ScriptExecutionOutcomeResult r = await Run(
            """
            bot.setVar('setOk', String(nnz.api.storage.set('feather', 'nomz')));
            bot.setVar('got', String(nnz.api.storage.get('feather')));
            """,
            Grant("storage.set", "storage.get"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["setOk"].Should().Be("true");
        r.VariablesOut["got"].Should().Be("nomz");
        bridge.Calls.Select(c => c.Key).Should().Equal("storage.set", "storage.get");
    }

    [Fact]
    public async Task Api_widget_emit_serializes_the_data_object_and_maps_ok_to_true()
    {
        RecordingBridge bridge = new((key, _) => key == "widget.emit" ? "ok" : null);

        ScriptExecutionOutcomeResult r = await Run(
            "bot.setVar('ok', String(nnz.api.widget.emit('Alert Box', 'confetti', { count: 5 })));",
            Grant("widget.emit"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["ok"].Should().Be("true");
        bridge.Calls[0].Key.Should().Be("widget.emit");
        bridge.Calls[0].Args.Should().Equal("Alert Box", "confetti", "{\"count\":5}");
    }

    [Fact]
    public async Task Api_tts_speak_parses_the_hosts_outcome_json()
    {
        RecordingBridge bridge = new(
            (key, _) =>
                key == "tts.speak" ? "{\"voiceId\":\"en-US-Aria\",\"characterCount\":5}" : null
        );

        ScriptExecutionOutcomeResult r = await Run(
            "var t = nnz.api.tts.speak('hello'); bot.setVar('voice', t.voiceId); bot.setVar('chars', String(t.characterCount));",
            Grant("tts.speak"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["voice"].Should().Be("en-US-Aria");
        r.VariablesOut["chars"].Should().Be("5");
    }

    [Fact]
    public async Task Compile_declares_capabilities_from_the_new_nnz_api_groups()
    {
        ScriptCompilation compilation = (
            await new JintScriptExecutor().CompileAsync(
                """
                nnz.api.storage.set('k', 'v');
                nnz.api.storage.list();
                nnz.api.tts.speak('hi');
                nnz.api.widget.emit('w', 'e');
                nnz.api.reward.update('r', { cost: 1 });
                """
            )
        ).Value;

        compilation
            .DeclaredCapabilities.Should()
            .BeEquivalentTo(
                "storage.set",
                "storage.list",
                "tts.speak",
                "widget.emit",
                "reward.update"
            );
    }

    [Fact]
    public async Task Api_stats_viewer_parses_the_hosts_stats_json()
    {
        RecordingBridge bridge = new(
            (key, _) =>
                key == "stats.viewer"
                    ? "{\"messages\":420,\"watchtimeSeconds\":7200,\"firstSeen\":\"2026-01-05\",\"redemptions\":3,\"songRequests\":9}"
                    : null
        );

        ScriptExecutionOutcomeResult r = await Run(
            """
            var s = nnz.api.stats.viewer('bamo');
            bot.setVar('msgs', String(s.messages));
            bot.setVar('watch', String(s.watchtimeSeconds));
            bot.setVar('first', s.firstSeen);
            bot.setVar('sr', String(s.songRequests));
            """,
            Grant("stats.viewer"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        bridge.Calls[0].Key.Should().Be("stats.viewer");
        bridge.Calls[0].Args.Should().ContainSingle().Which.Should().Be("bamo");
        r.VariablesOut["msgs"].Should().Be("420");
        r.VariablesOut["watch"].Should().Be("7200");
        r.VariablesOut["first"].Should().Be("2026-01-05");
        r.VariablesOut["sr"].Should().Be("9");
    }

    [Fact]
    public async Task Api_tts_voice_wrappers_carry_the_right_keys_and_map_ok_to_true()
    {
        RecordingBridge bridge = new(
            (key, _) =>
                key switch
                {
                    "tts.voice.get" =>
                        "{\"voiceId\":\"en-GB-Sonia\",\"displayName\":\"Sonia (British)\"}",
                    "tts.voice.set" => "ok",
                    _ => null,
                }
        );

        ScriptExecutionOutcomeResult r = await Run(
            """
            var v = nnz.api.tts.getVoice('bamo');
            bot.setVar('voice', v.displayName);
            bot.setVar('setOk', String(nnz.api.tts.setVoice('bamo', 'en-GB-Sonia')));
            bot.setVar('clearOk', String(nnz.api.tts.setVoice('bamo')));
            """,
            Grant("tts.voice.get", "tts.voice.set"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["voice"].Should().Be("Sonia (British)");
        r.VariablesOut["setOk"].Should().Be("true");
        r.VariablesOut["clearOk"].Should().Be("true");
        bridge
            .Calls.Select(c => c.Key)
            .Should()
            .Equal("tts.voice.get", "tts.voice.set", "tts.voice.set");
        // The no-voiceId overload clears: the wrapper sends an empty second arg, the clear contract.
        bridge.Calls[2].Args.Should().Equal("bamo", "");
    }

    [Fact]
    public async Task Api_stats_viewer_is_denied_when_the_capability_is_not_granted()
    {
        RecordingBridge bridge = new((_, _) => null);

        ScriptExecutionOutcomeResult r = await Run(
            "nnz.api.stats.viewer();",
            Grant(), // nothing granted
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Denied);
        bridge.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Compile_declares_the_stats_and_voice_capabilities_from_wrapper_usage()
    {
        ScriptCompilation compilation = (
            await new JintScriptExecutor().CompileAsync(
                """
                nnz.api.stats.viewer();
                nnz.api.tts.getVoice('u');
                nnz.api.tts.setVoice('u', 'v');
                """
            )
        ).Value;

        compilation
            .DeclaredCapabilities.Should()
            .BeEquivalentTo("stats.viewer", "tts.voice.get", "tts.voice.set");
    }
}
