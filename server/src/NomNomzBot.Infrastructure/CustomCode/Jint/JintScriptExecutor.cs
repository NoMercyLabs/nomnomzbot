// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jint;
using Jint.Runtime;
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;

namespace NomNomzBot.Infrastructure.CustomCode.Jint;

/// <summary>
/// The self-host sandbox executor (custom-code.md §3.1, code-execution-sandbox.md §4.2). Runs compiled JS in a
/// fresh hardened Jint engine under the resource budget; the only guest surface is a primitive-in/primitive-out
/// <c>bot</c> facade whose side-effecting calls reach host code ONLY through the per-execution
/// <see cref="IScriptHostBridge"/>, capability-key-gated and host-call-budgeted. NEVER throws a sandbox escape
/// outward — every fault maps to the matching <see cref="ScriptExecutionOutcome"/> (fail-closed).
/// </summary>
public sealed partial class JintScriptExecutor : IScriptExecutor
{
    public ScriptRuntimeKind Runtime => ScriptRuntimeKind.Jint;

    // The ceiling on a single nnz.time.sleep(ms) call — comfortably covers the going use case (holding a
    // chat.send for TTS's own reported durationMs, typically 1-3s) without letting one call alone claim the
    // whole self-host baseline wall-clock budget (ScriptContracts.WallClockMs: 8000).
    private const double MaxSleepMs = 5000;

    // Builds the `bot` facade and the batteries-included `nnz` SDK (dev-platform.md §3.1) from the host
    // primitives. Host-driven Execute (not guest eval), so it is allowed under DisableStringCompilation:
    // JSON.parse, Date, and regex literals are safe builtins (not code-from-string). The `nnz` global carries
    // pure-JS batteries (no host call, no budget cost — nnz.units/time/math/str/json/random) and the typed
    // `nnz.api.*` wrappers, each a thin call over the SAME `bot.call(key, …)` capability bridge (so an ungranted
    // key still denies at run time, unchanged). `bot` stays as-is so existing scripts and the executor tests keep
    // working.
    // internal (not private): SdkScriptSurfaceDriftTests executes THIS string in a real hardened engine and
    // enumerates the globals it creates, so the generated nnz.d.ts can never again declare a member the sandbox
    // does not have (or miss one it does). InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired.
    /// <summary>How many <c>console.*</c> lines one run keeps; the rest are counted, not stored.</summary>
    internal const int MaxLogLines = 200;

    internal const string Bootstrap = """
        var console = (function () {
            function text(value) {
                if (typeof value === 'string') { return value; }
                if (value === undefined) { return 'undefined'; }
                try { var json = JSON.stringify(value); return json === undefined ? String(value) : json; }
                catch (e) { return String(value); }
            }
            function line(level) {
                return function () {
                    __log(level, Array.prototype.slice.call(arguments).map(text).join(' '));
                };
            }
            return { log: line(''), info: line(''), warn: line('warn'), error: line('error') };
        })();
        var bot = {
            args: JSON.parse(__argsJson),
            getVar: function (k) { return __getVar(String(k)); },
            setVar: function (k, v) { __setVar(String(k), String(v)); },
            send: function (m) { __send(String(m)); },
            call: function (k) {
                return __call(String(k), JSON.stringify(Array.prototype.slice.call(arguments, 1).map(String)));
            }
        };
        var nnz = {
            get lastError() { var r = __call('last.error', '[]'); return r ? JSON.parse(r) : null; },
            units: {
                convert: function (value, from, to) {
                    var v = Number(value);
                    var f = String(from).toLowerCase();
                    var t = String(to).toLowerCase();
                    var temp = { c: 1, celsius: 1, f: 1, fahrenheit: 1, k: 1, kelvin: 1 };
                    if (temp[f] && temp[t]) {
                        var celsius;
                        if (f === 'c' || f === 'celsius') { celsius = v; }
                        else if (f === 'f' || f === 'fahrenheit') { celsius = (v - 32) * 5 / 9; }
                        else { celsius = v - 273.15; }
                        if (t === 'c' || t === 'celsius') { return celsius; }
                        if (t === 'f' || t === 'fahrenheit') { return celsius * 9 / 5 + 32; }
                        return celsius + 273.15;
                    }
                    var dims = [
                        { mm: 0.001, cm: 0.01, m: 1, km: 1000, "in": 0.0254, inch: 0.0254, ft: 0.3048, foot: 0.3048, yd: 0.9144, yard: 0.9144, mi: 1609.344, mile: 1609.344 },
                        { mg: 0.001, g: 1, kg: 1000, oz: 28.349523125, lb: 453.59237, ton: 1000000 },
                        { ms: 0.001, s: 1, sec: 1, min: 60, h: 3600, hr: 3600, hour: 3600, day: 86400, week: 604800 }
                    ];
                    for (var i = 0; i < dims.length; i++) {
                        if (dims[i][f] !== undefined && dims[i][t] !== undefined) {
                            return v * dims[i][f] / dims[i][t];
                        }
                    }
                    return NaN;
                }
            },
            time: {
                now: function () { return new Date().toISOString(); },
                parse: function (iso) { return Date.parse(String(iso)); },
                format: function (epochMs) { return new Date(Number(epochMs)).toISOString(); },
                add: function (iso, ms) { return new Date(Date.parse(String(iso)) + Number(ms)).toISOString(); },
                diff: function (a, b) { return Date.parse(String(a)) - Date.parse(String(b)); },
                // A bounded, synchronous pause — NOT a callback-based setTimeout: Jint runs the script to
                // completion in one call with no event loop to invoke a callback afterward, so a script simply
                // holds here (consuming its own wall-clock budget, request.Budget.WallClockMs — no separate
                // timer, no extra thread) before its NEXT statement runs. Clamped host-side (__sleep) so one
                // call can never claim the whole budget. The going example: nnz.api.tts.speak(line) returns
                // { durationMs }, and nnz.time.sleep(result.durationMs) holds chat.send until the line is
                // actually spoken.
                sleep: function (ms) { __sleep(Number(ms)); }
            },
            math: {
                clamp: function (value, min, max) { value = Number(value); min = Number(min); max = Number(max); return value < min ? min : (value > max ? max : value); },
                round: function (value, digits) { var d = Math.pow(10, Number(digits) || 0); return Math.round(Number(value) * d) / d; },
                lerp: function (a, b, t) { return Number(a) + (Number(b) - Number(a)) * Number(t); },
                sum: function (values) { var s = 0; for (var i = 0; i < values.length; i++) { s += Number(values[i]); } return s; },
                avg: function (values) { return values.length ? nnz.math.sum(values) / values.length : 0; },
                min: function (values) { return Math.min.apply(null, values.map(Number)); },
                max: function (values) { return Math.max.apply(null, values.map(Number)); },
                randomInt: function (min, max) { min = Math.ceil(Number(min)); max = Math.floor(Number(max)); return Math.floor(Math.random() * (max - min + 1)) + min; }
            },
            str: {
                padStart: function (value, length, pad) { return String(value).padStart(Number(length), pad === undefined || pad === null ? ' ' : String(pad)); },
                padEnd: function (value, length, pad) { return String(value).padEnd(Number(length), pad === undefined || pad === null ? ' ' : String(pad)); },
                trim: function (value) { return String(value).trim(); },
                upper: function (value) { return String(value).toUpperCase(); },
                lower: function (value) { return String(value).toLowerCase(); },
                title: function (value) { return String(value).replace(/\w\S*/g, function (w) { return w.charAt(0).toUpperCase() + w.substr(1).toLowerCase(); }); },
                truncate: function (value, length, ellipsis) { value = String(value); ellipsis = ellipsis === undefined || ellipsis === null ? '…' : String(ellipsis); length = Number(length); return value.length <= length ? value : value.slice(0, Math.max(0, length - ellipsis.length)) + ellipsis; },
                slugify: function (value) { return String(value).toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, ''); },
                format: function (template, values) { return String(template).replace(/\{(\w+)\}/g, function (m, k) { return values && values[k] !== undefined ? String(values[k]) : m; }); }
            },
            json: {
                parse: function (text) { try { return JSON.parse(String(text)); } catch (e) { return null; } },
                stringify: function (value) { try { return JSON.stringify(value); } catch (e) { return 'null'; } }
            },
            random: {
                int: function (min, max) { return nnz.math.randomInt(min, max); },
                pick: function (items) { return items[Math.floor(Math.random() * items.length)]; },
                shuffle: function (items) { var a = items.slice(); for (var i = a.length - 1; i > 0; i--) { var j = Math.floor(Math.random() * (i + 1)); var tmp = a[i]; a[i] = a[j]; a[j] = tmp; } return a; },
                uuid: function () { return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) { var r = Math.random() * 16 | 0; var val = c === 'x' ? r : (r & 0x3 | 0x8); return val.toString(16); }); }
            },
            api: {
                actions: {
                    invoke: function (actionType, params, variables) {
                        var args = ['actions.invoke:' + String(actionType)];
                        var hasVariables = variables !== undefined && variables !== null;
                        if (hasVariables || (params !== undefined && params !== null))
                            args.push(params === undefined || params === null ? '' : JSON.stringify(params));
                        if (hasVariables) args.push(JSON.stringify(variables));
                        return JSON.parse(bot.call.apply(bot, args));
                    }
                },
                chat: {
                    send: function (text) { bot.call('chat.send', String(text)); },
                    reply: function (text) { bot.call('chat.reply', String(text)); }
                },
                user: {
                    get: function (id) { var r = id === undefined || id === null ? bot.call('user.get') : bot.call('user.get', String(id)); return r ? JSON.parse(r) : null; }
                },
                economy: {
                    balance: function (userId) { var r = userId === undefined || userId === null ? bot.call('economy.read') : bot.call('economy.read', String(userId)); return Number(r); }
                },
                music: {
                    queue: function (uri) { return bot.call('music.queue', String(uri)) === 'true'; },
                    nowPlaying: function () { var r = bot.call('music.nowPlaying'); return r ? JSON.parse(r) : null; }
                },
                http: {
                    fetch: function (url) { return bot.call('http.fetch', String(url)); }
                },
                storage: {
                    get: function (key) { return bot.call('storage.get', String(key)); },
                    set: function (key, value) { return bot.call('storage.set', String(key), String(value)) === 'ok'; },
                    delete: function (key) { return bot.call('storage.delete', String(key)) === 'ok'; },
                    list: function (prefix) { var r = prefix === undefined || prefix === null ? bot.call('storage.list') : bot.call('storage.list', String(prefix)); return r ? JSON.parse(r) : []; }
                },
                tts: {
                    // ratePercent/pitchPercent are optional per-call SSML prosody overrides (e.g. an "evil
                    // wizard" voice for one line) -- never persisted against the channel's TTS config. An
                    // undefined or null param is "no override" (''), never the literal "undefined"; trailing
                    // ones are dropped so a 1-arg call still sends one arg.
                    speak: function (text, voiceId, ratePercent, pitchPercent) {
                        var args = ['tts.speak', String(text)];
                        var rest = [voiceId, ratePercent, pitchPercent];
                        while (rest.length && (rest[rest.length - 1] === undefined || rest[rest.length - 1] === null)) rest.pop();
                        for (var i = 0; i < rest.length; i++) args.push(rest[i] === undefined || rest[i] === null ? '' : String(rest[i]));
                        var r = bot.call.apply(bot, args);
                        return r ? JSON.parse(r) : null;
                    },
                    getVoice: function (userIdOrLogin) { var r = bot.call('tts.voice.get', String(userIdOrLogin)); return r ? JSON.parse(r) : null; },
                    setVoice: function (userIdOrLogin, voiceId) { return bot.call('tts.voice.set', String(userIdOrLogin), voiceId === undefined || voiceId === null ? '' : String(voiceId)) === 'ok'; }
                },
                stats: {
                    viewer: function (userIdOrLogin) { var r = userIdOrLogin === undefined || userIdOrLogin === null ? bot.call('stats.viewer') : bot.call('stats.viewer', String(userIdOrLogin)); return r ? JSON.parse(r) : null; }
                },
                widget: {
                    emit: function (widgetIdOrName, eventType, data) { var r = data === undefined || data === null ? bot.call('widget.emit', String(widgetIdOrName), String(eventType)) : bot.call('widget.emit', String(widgetIdOrName), String(eventType), JSON.stringify(data)); return r === 'ok'; }
                },
                reward: {
                    get: function (rewardIdOrTitle) { var r = bot.call('reward.get', String(rewardIdOrTitle)); return r ? JSON.parse(r) : null; },
                    update: function (rewardIdOrTitle, patch) { return bot.call('reward.update', String(rewardIdOrTitle), JSON.stringify(patch)) === 'ok'; }
                },
                schedule: {
                    pipeline: function (pipelineName, delaySeconds, variables, dedupeKey) {
                        // The host schedules whole seconds; rounding up never fires a pipeline early.
                        var d = String(Math.ceil(Number(delaySeconds)));
                        var v = variables === undefined || variables === null ? '{}' : JSON.stringify(variables);
                        return (dedupeKey === undefined || dedupeKey === null
                            ? bot.call('schedule.pipeline', String(pipelineName), d, v)
                            : bot.call('schedule.pipeline', String(pipelineName), d, v, String(dedupeKey))) === 'ok';
                    }
                }
            }
        };
        """;

    public Task<Result<ScriptCompilation>> CompileAsync(
        string sourceCode,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            // Parse only — never execute (no side effects, no host imports) — to reject syntax errors at save time.
            Engine.PrepareScript(sourceCode);
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                Result.Failure<ScriptCompilation>(
                    $"Script failed to compile: {ex.Message}",
                    "VALIDATION_FAILED"
                )
            );
        }

        if (HasNonLiteralActionType(sourceCode))
            return Task.FromResult(
                Result.Failure<ScriptCompilation>(
                    "nnz.api.actions.invoke needs its action type as a string literal (for example 'tts_synthesize'): a grant is per action type, so a computed type can never be granted.",
                    "VALIDATION_FAILED"
                )
            );

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceCode)));
        return Task.FromResult(
            Result.Success(
                new ScriptCompilation(sourceCode, hash, DeclaredCapabilities(sourceCode))
            )
        );
    }

    // Heuristic save-time capability declaration: every `bot.call("key", …)` host import the script makes.
    // The broker then validates each against the catalogue + gates; an undeclared call is denied at run time.
    [GeneratedRegex("""bot\.call\(\s*["']([a-zA-Z][a-zA-Z0-9.]*)["']""")]
    private static partial Regex HostCallPattern();

    // The ergonomic `nnz.api.<group>.<method>(…)` wrappers (dev-platform.md §3.1) resolve to the SAME broker
    // catalogue keys as `bot.call`, so a script that only ever reaches for `nnz.api.*` still declares (and thus
    // is granted / denied on) the right capabilities. Captures `<group>.<method>`; the map below is the 1:1
    // wrapper→key correspondence baked into the bootstrap.
    [GeneratedRegex("""nnz\.api\.([a-zA-Z]+)\.([a-zA-Z]+)""")]
    private static partial Regex ApiCallPattern();

    // `nnz.api.actions.invoke('<type>', ...)` is granted per action type (`actions.invoke:<type>`), so the type
    // must be a string literal the save-time scan can read. Group 2 is the literal type; absent means computed.
    [GeneratedRegex(
        """nnz\.api\.actions\.invoke\s*\(\s*(?:(["'])([A-Za-z][A-Za-z0-9_.-]*)\1\s*[,)])?"""
    )]
    private static partial Regex ActionInvokePattern();

    private static bool HasNonLiteralActionType(string sourceCode) =>
        ActionInvokePattern().Matches(sourceCode).Any(m => !m.Groups[2].Success);

    private static readonly Dictionary<string, string> ApiMethodCapabilities = new(
        StringComparer.Ordinal
    )
    {
        ["chat.send"] = "chat.send",
        ["chat.reply"] = "chat.reply",
        ["user.get"] = "user.get",
        ["economy.balance"] = "economy.read",
        ["music.queue"] = "music.queue",
        ["music.nowPlaying"] = "music.nowPlaying",
        ["http.fetch"] = "http.fetch",
        ["storage.get"] = "storage.get",
        ["storage.set"] = "storage.set",
        ["storage.delete"] = "storage.delete",
        ["storage.list"] = "storage.list",
        ["tts.speak"] = "tts.speak",
        ["tts.getVoice"] = "tts.voice.get",
        ["tts.setVoice"] = "tts.voice.set",
        ["stats.viewer"] = "stats.viewer",
        ["widget.emit"] = "widget.emit",
        ["reward.get"] = "reward.get",
        ["reward.update"] = "reward.update",
        ["schedule.pipeline"] = "schedule.pipeline",
    };

    private static IReadOnlyList<string> DeclaredCapabilities(string sourceCode)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (Match match in HostCallPattern().Matches(sourceCode))
            keys.Add(match.Groups[1].Value);
        foreach (Match match in ApiCallPattern().Matches(sourceCode))
            if (
                ApiMethodCapabilities.TryGetValue(
                    $"{match.Groups[1].Value}.{match.Groups[2].Value}",
                    out string? capability
                )
            )
                keys.Add(capability);
        foreach (Match match in ActionInvokePattern().Matches(sourceCode))
            if (match.Groups[2].Success)
                keys.Add($"actions.invoke:{match.Groups[2].Value}");
        return [.. keys];
    }

    public Task<Result<ScriptExecutionOutcomeResult>> ExecuteAsync(
        ScriptExecutionRequest request,
        ScriptCapabilityGrant grant,
        IScriptHostBridge bridge,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, string> vars = new(request.Inputs.Variables, StringComparer.Ordinal);
        StringBuilder output = new();
        List<string> logLines = [];
        int droppedLogLines = 0;
        HashSet<string> grantedKeys = new(grant.Granted.Select(g => g.Key), StringComparer.Ordinal);
        int hostCalls = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        ScriptExecutionOutcome outcome;
        string? error = null;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromMilliseconds(request.Budget.WallClockMs));

        try
        {
            Engine engine = JintEngineFactory.CreateHardened(request.Budget, timeout.Token);

            engine.SetValue(
                "__getVar",
                (Func<string, string?>)(k => vars.TryGetValue(k, out string? v) ? v : null)
            );
            // string? on the value: guest code reaches __setVar directly and Jint hands a JS null straight
            // through, so the coalesce is real defence — typed non-null it read as redundant.
            engine.SetValue(
                "__setVar",
                (Action<string, string?>)((k, v) => vars[k] = v ?? string.Empty)
            );
            engine.SetValue(
                "__log",
                (Action<string, string>)(
                    (level, text) =>
                    {
                        if (logLines.Count >= MaxLogLines)
                            droppedLogLines++;
                        else
                        {
                            string line = level.Length == 0 ? text : $"{level}: {text}";
                            logLines.Add(line);
                            request.OnConsoleLine?.Invoke(line);
                        }
                    }
                )
            );
            engine.SetValue(
                "__send",
                (Action<string>)(
                    m =>
                    {
                        Append(output, m, request.Budget.MaxOutputBytes);
                        request.OnBotSend?.Invoke(m);
                    }
                )
            );
            engine.SetValue(
                "__call",
                (Func<string, string, string?>)(
                    (key, argsJson) =>
                    {
                        // Reading the script's own last error is not a capability and costs no host call.
                        bool isLastError = key == ScriptHostErrorCodes.LastErrorKey;
                        if (!isLastError && !grantedKeys.Contains(key))
                            throw new ScriptCapabilityDeniedException(key);
                        if (!isLastError && ++hostCalls > request.Budget.MaxHostCalls)
                            throw new ScriptHostBudgetException();
                        IReadOnlyList<string> a =
                            JsonConvert.DeserializeObject<List<string>>(argsJson) ?? [];
                        return bridge.Resolve(key)(key, a, timeout.Token);
                    }
                )
            );
            engine.SetValue("__argsJson", JsonConvert.SerializeObject(request.Inputs.Args));
            // Genuinely blocks the calling thread for up to MaxSleepMs — Jint has no event loop to resume a
            // suspended script on, so this is the honest cost of nnz.time.sleep, not a Task.Delay a caller can
            // await around it. Clamped so one call can never claim the whole per-execution wall-clock budget on
            // its own; Task.Delay (not Thread.Sleep) so the SAME `timeout` token that bounds the rest of the
            // execution also cuts a sleep short the instant the overall budget expires, surfacing as the usual
            // ScriptExecutionOutcome.Timeout below rather than a second, uncancellable wait.
            engine.SetValue(
                "__sleep",
                (Action<double>)(
                    ms =>
                        Task.Delay(
                                TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, MaxSleepMs)),
                                timeout.Token
                            )
                            .GetAwaiter()
                            .GetResult()
                )
            );

            engine.Execute(Bootstrap);
            engine.Execute(request.CompiledJs);
            outcome = ScriptExecutionOutcome.Success;
        }
        catch (ScriptHostBudgetException)
        {
            outcome = ScriptExecutionOutcome.HostBudgetExceeded;
            error = "Host-call budget exceeded.";
        }
        catch (ScriptCapabilityDeniedException ex)
        {
            outcome = ScriptExecutionOutcome.Denied;
            error = $"Capability denied: {ex.CapabilityKey}.";
        }
        catch (Exception ex)
            when (ex is TimeoutException or ExecutionCanceledException or OperationCanceledException
            )
        {
            outcome = ScriptExecutionOutcome.Timeout;
            error = "Execution exceeded its time budget.";
        }
        catch (Exception ex)
            when (ex
                    is StatementsCountOverflowException
                        or MemoryLimitExceededException
                        or RecursionDepthOverflowException
            )
        {
            outcome = ScriptExecutionOutcome.Faulted;
            error = "Execution exceeded a resource limit.";
        }
        catch (JavaScriptException ex)
        {
            outcome = ScriptExecutionOutcome.Faulted;
            error = ex.Message;
        }
        catch (Exception)
        {
            // Fail-closed: never surface a sandbox escape / host exception to the caller.
            outcome = ScriptExecutionOutcome.Faulted;
            error = "Script execution faulted.";
        }
        stopwatch.Stop();
        if (droppedLogLines > 0)
            logLines.Add($"{droppedLogLines} more console line(s) not shown.");

        return Task.FromResult(
            Result.Success(
                new ScriptExecutionOutcomeResult(
                    outcome,
                    stopwatch.ElapsedMilliseconds,
                    hostCalls,
                    vars,
                    output.Length == 0 ? null : output.ToString(),
                    StopPipeline: false,
                    error,
                    logLines
                )
            )
        );
    }

    private static void Append(StringBuilder output, string message, long maxBytes)
    {
        if (output.Length >= maxBytes)
            return;
        int room = (int)Math.Min(maxBytes - output.Length, message.Length);
        output.Append(message, 0, room);
    }

    private sealed class ScriptHostBudgetException : Exception;

    private sealed class ScriptCapabilityDeniedException(string capabilityKey) : Exception
    {
        public string CapabilityKey { get; } = capabilityKey;
    }
}
