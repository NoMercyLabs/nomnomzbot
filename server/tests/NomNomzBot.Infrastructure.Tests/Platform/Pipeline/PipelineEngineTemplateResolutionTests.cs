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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NomNomzBot.Infrastructure.Webhooks.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S-PIPE-TREE-d2b(b): the central template-resolution seam in <see cref="PipelineEngine.ExecuteActionAsync"/>
/// — every <see cref="ICommandAction"/>'s <c>Templated</c>-marked fields (declared on
/// <see cref="PipelineActionFieldDescriptor"/>) get rendered exactly once, either by the engine itself (the
/// common case) or by the action, when it opts out via <see cref="ICommandAction.ResolvesOwnTemplates"/> (the
/// handful of actions — play_tts, send_message/reply, wait, schedule_pipeline, submit_media, shoutout,
/// tts_synthesize, set_viewer_data — that already called the resolver before this slice and keep doing so
/// unchanged, never double-resolved).
/// </summary>
public sealed class PipelineEngineTemplateResolutionTests
{
    private static readonly Guid Channel = Guid.Parse("019f2b00-2222-7000-8000-000000000001");

    /// <summary>Maps exact template strings to a resolved value; anything not in the map is a bug in the
    /// test (fails loudly) rather than silently echoing — so a test only asserts on templates it actually
    /// wired up.</summary>
    private sealed class MapResolver(IReadOnlyDictionary<string, string> map) : ITemplateResolver
    {
        public int CallCount { get; private set; }
        public List<string> CallsSeen { get; } = [];

        public string Resolve(string template, IDictionary<string, string> variables) =>
            map.TryGetValue(template, out string? v) ? v : template;

        public Task<string> ResolveAsync(
            string template,
            IDictionary<string, string> seedVariables,
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            CallsSeen.Add(template);
            if (!map.TryGetValue(template, out string? resolved))
                throw new InvalidOperationException(
                    $"MapResolver got an unexpected template '{template}' — wire it into the test's map."
                );
            return Task.FromResult(resolved);
        }
    }

    private static PipelineEngine CreateEngine(
        IEnumerable<ICommandAction> actions,
        ITemplateResolver resolver
    )
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext db =
            Substitute.For<NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext>();

        return new(
            db,
            registry,
            actions,
            [],
            resolver,
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );
    }

    private static PipelineRequest Request(string json) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "u1",
            TriggeredByDisplayName = "TestUser",
            PipelineJson = json,
            MessageId = "m1",
            RawMessage = "",
        };

    // ─── Done-when 1: set_variable stores the RESOLVED value ──────────────────

    [Fact]
    public async Task SetVariable_TemplatedValue_StoresTheResolvedValue_NotTheRawTemplate()
    {
        MapResolver resolver = new(
            new Dictionary<string, string> { ["{{user.name}}"] = "Stoney_Eagle" }
        );
        PipelineEngine engine = CreateEngine([new SetVariableAction()], resolver);

        const string json = /*lang=json*/
            """{"steps":[{"action":{"type":"set_variable","name":"greeting","value":"{{user.name}}"}}]}""";

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        // SetVariableAction's Output is literally "{name}={value it just stored}" — asserting on it proves
        // the RESOLVED value reached the action (and is what ctx.Variables[name] was set to), not merely
        // that the step succeeded.
        result.StepLogs.Should().ContainSingle(l => l.Output == "greeting=Stoney_Eagle");
        resolver.CallsSeen.Should().Equal("{{user.name}}");
    }

    [Fact]
    public async Task SetVariable_PlainValue_WithNoPlaceholders_ResolvesToItself()
    {
        // No template markers at all — the resolver's contract is to hand plain text straight back, so
        // the map only needs the identity entry; this proves the central pass runs uniformly (every
        // Templated field goes through ResolveAsync once) rather than skipping plain-looking text on a
        // guess, which would itself be an inconsistent special case.
        MapResolver resolver = new(new Dictionary<string, string> { ["plain"] = "plain" });
        PipelineEngine engine = CreateEngine([new SetVariableAction()], resolver);

        const string json = /*lang=json*/
            """{"steps":[{"action":{"type":"set_variable","name":"x","value":"plain"}}]}""";

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        result.StepLogs.Should().ContainSingle(l => l.Output == "x=plain");
        resolver.CallCount.Should().Be(1);
    }

    // ─── Done-when 3: a deliberately non-templating field passes "{{" through unchanged ───────

    [Fact]
    public async Task SendWebhook_EventType_IsDeliberatelyLiteral_PassesDoubleBraceThroughUnchanged()
    {
        IOutboundWebhookDispatcher dispatcher = Substitute.For<IOutboundWebhookDispatcher>();
        Guid endpointId = Guid.NewGuid();
        string? capturedEventType = null;
        dispatcher
            .EnqueueForEndpointAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Do<string>(et => capturedEventType = et),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new OutboundEnqueueResult(
                        endpointId,
                        Guid.NewGuid(),
                        1,
                        WebhookDeliveryStatus.Pending
                    )
                )
            );

        // Empty map: the resolver throws if the engine calls it with ANYTHING — proving event_type's raw
        // "{{not_a_real_template}}" text never reaches ITemplateResolver at all.
        MapResolver resolver = new(new Dictionary<string, string>());
        PipelineEngine engine = CreateEngine([new SendWebhookAction(dispatcher)], resolver);

        string json = /*lang=json*/
            """
            {"steps":[{"action":{"type":"send_webhook","endpoint":"__ENDPOINT_ID__","event_type":"{{not_a_real_template}}"}}]}
            """.Replace("__ENDPOINT_ID__", endpointId.ToString());

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        resolver
            .CallCount.Should()
            .Be(0, "event_type is declared Templated: false — the engine must never resolve it");
        capturedEventType
            .Should()
            .Be(
                "{{not_a_real_template}}",
                "a field declared non-templating must reach the dispatcher byte-for-byte, braces included"
            );
    }

    // ─── Done-when 5: no double-render for actions that already self-resolve ──

    [Fact]
    public async Task SendMessage_AlreadySelfResolving_EngineNeverResolvesItAgain()
    {
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Contains a literal "{{" the resolver would mangle on a SECOND pass (an unknown-to-the-map
        // template throws) — proving the engine's central pass never touches this field at all, only
        // SendMessageAction's own single internal resolve call does.
        MapResolver resolver = new(
            new Dictionary<string, string>
            {
                ["{{user.name}} said {{literal}}"] = "Stoney said {{literal}}",
            }
        );
        PipelineEngine engine = CreateEngine(
            [new NomNomzBot.Infrastructure.Chat.PipelineActions.SendMessageAction(chat, resolver)],
            resolver
        );

        const string json = /*lang=json*/
            """{"steps":[{"action":{"type":"send_message","message":"{{user.name}} said {{literal}}"}}]}""";

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        resolver
            .CallCount.Should()
            .Be(
                1,
                "resolved once by the action itself; a second (engine) pass would have thrown on the literal \"{{literal}}\" left in the resolved text"
            );
        await chat.Received(1)
            .SendMessageAsync(Channel, "Stoney said {{literal}}", Arg.Any<CancellationToken>());
    }

    // ─── Templated Text field carrying raw JSON (S-STREAMDECK-OBS-REMAINDER) ──────────────────

    /// <summary>Fixture: a single Templated Text field whose resolved <see cref="JsonElement"/> is
    /// captured verbatim, so a test can assert on its <see cref="JsonValueKind"/> and shape — this is
    /// what <c>obs_request</c>'s/<c>obs_call_vendor</c>'s <c>request_data</c> and
    /// <c>obs_request_batch</c>'s <c>requests</c> fields are (a plain Text field, no dedicated "raw JSON"
    /// <see cref="PipelineActionFieldKind"/> exists) so the engine's resolution seam must preserve
    /// array/object shape rather than always flattening to a JSON string.</summary>
    private sealed class CapturesResolvedElementFixtureAction : ICommandAction
    {
        public string ActionType => "guard_fixture_captures_resolved_element";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("value", PipelineActionFieldKind.Text, Templated: true)];

        public JsonElement? CapturedElement { get; private set; }

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            if (
                action.Parameters is not null
                && action.Parameters.TryGetValue("value", out JsonElement el)
            )
                CapturedElement = el.Clone();
            return Task.FromResult(ActionResult.Success("ok"));
        }
    }

    [Fact]
    public async Task TemplatedTextField_ConfiguredValueIsAJsonArray_ResolvesToARealJsonArray_NotAString()
    {
        // No placeholders at all — the resolver's contract hands plain text straight back (see the
        // "plain value" test above), so the literal JSON array text is what ResolveAsync returns.
        const string arrayJson = """[{"a":1},{"a":2}]""";
        MapResolver resolver = new(new Dictionary<string, string> { [arrayJson] = arrayJson });
        CapturesResolvedElementFixtureAction fixtureAction = new();
        PipelineEngine engine = CreateEngine([fixtureAction], resolver);

        string json = /*lang=json*/
            """{"steps":[{"action":{"type":"guard_fixture_captures_resolved_element","value":__VALUE__}}]}""".Replace(
                "__VALUE__",
                JsonSerializer.Serialize(arrayJson)
            );

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        fixtureAction.CapturedElement.Should().NotBeNull();
        fixtureAction.CapturedElement!.Value.ValueKind.Should().Be(JsonValueKind.Array);
        fixtureAction.CapturedElement!.Value.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task TemplatedTextField_PlainNonJsonTemplate_StillResolvesToAJsonString_Regression()
    {
        MapResolver resolver = new(
            new Dictionary<string, string> { ["hello {{user.name}}"] = "hello Stoney_Eagle" }
        );
        CapturesResolvedElementFixtureAction fixtureAction = new();
        PipelineEngine engine = CreateEngine([fixtureAction], resolver);

        const string json = /*lang=json*/
            """{"steps":[{"action":{"type":"guard_fixture_captures_resolved_element","value":"hello {{user.name}}"}}]}""";

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        fixtureAction.CapturedElement.Should().NotBeNull();
        fixtureAction.CapturedElement!.Value.ValueKind.Should().Be(JsonValueKind.String);
        fixtureAction.CapturedElement!.Value.GetString().Should().Be("hello Stoney_Eagle");
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    public async Task TemplatedTextField_ResolvedTextIsABareScalar_StaysAJsonString_NeverBecomesNumberOrBool(
        string scalarText
    )
    {
        // A resolved value of "42" or "true" must NOT silently become the JSON number 42 or the JSON
        // bool true — only array/object roots opt into re-parsing.
        MapResolver resolver = new(new Dictionary<string, string> { [scalarText] = scalarText });
        CapturesResolvedElementFixtureAction fixtureAction = new();
        PipelineEngine engine = CreateEngine([fixtureAction], resolver);

        string json = /*lang=json*/
            """{"steps":[{"action":{"type":"guard_fixture_captures_resolved_element","value":__VALUE__}}]}""".Replace(
                "__VALUE__",
                JsonSerializer.Serialize(scalarText)
            );

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        fixtureAction.CapturedElement.Should().NotBeNull();
        fixtureAction.CapturedElement!.Value.ValueKind.Should().Be(JsonValueKind.String);
        fixtureAction.CapturedElement!.Value.GetString().Should().Be(scalarText);
    }

    /// <summary>Fixture: a Repeatable Templated Text field (the <c>obs_request_batch.requests</c>/
    /// <c>run_pipeline.args</c> shape — the builder stores it as a JSON array of per-item strings, each
    /// resolved independently by the engine's <c>Array</c> case) — captures the resolved array element
    /// verbatim so a test can assert each item's shape survived.</summary>
    private sealed class CapturesResolvedRepeatableFixtureAction : ICommandAction
    {
        public string ActionType => "guard_fixture_captures_resolved_repeatable";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("items", PipelineActionFieldKind.Text, Repeatable: true, Templated: true)];

        public JsonElement? CapturedElement { get; private set; }

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            if (
                action.Parameters is not null
                && action.Parameters.TryGetValue("items", out JsonElement el)
            )
                CapturedElement = el.Clone();
            return Task.FromResult(ActionResult.Success("ok"));
        }
    }

    [Fact]
    public async Task RepeatableTemplatedTextField_OneItemIsJsonObjectText_ThatItemResolvesToARealObject()
    {
        // Mirrors obs_request_batch's real storage shape: "requests" is Repeatable Text, so the
        // dashboard stores it as an array of per-item strings — one per repeated input — not one big
        // JSON-array string. Only the second item happens to be JSON object syntax; the first is plain
        // text and must be untouched (regression guard within the same array).
        const string objectItemJson = """{"request_type":"StartRecord"}""";
        MapResolver resolver = new(
            new Dictionary<string, string>
            {
                ["plain"] = "plain",
                [objectItemJson] = objectItemJson,
            }
        );
        CapturesResolvedRepeatableFixtureAction fixtureAction = new();
        PipelineEngine engine = CreateEngine([fixtureAction], resolver);

        string json = /*lang=json*/
            """{"steps":[{"action":{"type":"guard_fixture_captures_resolved_repeatable","items":["plain",__ITEM__]}}]}""".Replace(
                "__ITEM__",
                JsonSerializer.Serialize(objectItemJson)
            );

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        fixtureAction.CapturedElement.Should().NotBeNull();
        JsonElement array = fixtureAction.CapturedElement!.Value;
        array.ValueKind.Should().Be(JsonValueKind.Array);
        array[0].ValueKind.Should().Be(JsonValueKind.String, "the plain item must stay untouched");
        array[0].GetString().Should().Be("plain");
        array[1]
            .ValueKind.Should()
            .Be(
                JsonValueKind.Object,
                "before the fix every array item was flattened to a JSON string regardless of its own shape, which is exactly what broke obs_request_batch's per-item request objects"
            );
        array[1].GetProperty("request_type").GetString().Should().Be("StartRecord");
    }

    // ─── Structural guard: ResolvesOwnTemplates must match actual source behaviour ─────────────

    /// <summary>Fixture: claims <c>ResolvesOwnTemplates =&gt; true</c> but its body never calls a resolver
    /// at all — the exact contradiction <see cref="TemplateResolutionContractScanner"/> must catch.</summary>
    private sealed class ClaimsSelfResolvingButDoesNotFixtureAction : ICommandAction
    {
        public string ActionType => "guard_fixture_claims_self_resolving";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public bool ResolvesOwnTemplates => true;

        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("value", PipelineActionFieldKind.Text, Templated: true)];

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success(action.GetString("value")));
    }

    /// <summary>Fixture: leaves <c>ResolvesOwnTemplates</c> at its default (false) yet calls a resolver
    /// directly in its body — the engine will ALSO try to resolve any of its Templated fields, double
    /// rendering. <see cref="TemplateResolutionContractScanner"/> must catch this too.</summary>
    private sealed class SilentlySelfResolvesFixtureAction(ITemplateResolver resolver)
        : ICommandAction
    {
        public string ActionType => "guard_fixture_silently_self_resolves";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
            [new("value", PipelineActionFieldKind.Text, Templated: true)];

        public async Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            string resolved = await resolver.ResolveAsync(
                action.GetString("value") ?? string.Empty,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
            return ActionResult.Success(resolved);
        }
    }

    /// <summary>
    /// S-PIPE-TREE-d2b(b) structural check: a plain regex/brace-count scan (Roslyn is banned, CLAUDE.md) over
    /// each <see cref="ICommandAction"/>'s own class body — reuses <see cref="PipelineActionSourceLocator"/>
    /// from the field-schema guard (S045b) — proving <see cref="ICommandAction.ResolvesOwnTemplates"/> is
    /// truthful: <c>true</c> requires at least one <c>.ResolveAsync(</c> call in the body (it actually
    /// resolves something itself); <c>false</c> (the default) requires ZERO such calls (nothing here silently
    /// resolves a field the engine will also try to resolve, corrupting it on a second pass).
    /// </summary>
    private static class TemplateResolutionContractScanner
    {
        // A call named ResolveAsync is NOT enough: `MusicUnsaveTrackAction` calls
        // `MusicSaveTrackAction.ResolveAsync(_music, ctx)`, which resolves the current TRACK and has
        // nothing to do with templates. Flagging it as double-resolution and "fixing" the action by
        // declaring ResolvesOwnTemplates => true would SKIP the central pass and break its templating —
        // pinning a general mechanism to one mis-detected instance.
        // Receiver NAMES vary (`templates`, `_resolver`, `_templateResolver`), so matching those would be
        // another hand-maintained list. Ground it in the TYPE instead: self-resolution requires the
        // ITemplateResolver dependency AND a ResolveAsync call in the body.
        private static readonly Regex ResolveCall = new(@"\.\s*ResolveAsync\s*\(");

        // Reflection, not text: `SetViewerDataAction` takes ITemplateResolver via a PRIMARY CONSTRUCTOR,
        // which lives in the class DECLARATION rather than the body a source scan extracts. The type
        // system knows the dependency regardless of where it is declared.
        private static bool TakesTemplateResolver(ICommandAction action) =>
            action
                .GetType()
                .GetConstructors()
                .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ITemplateResolver)));

        public static List<string> ComputeViolations(ICommandAction action, string searchRoot)
        {
            string typeName = action.GetType().Name;
            string? body = PipelineActionSourceLocator.FindClassBody(typeName, searchRoot);
            if (body is null)
                return
                [
                    $"{action.ActionType}: could not locate source for {typeName} under {searchRoot}",
                ];

            bool callsResolver = ResolveCall.IsMatch(body) && TakesTemplateResolver(action);

            if (action.ResolvesOwnTemplates && !callsResolver)
                return
                [
                    $"{action.ActionType} ({typeName}) declares ResolvesOwnTemplates => true but its body "
                        + "never calls .ResolveAsync( — it claims to self-resolve but resolves nothing",
                ];

            if (!action.ResolvesOwnTemplates && callsResolver)
                return
                [
                    $"{action.ActionType} ({typeName}) calls .ResolveAsync( in its body but does not "
                        + "declare ResolvesOwnTemplates => true — any Templated field it declares will be "
                        + "resolved a SECOND time by the engine's central pass, corrupting a literal '{{' "
                        + "a user typed into the first resolved value",
                ];

            return [];
        }
    }

    private static ServiceProvider BuildProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=template_resolution_guard;Username=test;Password=test",
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

    /// <summary>
    /// Enumerates every registered <see cref="ICommandAction"/> structurally (assembly-scanned, never a
    /// hand-list) and buckets it: ALREADY TEMPLATING (self-resolving, unchanged by this slice), NEWLY
    /// TEMPLATING (a Templated field the engine now resolves centrally — the systemic gap this slice
    /// closes), or carries no templated fields at all. Also runs the contradiction scanner over the real
    /// catalogue and demands zero violations.
    /// </summary>
    [Fact]
    public void Every_registered_actions_ResolvesOwnTemplates_matches_its_actual_source_behaviour()
    {
        using ServiceProvider provider = BuildProvider();
        List<ICommandAction> actions = [.. provider.GetServices<ICommandAction>()];
        actions.Should().NotBeEmpty("the assembly registers pipeline actions to check");

        int alreadyTemplating = actions.Count(a => a.ResolvesOwnTemplates);
        int newlyTemplating = actions.Count(a =>
            !a.ResolvesOwnTemplates && a.Fields.Any(f => f.Templated)
        );
        int deliberatelyLiteralTextFields = actions
            .SelectMany(a => a.Fields)
            .Count(f => f is { Kind: PipelineActionFieldKind.Text, Templated: false });

        alreadyTemplating.Should().BeGreaterThan(0);
        newlyTemplating.Should().BeGreaterThan(0);
        deliberatelyLiteralTextFields.Should().BeGreaterThan(0);

        List<string> violations =
        [
            .. actions.SelectMany(a =>
                TemplateResolutionContractScanner.ComputeViolations(
                    a,
                    "src/NomNomzBot.Infrastructure"
                )
            ),
        ];
        violations.Should().BeEmpty();
    }

    [Fact]
    public void Scanner_flags_an_action_that_claims_ResolvesOwnTemplates_but_never_calls_the_resolver()
    {
        ClaimsSelfResolvingButDoesNotFixtureAction action = new();

        List<string> violations = TemplateResolutionContractScanner.ComputeViolations(
            action,
            "tests/NomNomzBot.Infrastructure.Tests"
        );

        violations.Should().Contain(v => v.Contains("resolves nothing"));
    }

    [Fact]
    public void Scanner_flags_an_action_that_silently_calls_the_resolver_without_declaring_it()
    {
        SilentlySelfResolvesFixtureAction action = new(Substitute.For<ITemplateResolver>());

        List<string> violations = TemplateResolutionContractScanner.ComputeViolations(
            action,
            "tests/NomNomzBot.Infrastructure.Tests"
        );

        violations.Should().Contain(v => v.Contains("resolved a SECOND time"));
    }

    /// <summary>Proves the fixture from the guard test above actually double-renders when run through the
    /// real engine — not just a static-scan claim, but the observable corruption itself: a literal "{{"
    /// left by the FIRST resolve gets mangled (thrown on, here) by the engine's second pass.</summary>
    [Fact]
    public async Task SilentlySelfResolvingFixture_ActuallyDoubleRendersThroughTheRealEngine()
    {
        MapResolver resolver = new(
            new Dictionary<string, string> { ["{{outer}}"] = "resolved-once-{{still-braced}}" }
        );
        SilentlySelfResolvesFixtureAction fixtureAction = new(resolver);
        PipelineEngine engine = CreateEngine([fixtureAction], resolver);

        const string json = /*lang=json*/
            """{"steps":[{"action":{"type":"guard_fixture_silently_self_resolves","value":"{{outer}}"}}]}""";

        PipelineExecutionResult result = await engine.ExecuteAsync(Request(json));

        // The engine's central pass ALSO resolves "value" (Templated: true) before ever calling
        // ExecuteAsync, so the fixture's own internal resolve runs on an ALREADY-resolved string
        // ("resolved-once-{{still-braced}}") that the MapResolver has no entry for — the double-render
        // surfaces as exactly the failure a real corrupted-literal-brace bug would produce.
        result.Outcome.Should().Be(PipelineOutcome.PartiallyFailed);
        resolver.CallCount.Should().Be(2, "resolved once centrally, once again inside the fixture");
    }
}
