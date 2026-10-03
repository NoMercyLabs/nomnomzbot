// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Jint;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The drift guard for the script context's authored globals (<see cref="SdkRuntimeSurface"/>). It runs the REAL
/// <c>JintScriptExecutor.Bootstrap</c> in the REAL hardened engine, enumerates the globals it creates and their
/// top-level members, and holds the generated <c>nnz.d.ts</c> to exactly that set — in both directions. Without
/// this the hand-authored surface silently rotted: it declared an <c>nnz.on/once/off</c> event API the sandbox has
/// never had (calling it throws) and omitted the <c>bot</c> facade every real script is written against.
/// </summary>
public sealed partial class SdkScriptSurfaceDriftTests
{
    // A control char no JS identifier can contain, so join/split round-trips member names unambiguously.
    private const char Separator = (char)1;

    // A generous wall clock so engine construction never races the constraint on a loaded box (same rationale as
    // JintScriptExecutorTests); the bootstrap itself runs in microseconds.
    private static readonly ScriptResourceBudget Generous = ScriptResourceBudget.Baseline with
    {
        WallClockMs = 30_000,
    };

    /// <summary>
    /// An engine built exactly as <c>JintScriptExecutor.ExecuteAsync</c> builds it — same hardened factory, same
    /// five host primitives. Those primitives are plumbing, not SDK surface, so they appear in both the before and
    /// after snapshots and cancel out of the diff.
    /// </summary>
    private static Engine SandboxEngine()
    {
        Engine engine = JintEngineFactory.CreateHardened(Generous, CancellationToken.None);
        engine.SetValue("__getVar", (Func<string, string?>)(_ => null));
        engine.SetValue("__setVar", (Action<string, string>)((_, _) => { }));
        engine.SetValue("__send", (Action<string>)(_ => { }));
        engine.SetValue("__call", (Func<string, string, string?>)((_, _) => null));
        engine.SetValue("__argsJson", "[]");
        return engine;
    }

    private static List<string> Names(Engine engine, string arrayExpression) =>
        [
            .. engine
                .Evaluate($"{arrayExpression}.join(String.fromCharCode(1))")
                .AsString()
                .Split(Separator, StringSplitOptions.RemoveEmptyEntries),
        ];

    /// <summary>The globals the bootstrap introduces, each mapped to its own enumerable top-level members.</summary>
    private static Dictionary<string, List<string>> RuntimeSurface()
    {
        HashSet<string> before = new(
            Names(SandboxEngine(), "Object.getOwnPropertyNames(globalThis)"),
            StringComparer.Ordinal
        );

        Engine engine = SandboxEngine();
        engine.Execute(JintScriptExecutor.Bootstrap);

        Dictionary<string, List<string>> surface = new(StringComparer.Ordinal);
        foreach (
            string global in Names(engine, "Object.getOwnPropertyNames(globalThis)")
                .Where(n => !before.Contains(n))
        )
            surface[global] = Names(engine, $"Object.keys({global})");
        return surface;
    }

    internal static string ScriptDts() =>
        new SdkTypeEmitter(new EventCatalog()).EmitTypeScript(SdkContext.Script);

    /// <summary>Every <c>declare const &lt;name&gt;: { … }</c> block in the emitted d.ts, with its member names.</summary>
    private static Dictionary<string, List<string>> DeclaredSurface(string dts)
    {
        Dictionary<string, List<string>> declared = new(StringComparer.Ordinal);
        string? open = null;
        int depth = 0;

        foreach (string line in dts.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (open is null)
            {
                Match start = BlockStart().Match(line);
                if (!start.Success)
                    continue;
                open = start.Groups[1].Value;
                declared[open] = [];
                depth = 1;
                continue;
            }

            // Only a line at the block's own indent level is a top-level member; anything deeper belongs to a
            // nested object literal.
            if (depth == 1)
            {
                Match member = TopLevelMember().Match(line);
                if (member.Success)
                    declared[open].Add(member.Groups[1].Value);
            }

            depth += line.Count(c => c == '{') - line.Count(c => c == '}');
            if (depth <= 0)
                open = null;
        }
        return declared;
    }

    [GeneratedRegex(@"^declare const ([A-Za-z_$][A-Za-z0-9_$]*):\s*\{\s*$")]
    private static partial Regex BlockStart();

    [GeneratedRegex(@"^  (?:readonly\s+)?([A-Za-z_$][A-Za-z0-9_$]*)\s*[:(<]")]
    private static partial Regex TopLevelMember();

    [Fact]
    public void Script_dts_declares_exactly_the_globals_the_bootstrap_creates()
    {
        List<string> runtime = [.. RuntimeSurface().Keys.OrderBy(n => n, StringComparer.Ordinal)];
        List<string> declared =
        [
            .. DeclaredSurface(ScriptDts()).Keys.OrderBy(n => n, StringComparer.Ordinal),
        ];

        // Sanity: the diff really did isolate the SDK, not the whole global object.
        runtime.Should().Equal("bot", "console", "nnz");

        List<string> undeclared = [.. runtime.Except(declared, StringComparer.Ordinal)];
        List<string> phantom = [.. declared.Except(runtime, StringComparer.Ordinal)];

        undeclared
            .Should()
            .BeEmpty(
                "the script .d.ts must declare every global the sandbox bootstrap creates — undeclared: "
                    + string.Join(", ", undeclared)
            );
        phantom
            .Should()
            .BeEmpty(
                "the script .d.ts must not declare a global the sandbox does not create — phantom: "
                    + string.Join(", ", phantom)
            );
    }

    [Fact]
    public void Script_dts_declares_exactly_the_top_level_members_each_global_really_has()
    {
        Dictionary<string, List<string>> runtime = RuntimeSurface();
        Dictionary<string, List<string>> declared = DeclaredSurface(ScriptDts());

        foreach ((string global, List<string> runtimeMembers) in runtime)
        {
            declared
                .Should()
                .ContainKey(
                    global,
                    "the global itself has to be declared before its members can be"
                );

            List<string> declaredMembers = declared[global];
            List<string> undeclared =
            [
                .. runtimeMembers.Except(declaredMembers, StringComparer.Ordinal),
            ];
            List<string> phantom =
            [
                .. declaredMembers.Except(runtimeMembers, StringComparer.Ordinal),
            ];

            undeclared
                .Should()
                .BeEmpty(
                    $"the sandbox global '{global}' has members the script .d.ts never declares, so the editor "
                        + "hides them — undeclared: "
                        + string.Join(", ", undeclared)
                );
            phantom
                .Should()
                .BeEmpty(
                    $"the script .d.ts declares '{global}' members the sandbox does not have, so autocomplete "
                        + "leads straight into a TypeError — phantom: "
                        + string.Join(", ", phantom)
                );
        }
    }

    [Fact]
    public void Bot_facade_members_carry_the_signatures_the_bootstrap_actually_implements()
    {
        string dts = ScriptDts();

        // Names alone are not enough: bot.args is a value, the rest are functions, and getVar/call really can
        // return null (the host primitives behind them are Func<…, string?>).
        dts.Should().Contain("  args: string[];");
        dts.Should().Contain("  getVar(key: string): string | null;");
        dts.Should().Contain("  setVar(key: string, value: string): void;");
        dts.Should().Contain("  send(message: string): void;");
        dts.Should().Contain("  call(key: string, ...args: string[]): string | null;");
    }

    [Fact]
    public void The_sandbox_has_no_event_api_so_the_script_dts_must_not_declare_one()
    {
        Engine engine = SandboxEngine();
        engine.Execute(JintScriptExecutor.Bootstrap);

        // Ground truth first: nnz.on/once/off do not exist, so a script the editor's autocomplete leads someone
        // to write against them dies with a TypeError.
        engine.Evaluate("typeof nnz.on").AsString().Should().Be("undefined");
        engine.Evaluate("typeof nnz.once").AsString().Should().Be("undefined");
        engine.Evaluate("typeof nnz.off").AsString().Should().Be("undefined");

        string dts = ScriptDts();
        dts.Should().NotContain("once<K extends keyof NnzEventMap>");
        dts.Should()
            .NotContain(
                "on<K extends keyof NnzEventMap>",
                "the script sandbox is invoked by the run_code pipeline action with args + variables; it has no event bus"
            );
    }

    /// <summary>Every function anywhere under <c>nnz</c>, as <c>path=parameterCount</c>, read from the bootstrap.</summary>
    private static List<string> RuntimeNnzFunctions()
    {
        Engine engine = SandboxEngine();
        engine.Execute(JintScriptExecutor.Bootstrap);
        return Names(
            engine,
            """
            (function walk(o, p, out) {
              Object.keys(o).forEach(function (k) {
                var v = o[k];
                if (typeof v === 'function') out.push(p + k + '=' + v.length);
                else if (v && typeof v === 'object') walk(v, p + k + '.', out);
              });
              return out;
            })(nnz, '', [])
            """
        );
    }

    /// <summary>The same list read from the d.ts: <c>declare const nnz</c>, following each <c>Nnz*</c> member type.</summary>
    private static List<string> DeclaredNnzFunctions(string dts)
    {
        Dictionary<string, List<string>> blocks = TypeBlocks(dts);
        List<string> functions = [];
        Walk("nnz", string.Empty);
        // An overloaded method is one runtime function: count its signatures once.
        return [.. functions.Distinct(StringComparer.Ordinal)];

        void Walk(string block, string prefix)
        {
            foreach (string line in blocks[block])
            {
                Match property = PropertyMember().Match(line);
                if (property.Success && blocks.ContainsKey(property.Groups[2].Value))
                {
                    Walk(property.Groups[2].Value, prefix + property.Groups[1].Value + ".");
                    continue;
                }

                Match method = MethodMember().Match(line);
                if (method.Success)
                    functions.Add(
                        $"{prefix}{method.Groups[1].Value}={ParameterCount(line, method.Length - 1)}"
                    );
            }
        }
    }

    /// <summary>Each <c>interface X {</c> and <c>declare const x: {</c> block, with its top-level member lines.</summary>
    internal static Dictionary<string, List<string>> TypeBlocks(string dts)
    {
        Dictionary<string, List<string>> blocks = new(StringComparer.Ordinal);
        string? open = null;
        int depth = 0;

        foreach (string line in dts.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (open is null)
            {
                Match start = BlockStart().Match(line);
                if (!start.Success)
                    start = InterfaceStart().Match(line);
                if (!start.Success)
                    continue;
                open = start.Groups[1].Value;
                blocks[open] = [];
                depth = 1;
                continue;
            }

            if (depth == 1)
                blocks[open].Add(line);
            depth += line.Count(c => c == '{') - line.Count(c => c == '}');
            if (depth <= 0)
                open = null;
        }
        return blocks;
    }

    // The parameters a JS function.length counts: every top-level one before a rest parameter.
    private static int ParameterCount(string line, int openParen)
    {
        int count = 0;
        int depth = 0;
        bool inParameter = false;
        for (int i = openParen + 1; i < line.Length; i++)
        {
            char c = line[i];
            if (c is '(' or '[' or '{' or '<')
                depth++;
            else if (c is ']' or '}' || (c == '>' && line[i - 1] != '='))
                depth--;
            else if (c == ')' && depth-- == 0)
                break;
            else if (depth == 0 && c == ',')
                inParameter = false;
            else if (depth == 0 && !inParameter && !char.IsWhiteSpace(c))
            {
                if (line.AsSpan(i).StartsWith("..."))
                    break;
                inParameter = true;
                count++;
            }
        }
        return count;
    }

    [GeneratedRegex(@"^interface ([A-Za-z_$][A-Za-z0-9_$]*)\s*\{\s*$")]
    private static partial Regex InterfaceStart();

    [GeneratedRegex(@"^  (?:readonly\s+)?([A-Za-z_$][A-Za-z0-9_$]*)\??:\s*(Nnz[A-Za-z0-9_]*);$")]
    private static partial Regex PropertyMember();

    [GeneratedRegex(@"^  ([A-Za-z_$][A-Za-z0-9_$]*)\s*(?:<[^(]*>)?\s*\(")]
    private static partial Regex MethodMember();

    // A wrapper that takes four arguments but is typed with two hides the other two from the editor; one typed
    // with more than the runtime reads leads a script into an argument that is silently dropped.
    [Fact]
    public void Every_nnz_function_is_typed_with_the_parameters_the_runtime_reads()
    {
        List<string> runtime = RuntimeNnzFunctions();
        List<string> declared = DeclaredNnzFunctions(ScriptDts());

        runtime.Should().Contain("api.tts.speak=4", "the walk must reach the nested api wrappers");
        declared
            .Should()
            .BeEquivalentTo(
                runtime,
                "each nnz function's d.ts signature must take the parameters the bootstrap reads"
            );
    }

    [Fact]
    public void The_declared_last_error_codes_are_exactly_the_codes_the_host_can_raise()
    {
        Match union = Regex.Match(
            ScriptDts(),
            @"interface NnzApiError \{[^}]*?\bcode:\s*([^;]+);",
            RegexOptions.Singleline
        );
        union.Success.Should().BeTrue("the script d.ts must declare NnzApiError.code");

        string[] declared = [.. union.Groups[1].Value.Split('|').Select(c => c.Trim().Trim('\''))];
        string[] raised =
        [
            .. typeof(ScriptHostErrorCodes)
                .GetFields()
                .Where(f => f.IsLiteral && f.Name != nameof(ScriptHostErrorCodes.LastErrorKey))
                .Select(f => (string)f.GetRawConstantValue()!),
        ];

        raised.Should().HaveCount(6);
        declared.Should().BeEquivalentTo(raised);
    }

    [Fact]
    public void Every_script_surface_member_has_a_jsdoc()
    {
        List<string> missing = [];
        int total = 0;

        // Only the runtime-authored half: the generated event payload interfaces are documented by their own
        // emitter, not by SdkRuntimeSurface.
        string surface =
            SdkRuntimeSurface.ScriptApiInterfaces() + "\n" + SdkRuntimeSurface.ScriptGlobals();

        foreach ((string block, List<string> lines) in TypeBlocks(surface))
        {
            string? previous = null;
            foreach (string line in lines.Where(l => l.Trim().Length > 0))
            {
                Match member = DocumentableMember().Match(line);
                if (member.Success)
                {
                    total++;
                    if (
                        previous is null
                        || !previous.TrimEnd().EndsWith("*/", StringComparison.Ordinal)
                    )
                        missing.Add($"{block}.{member.Groups[1].Value}");
                }
                previous = line;
            }
        }

        string report =
            $"{total - missing.Count} of {total} members are documented; missing: {string.Join(", ", missing)}";
        missing.Count.Should().Be(0, report);
    }

    [GeneratedRegex(@"^  (?:readonly\s+)?([A-Za-z_$][A-Za-z0-9_$]*)\s*[?(<:]")]
    private static partial Regex DocumentableMember();

    /// <summary>One declared parameter of an SDK method: its name, whether it is optional, and its type text.</summary>
    private sealed record DeclaredParameter(string Name, bool Optional, string Type);

    // The d.ts parameters of one method line, split on the commas that sit outside any bracket. A rest
    // parameter is skipped: the wrappers it types read the arguments object, so it has no name to compare.
    private static List<DeclaredParameter> DeclaredParameters(string line, int openParen)
    {
        List<string> parts = [];
        int depth = 0;
        int start = openParen + 1;
        for (int i = start; i < line.Length; i++)
        {
            char c = line[i];
            if (c is '(' or '[' or '{' or '<')
                depth++;
            else if (c is ']' or '}' || (c == '>' && line[i - 1] != '='))
                depth--;
            else if (c == ')' && depth-- == 0)
            {
                parts.Add(line[start..i]);
                break;
            }
            else if (depth == 0 && c == ',')
            {
                parts.Add(line[start..i]);
                start = i + 1;
            }
        }

        List<DeclaredParameter> parameters = [];
        foreach (string part in parts.Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (part.StartsWith("...", StringComparison.Ordinal))
                continue;
            int colon = part.IndexOf(':');
            string name = part[..colon].Trim();
            bool optional = name.EndsWith('?');
            parameters.Add(
                new DeclaredParameter(name.TrimEnd('?'), optional, part[(colon + 1)..].Trim())
            );
        }
        return parameters;
    }

    /// <summary>Every nnz method as <c>path</c> to its d.ts member line, following each <c>Nnz*</c> member type.</summary>
    private static Dictionary<string, string> DeclaredNnzMethodLines(string dts)
    {
        Dictionary<string, List<string>> blocks = TypeBlocks(dts);
        Dictionary<string, string> methods = new(StringComparer.Ordinal);
        Walk("nnz", string.Empty);
        return methods;

        void Walk(string block, string prefix)
        {
            foreach (string line in blocks[block])
            {
                Match property = PropertyMember().Match(line);
                if (property.Success && blocks.ContainsKey(property.Groups[2].Value))
                {
                    Walk(property.Groups[2].Value, prefix + property.Groups[1].Value + ".");
                    continue;
                }

                Match method = MethodMember().Match(line);
                if (method.Success)
                    methods[prefix + method.Groups[1].Value] = line;
            }
        }
    }

    private static string PlaceholderFor(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
            return "[1]";
        if (type.StartsWith("Record<", StringComparison.Ordinal) || type.StartsWith('{'))
            return "{}";
        if (type.Contains("string", StringComparison.Ordinal))
            return "'a'";
        return "1";
    }

    [GeneratedRegex(@"^function\s*[A-Za-z0-9_$]*\s*\(([^)]*)\)")]
    private static partial Regex FunctionSignature();

    /// <summary>A bootstrapped sandbox whose host <c>__call</c> records each call as <c>key argsJson</c>.</summary>
    private static Engine RecordingEngine(List<string> hostCalls)
    {
        Engine engine = SandboxEngine();
        engine.SetValue("__sleep", (Action<double>)(_ => { }));
        engine.SetValue("__log", (Action<string, string>)((_, _) => { }));
        engine.SetValue(
            "__call",
            (Func<string, string, string?>)(
                (key, args) =>
                {
                    hostCalls.Add(key + " " + args);
                    return null;
                }
            )
        );
        engine.Execute(JintScriptExecutor.Bootstrap);
        return engine;
    }

    // Every host argument is a string the wrapper built; an optional one a script passes as null must reach the
    // host as nothing (omitted or empty), never as the text "null"/"undefined"/"NaN"/"[object Object]".
    [Fact]
    public void Every_nnz_function_called_with_null_for_each_optional_parameter_never_sends_null_text_to_the_host()
    {
        List<string> hostCalls = [];
        Engine engine = RecordingEngine(hostCalls);
        List<string> offenders = [];
        int optionalCalls = 0;

        foreach ((string path, string line) in DeclaredNnzMethodLines(ScriptDts()))
        {
            List<DeclaredParameter> parameters = DeclaredParameters(
                line,
                MethodMember().Match(line).Length - 1
            );
            if (!parameters.Any(p => p.Optional))
                continue;

            string arguments = string.Join(
                ", ",
                parameters.Select(p => p.Optional ? "null" : PlaceholderFor(p.Type))
            );
            hostCalls.Clear();
            optionalCalls++;
            try
            {
                engine.Evaluate($"nnz.{path}({arguments})");
            }
            catch (Exception)
            {
                // A placeholder the function cannot use may throw; only what reached the host matters.
            }

            foreach (string call in hostCalls)
            {
                string[] sent =
                    System.Text.Json.JsonSerializer.Deserialize<string[]>(
                        call[(call.IndexOf(' ') + 1)..]
                    ) ?? [];
                if (
                    sent.Any(a =>
                        a is "null" or "undefined" or "NaN"
                        || a.Contains("[object", StringComparison.Ordinal)
                    )
                )
                    offenders.Add($"{path}({arguments}) sent the host: {call}");
            }
        }

        optionalCalls
            .Should()
            .BeGreaterThan(5, "the walk must reach the optional-parameter methods");
        offenders.Should().BeEmpty(string.Join("\n", offenders));
    }

    // The editor shows one parameter name; a script author writing against it must reach the same argument the
    // wrapper reads, and a call that passes only the required ones must not leak "undefined" into the host.
    [Fact]
    public void Every_nnz_function_has_the_parameter_names_the_editor_shows_and_tolerates_optional_omission()
    {
        List<string> hostArguments = [];
        Engine engine = RecordingEngine(hostArguments);

        List<string> sources = Names(
            engine,
            """
            (function walk(o, p, out) {
              Object.keys(o).forEach(function (k) {
                var v = o[k];
                if (typeof v === 'function') out.push(p + k + String.fromCharCode(2) + v.toString());
                else if (v && typeof v === 'object') walk(v, p + k + '.', out);
              });
              return out;
            })(nnz, '', [])
            """
        );
        Dictionary<string, string> runtime = sources.ToDictionary(
            s => s.Split('\u0002')[0],
            s => s.Split('\u0002')[1],
            StringComparer.Ordinal
        );
        Dictionary<string, string> declared = DeclaredNnzMethodLines(ScriptDts());

        runtime.Should().ContainKey("api.tts.speak");
        List<string> mismatches = [];
        foreach ((string path, string line) in declared)
        {
            if (!runtime.TryGetValue(path, out string? source))
                continue;

            Match signature = FunctionSignature().Match(source);
            if (!signature.Success)
            {
                mismatches.Add($"{path}: the runtime source is unreadable: {source}");
                continue;
            }

            List<string> runtimeNames =
            [
                .. signature
                    .Groups[1]
                    .Value.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    ),
            ];
            List<DeclaredParameter> parameters = DeclaredParameters(
                line,
                MethodMember().Match(line).Length - 1
            );
            List<string> declaredNames = [.. parameters.Select(p => p.Name)];
            if (!runtimeNames.SequenceEqual(declaredNames, StringComparer.Ordinal))
                mismatches.Add(
                    $"{path}: runtime ({string.Join(", ", runtimeNames)}) vs editor ({string.Join(", ", declaredNames)})"
                );

            string arguments = string.Join(
                ", ",
                parameters.Where(p => !p.Optional).Select(p => PlaceholderFor(p.Type))
            );
            hostArguments.Clear();
            try
            {
                engine.Evaluate($"nnz.{path}({arguments})");
            }
            catch (Exception)
            {
                // A placeholder the function cannot use may throw; only what reached the host matters.
            }

            foreach (
                string sent in hostArguments.Where(h =>
                    h.Contains("undefined", StringComparison.Ordinal)
                    || h.Contains("NaN", StringComparison.Ordinal)
                    || h.Contains("[object", StringComparison.Ordinal)
                )
            )
                mismatches.Add($"{path}({arguments}) sent the host: {sent}");
        }

        mismatches.Should().BeEmpty(string.Join("\n", mismatches));
    }
}
