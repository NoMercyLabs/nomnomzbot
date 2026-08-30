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

    private static string ScriptDts() =>
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
        runtime.Should().Equal("bot", "nnz");

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
}
