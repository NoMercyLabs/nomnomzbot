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

    // Builds the `bot` facade and the batteries-included `nnz` SDK (dev-platform.md §3.1) from the host
    // primitives. Host-driven Execute (not guest eval), so it is allowed under DisableStringCompilation:
    // JSON.parse, Date, and regex literals are safe builtins (not code-from-string). The `nnz` global carries
    // pure-JS batteries (no host call, no budget cost — nnz.units/time/math/str/json/random) and the typed
    // `nnz.api.*` wrappers, each a thin call over the SAME `bot.call(key, …)` capability bridge (so an ungranted
    // key still denies at run time, unchanged). `bot` stays as-is so existing scripts and the executor tests keep
    // working.
    private const string Bootstrap = """
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
                diff: function (a, b) { return Date.parse(String(a)) - Date.parse(String(b)); }
            },
            math: {
                clamp: function (v, lo, hi) { v = Number(v); lo = Number(lo); hi = Number(hi); return v < lo ? lo : (v > hi ? hi : v); },
                round: function (v, digits) { var d = Math.pow(10, Number(digits) || 0); return Math.round(Number(v) * d) / d; },
                lerp: function (a, b, t) { return Number(a) + (Number(b) - Number(a)) * Number(t); },
                sum: function (xs) { var s = 0; for (var i = 0; i < xs.length; i++) { s += Number(xs[i]); } return s; },
                avg: function (xs) { return xs.length ? nnz.math.sum(xs) / xs.length : 0; },
                min: function (xs) { return Math.min.apply(null, xs.map(Number)); },
                max: function (xs) { return Math.max.apply(null, xs.map(Number)); },
                randomInt: function (lo, hi) { lo = Math.ceil(Number(lo)); hi = Math.floor(Number(hi)); return Math.floor(Math.random() * (hi - lo + 1)) + lo; }
            },
            str: {
                padStart: function (v, n, p) { return String(v).padStart(Number(n), p === undefined ? ' ' : String(p)); },
                padEnd: function (v, n, p) { return String(v).padEnd(Number(n), p === undefined ? ' ' : String(p)); },
                trim: function (v) { return String(v).trim(); },
                upper: function (v) { return String(v).toUpperCase(); },
                lower: function (v) { return String(v).toLowerCase(); },
                title: function (v) { return String(v).replace(/\w\S*/g, function (w) { return w.charAt(0).toUpperCase() + w.substr(1).toLowerCase(); }); },
                truncate: function (v, n, e) { v = String(v); e = e === undefined ? '…' : String(e); n = Number(n); return v.length <= n ? v : v.slice(0, Math.max(0, n - e.length)) + e; },
                slugify: function (v) { return String(v).toLowerCase().trim().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, ''); },
                format: function (tpl, vals) { return String(tpl).replace(/\{(\w+)\}/g, function (m, k) { return vals && vals[k] !== undefined ? String(vals[k]) : m; }); }
            },
            json: {
                parse: function (text) { try { return JSON.parse(String(text)); } catch (e) { return null; } },
                stringify: function (value) { try { return JSON.stringify(value); } catch (e) { return 'null'; } }
            },
            random: {
                int: function (lo, hi) { return nnz.math.randomInt(lo, hi); },
                pick: function (xs) { return xs[Math.floor(Math.random() * xs.length)]; },
                shuffle: function (xs) { var a = xs.slice(); for (var i = a.length - 1; i > 0; i--) { var j = Math.floor(Math.random() * (i + 1)); var tmp = a[i]; a[i] = a[j]; a[j] = tmp; } return a; },
                uuid: function () { return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) { var r = Math.random() * 16 | 0; var val = c === 'x' ? r : (r & 0x3 | 0x8); return val.toString(16); }); }
            },
            api: {
                chat: {
                    send: function (text) { bot.call('chat.send', String(text)); },
                    reply: function (text) { bot.call('chat.reply', String(text)); }
                },
                user: {
                    get: function (id) { var r = id === undefined ? bot.call('user.get') : bot.call('user.get', String(id)); return r ? JSON.parse(r) : null; }
                },
                economy: {
                    balance: function (userId) { var r = userId === undefined ? bot.call('economy.read') : bot.call('economy.read', String(userId)); return Number(r); }
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
                    list: function (prefix) { var r = prefix === undefined ? bot.call('storage.list') : bot.call('storage.list', String(prefix)); return r ? JSON.parse(r) : []; }
                },
                tts: {
                    speak: function (text, voiceId) { var r = voiceId === undefined ? bot.call('tts.speak', String(text)) : bot.call('tts.speak', String(text), String(voiceId)); return r ? JSON.parse(r) : null; },
                    getVoice: function (userIdOrLogin) { var r = bot.call('tts.voice.get', String(userIdOrLogin)); return r ? JSON.parse(r) : null; },
                    setVoice: function (userIdOrLogin, voiceId) { return bot.call('tts.voice.set', String(userIdOrLogin), voiceId === undefined ? '' : String(voiceId)) === 'ok'; }
                },
                stats: {
                    viewer: function (userIdOrLogin) { var r = userIdOrLogin === undefined ? bot.call('stats.viewer') : bot.call('stats.viewer', String(userIdOrLogin)); return r ? JSON.parse(r) : null; }
                },
                widget: {
                    emit: function (widget, eventType, data) { var r = data === undefined ? bot.call('widget.emit', String(widget), String(eventType)) : bot.call('widget.emit', String(widget), String(eventType), JSON.stringify(data)); return r === 'ok'; }
                },
                reward: {
                    get: function (idOrTitle) { var r = bot.call('reward.get', String(idOrTitle)); return r ? JSON.parse(r) : null; },
                    update: function (idOrTitle, patch) { return bot.call('reward.update', String(idOrTitle), JSON.stringify(patch)) === 'ok'; }
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
            engine.SetValue(
                "__setVar",
                (Action<string, string>)((k, v) => vars[k] = v ?? string.Empty)
            );
            engine.SetValue(
                "__send",
                (Action<string>)(m => Append(output, m, request.Budget.MaxOutputBytes))
            );
            engine.SetValue(
                "__call",
                (Func<string, string, string?>)(
                    (key, argsJson) =>
                    {
                        if (!grantedKeys.Contains(key))
                            throw new ScriptCapabilityDeniedException(key);
                        if (++hostCalls > request.Budget.MaxHostCalls)
                            throw new ScriptHostBudgetException();
                        IReadOnlyList<string> a =
                            JsonConvert.DeserializeObject<List<string>>(argsJson) ?? [];
                        return bridge.Resolve(key)(key, a, timeout.Token);
                    }
                )
            );
            engine.SetValue("__argsJson", JsonConvert.SerializeObject(request.Inputs.Args));

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

        return Task.FromResult(
            Result.Success(
                new ScriptExecutionOutcomeResult(
                    outcome,
                    stopwatch.ElapsedMilliseconds,
                    hostCalls,
                    vars,
                    output.Length == 0 ? null : output.ToString(),
                    StopPipeline: false,
                    error
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
