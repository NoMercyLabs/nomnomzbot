// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S045b fixture: reads a parameter (<c>surprise_key</c>) it never declares in <see cref="Fields"/> — proves
/// <see cref="PipelineActionParameterSurfaceScanner"/> catches an undeclared read that no name heuristic would
/// ever flag (S045's original guard only pattern-matches conventional names like <c>*_id</c>).
/// </summary>
internal sealed class UndeclaredParamFixtureAction : ICommandAction
{
    public string ActionType => "guard_fixture_undeclared_param";

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        string? value = action.GetString("surprise_key");
        return Task.FromResult(
            value is null ? ActionResult.Failure("missing") : ActionResult.Success()
        );
    }
}

/// <summary>
/// S045b fixture: declares a field (<c>phantom_field</c>) that <see cref="ExecuteAsync"/> never reads — proves
/// <see cref="PipelineActionParameterSurfaceScanner"/> catches a dead field lying to the dashboard's step form.
/// </summary>
internal sealed class DeadFieldFixtureAction : ICommandAction
{
    public string ActionType => "guard_fixture_dead_field";

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [new("phantom_field", PipelineActionFieldKind.Text)];

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action) =>
        Task.FromResult(ActionResult.Success());
}

/// <summary>
/// S045b fail-safe fixture: reads a parameter through a LOCAL ALIAS of <see cref="ActionDefinition.Parameters"/>
/// (<c>Dictionary&lt;string, JsonElement&gt;? p = action.Parameters; p["sneaky_key"];</c>) — an accessor shape
/// outside the scanner's known-pattern list. Proves the scanner FAILS SAFE: an unrecognised
/// <c>action.Parameters</c>/<c>action.</c> access must be a loud, named failure, never a silent pass, because
/// the read itself (<c>p["sneaky_key"]</c>) does not even mention <c>action</c> and so can never be pattern-matched
/// once aliased.
/// </summary>
internal sealed class LocalAliasIndexerFixtureAction : ICommandAction
{
    public string ActionType => "guard_fixture_local_alias_indexer";

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        Dictionary<string, JsonElement>? p = action.Parameters;
        JsonElement sneaky = p!["sneaky_key"];
        return Task.FromResult(ActionResult.Success(sneaky.ToString()));
    }
}

/// <summary>
/// S045b fail-safe fixture: reads a parameter through an EXTENSION METHOD (<c>action.ReadSneaky("other_key")</c>)
/// — a second, differently-shaped accessor outside the scanner's known-pattern list. Proves the fail-safe check
/// is not narrowly tuned to the indexer-alias shape alone: any unrecognised <c>action.&lt;member&gt;</c> access
/// must fail loud.
/// </summary>
internal static class SneakyActionDefinitionExtensions
{
    public static string? ReadSneaky(this ActionDefinition action, string key) =>
        action.GetString(key);
}

internal sealed class ExtensionMethodFixtureAction : ICommandAction
{
    public string ActionType => "guard_fixture_extension_method";

    public Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)
    {
        string? value = action.ReadSneaky("other_key");
        return Task.FromResult(
            value is null ? ActionResult.Failure("missing") : ActionResult.Success()
        );
    }
}

/// <summary>
/// S045b: locates an <see cref="ICommandAction"/>'s own source file under a repo-relative search root and
/// extracts the textual body of its class declaration. This is a plain regex/brace-count scan over the .cs
/// source text — NOT a Roslyn syntax tree (Roslyn is banned in this project, CLAUDE.md) — so it is a
/// best-effort structural check, not a full semantic one: it can be fooled by an unbalanced '{'/'}' inside a
/// string literal, which does not occur in the current action catalogue.
/// </summary>
internal static class PipelineActionSourceLocator
{
    public static string RepoRoot { get; } = ResolveRepoRoot();

    public static string? FindClassBody(string className, string searchRootRelativeToRepo)
    {
        string root = Path.Combine(RepoRoot, searchRootRelativeToRepo);
        if (!Directory.Exists(root))
            return null;

        Regex classOpen = new($@"\bclass\s+{Regex.Escape(className)}\b[^{{]*\{{");
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Match classMatch = classOpen.Match(text);
            if (!classMatch.Success)
                continue;

            int bodyStart = classMatch.Index + classMatch.Length - 1;
            int depth = 0;
            for (int i = bodyStart; i < text.Length; i++)
            {
                if (text[i] == '{')
                    depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return text[bodyStart..(i + 1)];
                }
            }
        }

        return null;
    }

    private static string ResolveRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "NomNomzBot.Infrastructure")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate the 'server' repo root above '{AppContext.BaseDirectory}'."
        );
    }
}

/// <summary>
/// S045b: compares an <see cref="ICommandAction"/>'s declared <see cref="ICommandAction.Fields"/> against the
/// parameter keys its own source actually reads (<c>ActionDefinition.GetString/GetInt/GetBool</c>, the
/// <c>ObsActionBase</c>/<c>VtsActionBase</c> <c>Param</c>/<c>TryRequire</c>/<c>GetBool</c>/<c>GetDouble</c>
/// wrappers, the <c>Music</c> module's <c>ResolveIntParam</c>/<c>ResolveStringParam</c>, the OBS
/// <c>ReadDataObject</c>/<c>WaitAction.ResolveIntAsync</c> helpers, and a direct
/// <c>action.Parameters.TryGetValue</c>) — an exhaustive, hand-verified list of every parameter-access surface
/// in the current action catalogue (S045b). This is the STRUCTURAL check: it catches an unconventionally-named
/// field the name heuristic in <see cref="PipelineActionFieldSchemaGuardTests"/> can never see.
/// </summary>
internal static class PipelineActionParameterSurfaceScanner
{
    private static readonly Regex ParamKeyAccess = new(
        string.Join(
            "|",
            @"\.(?:GetString|GetInt|GetBool|GetDouble)\s*\(\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\bGetBool\s*\(\s*action\s*,\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\bGetDouble\s*\(\s*action\s*,\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\bParam\s*\(\s*ctx\s*,\s*action\s*,\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\bTryRequire\s*\(\s*ctx\s*,\s*action\s*,\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\bResolveIntParam\s*\(\s*action\s*,\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\bResolveStringParam\s*\(\s*action\s*,\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\bReadDataObject\s*\(\s*action\s*,\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\bResolveIntAsync\s*\(\s*ctx\s*,\s*action\s*,\s*""(?<key>[A-Za-z0-9_]+)""",
            @"\.Parameters\??\.TryGetValue\s*\(\s*""(?<key>[A-Za-z0-9_]+)"""
        ),
        RegexOptions.IgnoreCase
    );

    /// <summary>
    /// Cases in the current catalogue where a literal parameter key is not inside the action's own class body
    /// but in a shared helper it delegates to: <c>SetViewerDataAction</c>/<c>AdjustViewerDataAction</c>
    /// (per-viewer-data.md §4) read <c>"target"</c> via <c>ViewerDataActionSupport.ResolveSubjectAsync</c>
    /// (same file, different class); <c>PermitAction</c>/<c>UnpermitAction</c> (roles-permissions §3.6) read
    /// <c>"target_variable"</c> via <c>PermitCommandSupport.ResolveTargetAsync</c> (different file entirely —
    /// <c>PermitCommandSupport.cs</c>). Hand-verified — there is no other cross-class parameter read in the
    /// action catalogue (S045b).
    /// </summary>
    private static readonly (Regex Call, string ImpliedKey)[] CrossClassHelperReads =
    [
        (new Regex(@"\bViewerDataActionSupport\.ResolveSubjectAsync\s*\("), "target"),
        (new Regex(@"\bPermitCommandSupport\.ResolveTargetAsync\s*\("), "target_variable"),
    ];

    /// <summary>
    /// FAIL-SAFE (S045b, second pass): every recognised parameter-access shape that hangs directly off the
    /// literal identifier <c>action</c> (<c>action.GetString(...)</c>, <c>action.Parameters is null</c>,
    /// <c>action.Parameters.TryGetValue(...)</c>). Each pattern is anchored at <c>\baction\.</c> so its match
    /// START always coincides with the start of the corresponding <see cref="AnyActionMemberAccess"/> token —
    /// that shared start position is how <see cref="FindUnclassifiedAccess"/> proves an
    /// <c>action.&lt;member&gt;</c> access is accounted for. Anything reachable off <c>action</c> that is NOT
    /// one of these shapes (a local alias of <c>action.Parameters</c>, an extension method, a differently-named
    /// helper) is deliberately NOT whitelisted here — see <see cref="FindUnclassifiedAccess"/>.
    /// </summary>
    private static readonly Regex[] SafeActionMemberAccess =
    [
        new(@"\baction\.(?:GetString|GetInt|GetBool|GetDouble)\s*\("),
        new(@"\baction\.Parameters\s*\??\s*\.\s*TryGetValue\s*\("),
        new(@"\baction\.Parameters\s*is\s+(?:not\s+)?null\b"),
    ];

    /// <summary>Any dot-access hanging off the literal identifier <c>action</c> — the full accessor surface.</summary>
    private static readonly Regex AnyActionMemberAccess = new(@"\baction\.(?<member>\w+)");

    /// <summary>
    /// FAIL-SAFE (S045b): the hole a verifier proved — <c>Dictionary&lt;string, JsonElement&gt;? p =
    /// action.Parameters; p["sneaky_key"];</c> reads a parameter through an accessor shape outside the
    /// 10-pattern list above, and the scanner reported zero violations for it. A hand-maintained accessor list
    /// can never be proven exhaustive, so instead of trusting the list, this walks EVERY
    /// <c>action.&lt;member&gt;</c> token in the class body and demands each one start at the same position as
    /// one of <see cref="SafeActionMemberAccess"/> — any token that does not is an unrecognised accessor and is
    /// a LOUD, named failure: extend the scanner or use a known accessor, never a silent pass.
    /// </summary>
    private static List<string> FindUnclassifiedAccess(
        string body,
        string actionType,
        string typeName
    )
    {
        HashSet<int> safeStarts =
        [
            .. SafeActionMemberAccess.SelectMany(p => p.Matches(body).Select(m => m.Index)),
        ];

        return
        [
            .. AnyActionMemberAccess
                .Matches(body)
                .Where(m => !safeStarts.Contains(m.Index))
                .Select(m =>
                    $"{actionType} ({typeName}) has an UNRECOGNISED accessor 'action.{m.Groups["member"].Value}' "
                    + "the scanner cannot classify — extend PipelineActionParameterSurfaceScanner or use a "
                    + "known accessor (GetString/GetInt/GetBool/GetDouble/Parameters.TryGetValue), never an "
                    + "unrecognised shape that reads a parameter invisibly"
                ),
        ];
    }

    /// <summary>
    /// Structural violations for one action: a key its source reads but does not declare in
    /// <see cref="ICommandAction.Fields"/>, a declared field its source never reads, and (fail-safe) any
    /// <c>action.&lt;member&gt;</c> accessor the scanner cannot classify at all. Returns an empty list (never
    /// null) when the action's own source file could not be located under <paramref name="searchRoot"/> —
    /// callers that require full coverage should assert non-empty results were found for every action.
    /// </summary>
    public static List<string> ComputeViolations(ICommandAction action, string searchRoot)
    {
        string typeName = action.GetType().Name;
        string? body = PipelineActionSourceLocator.FindClassBody(typeName, searchRoot);
        if (body is null)
            return
            [
                $"{action.ActionType}: could not locate source for {typeName} under {searchRoot}",
            ];

        List<string> violations = [.. FindUnclassifiedAccess(body, action.ActionType, typeName)];

        HashSet<string> readKeys =
        [
            .. ParamKeyAccess.Matches(body).Select(m => m.Groups["key"].Value),
            .. CrossClassHelperReads.Where(h => h.Call.IsMatch(body)).Select(h => h.ImpliedKey),
        ];
        HashSet<string> declaredKeys = [.. action.Fields.Select(f => f.Name)];

        foreach (string key in readKeys.Except(declaredKeys))
            violations.Add(
                $"{action.ActionType} reads '{key}' but does not declare a Fields entry for it"
            );
        foreach (string key in declaredKeys.Except(readKeys))
            violations.Add($"{action.ActionType} declares field '{key}' but never reads it");

        return violations;
    }
}

/// <summary>
/// S045 guard: proves every registered <see cref="ICommandAction"/> declares its id-shaped, enum-shaped, and
/// numeric-shaped configuration fields with a non-<see cref="PipelineActionFieldKind.Text"/> kind, so the
/// dashboard's step form can render a real picker/number/boolean/enum control instead of a free-text box
/// (commands-pipelines.md §3.13). The check is NAME-DRIVEN, not a per-action allowlist: it recognizes the
/// same naming conventions this codebase already uses for ids ("*_id"/"id"), booleans
/// ("enabled"/"active"/"relative"/"tts"/"all"/"muted"/"toggle"/"studio"/"halt_on_failure"/"visible"/"is_*"),
/// and numbers ("amount"/"bet"/"min"/"max"/"volume"/"duration"/"delta"/"step"/"number"/"r"/"g"/"b"/"a"/"x"/"y"/
/// "rotation"/"size"/"*_seconds"/"*_minutes"/"*_ms") — so a NEW action that carelessly declares one of these as
/// <c>text</c> fails this test without needing an update here.
///
/// S045b ADDS a structural check (<see cref="Every_registered_action_declares_exactly_the_parameters_it_reads"/>)
/// that is NOT name-driven: it compares each action's declared fields against the parameter keys its own source
/// actually reads, so it catches the hole the name heuristic left open — e.g. <c>RequireTierAction</c>'s
/// <c>min_tier</c>/<c>denied_message</c>, which matched no naming convention and so passed the original guard
/// while declaring no fields at all. The name heuristic stays as an additional, cheap first line of defence.
/// </summary>
public sealed class PipelineActionFieldSchemaGuardTests
{
    private static readonly Regex IdShaped = new(@"(^id$|_id$)", RegexOptions.IgnoreCase);
    private static readonly Regex NumberShaped = new(
        @"^(amount|bet|min|max|volume|duration|delta|step|number|r|g|b|a|x|y|rotation|size)$|(_seconds|_minutes|_ms)$",
        RegexOptions.IgnoreCase
    );
    private static readonly Regex BooleanShaped = new(
        @"^(enabled|active|relative|tts|all|muted|toggle|studio|halt_on_failure|visible|is_.*)$",
        RegexOptions.IgnoreCase
    );

    private static ServiceProvider BuildProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=field_schema_guard;Username=test;Password=test",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Every_registered_action_declares_id_enum_and_numeric_fields_with_a_non_text_kind()
    {
        using ServiceProvider provider = BuildProvider();
        List<ICommandAction> actions = [.. provider.GetServices<ICommandAction>()];
        actions.Should().NotBeEmpty("the assembly registers pipeline actions to check");

        List<string> violations = [];
        foreach (ICommandAction action in actions)
        foreach (PipelineActionFieldDescriptor field in action.Fields)
        {
            if (field.Kind != PipelineActionFieldKind.Text)
                continue;

            if (IdShaped.IsMatch(field.Name))
                violations.Add(
                    $"{action.ActionType}.{field.Name} looks id-shaped but is declared as 'text'"
                );
            else if (NumberShaped.IsMatch(field.Name))
                violations.Add(
                    $"{action.ActionType}.{field.Name} looks numeric but is declared as 'text'"
                );
            else if (BooleanShaped.IsMatch(field.Name))
                violations.Add(
                    $"{action.ActionType}.{field.Name} looks boolean but is declared as 'text'"
                );
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Every_enum_field_carries_a_non_empty_options_list_when_the_options_are_a_closed_static_set()
    {
        // Enum fields backed by a channel-configured/dynamic catalogue (e.g. a music repeat mode is static,
        // but a Discord trigger_type is tenant-configured) legitimately use ResourceId instead — this test only
        // asserts that a field explicitly declared Enum is never left without its option list, since an enum
        // control with no options is exactly the free-text box this slice removes.
        using ServiceProvider provider = BuildProvider();
        List<ICommandAction> actions = [.. provider.GetServices<ICommandAction>()];

        List<string> violations =
        [
            .. actions
                .SelectMany(a => a.Fields.Select(f => (a.ActionType, f)))
                .Where(x => x.f.Kind == PipelineActionFieldKind.Enum)
                .Where(x => x.f.Options is null or { Count: 0 })
                .Select(x => $"{x.ActionType}.{x.f.Name} is 'enum' with no Options"),
        ];

        violations.Should().BeEmpty();
    }

    /// <summary>
    /// S045b PART 1: structural check. For every registered action, compares its declared <c>Fields</c> against
    /// the parameter keys its own source actually reads. Unlike the name-heuristic test above, this catches an
    /// unconventionally-named field — the exact hole <c>RequireTierAction</c> fell through under S045 (its
    /// <c>min_tier</c>/<c>denied_message</c> params matched no id/number/boolean naming pattern and so the old
    /// guard passed it despite zero declared fields).
    /// </summary>
    [Fact]
    public void Every_registered_action_declares_exactly_the_parameters_it_reads()
    {
        using ServiceProvider provider = BuildProvider();
        List<ICommandAction> actions = [.. provider.GetServices<ICommandAction>()];
        actions.Should().NotBeEmpty("the assembly registers pipeline actions to check");

        List<string> violations =
        [
            .. actions.SelectMany(a =>
                PipelineActionParameterSurfaceScanner.ComputeViolations(
                    a,
                    "src/NomNomzBot.Infrastructure"
                )
            ),
        ];

        violations.Should().BeEmpty();
    }

    /// <summary>
    /// S045b: proves the structural scanner actually fails when a fixture action reads a parameter it never
    /// declared — the case a name heuristic can never catch because the fixture's key deliberately matches no
    /// id/number/boolean naming convention.
    /// </summary>
    [Fact]
    public void Structural_scanner_flags_a_read_parameter_with_no_declared_field()
    {
        UndeclaredParamFixtureAction action = new();

        List<string> violations = PipelineActionParameterSurfaceScanner.ComputeViolations(
            action,
            "tests/NomNomzBot.Infrastructure.Tests"
        );

        violations
            .Should()
            .Contain(v =>
                v.Contains("surprise_key") && v.Contains("does not declare a Fields entry")
            );
    }

    /// <summary>
    /// S045b: proves the structural scanner actually fails when a fixture action declares a field its source
    /// never reads — a dead field that would lie to the dashboard's step form.
    /// </summary>
    [Fact]
    public void Structural_scanner_flags_a_declared_field_that_is_never_read()
    {
        DeadFieldFixtureAction action = new();

        List<string> violations = PipelineActionParameterSurfaceScanner.ComputeViolations(
            action,
            "tests/NomNomzBot.Infrastructure.Tests"
        );

        violations
            .Should()
            .Contain(v => v.Contains("phantom_field") && v.Contains("never reads it"));
    }

    /// <summary>
    /// S045b FAIL-SAFE: proves an accessor shape outside the scanner's 10-pattern list — a local alias of
    /// <c>action.Parameters</c> read via an indexer (<c>Dictionary&lt;string, JsonElement&gt;? p =
    /// action.Parameters; p["sneaky_key"];</c>) — is caught as an UNRECOGNISED accessor rather than silently
    /// passing. This is the exact hole a verifier proved: the scanner previously reported zero violations for
    /// this fixture.
    /// </summary>
    [Fact]
    public void Structural_scanner_flags_an_unrecognised_local_alias_indexer_accessor()
    {
        LocalAliasIndexerFixtureAction action = new();

        List<string> violations = PipelineActionParameterSurfaceScanner.ComputeViolations(
            action,
            "tests/NomNomzBot.Infrastructure.Tests"
        );

        violations
            .Should()
            .Contain(v =>
                v.Contains("UNRECOGNISED accessor")
                && v.Contains("action.Parameters")
                && v.Contains(nameof(LocalAliasIndexerFixtureAction))
            );
    }

    /// <summary>
    /// S045b FAIL-SAFE: proves a SECOND, differently-shaped unrecognised accessor — an extension method
    /// (<c>action.ReadSneaky("other_key")</c>) — is also caught, so the fail-safe check is not narrowly tuned
    /// to the indexer-alias shape alone.
    /// </summary>
    [Fact]
    public void Structural_scanner_flags_an_unrecognised_extension_method_accessor()
    {
        ExtensionMethodFixtureAction action = new();

        List<string> violations = PipelineActionParameterSurfaceScanner.ComputeViolations(
            action,
            "tests/NomNomzBot.Infrastructure.Tests"
        );

        violations
            .Should()
            .Contain(v =>
                v.Contains("UNRECOGNISED accessor")
                && v.Contains("action.ReadSneaky")
                && v.Contains(nameof(ExtensionMethodFixtureAction))
            );
    }
}
