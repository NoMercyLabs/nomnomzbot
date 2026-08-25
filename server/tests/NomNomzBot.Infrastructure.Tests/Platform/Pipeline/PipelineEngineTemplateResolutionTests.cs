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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application;
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

    // ─── Structural guard: ResolvesOwnTemplates must match actual source behaviour ─────────────

    /// <summary>Fixture: claims <c>ResolvesOwnTemplates =&gt; true</c> but its body never calls a resolver
    /// at all — the exact contradiction <see cref="TemplateResolutionContractScanner"/> must catch.</summary>
    private sealed class ClaimsSelfResolvingButDoesNotFixtureAction : ICommandAction
    {
        public string ActionType => "guard_fixture_claims_self_resolving";
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
        services.AddSingleton<IConfiguration>(configuration);
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
            .Count(f => f.Kind == PipelineActionFieldKind.Text && !f.Templated);

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
