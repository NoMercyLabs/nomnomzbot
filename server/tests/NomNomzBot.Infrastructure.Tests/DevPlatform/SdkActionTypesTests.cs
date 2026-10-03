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
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// <c>actions.invoke</c> was the one untyped door in the SDK: any string, any params. The emitted declaration now
/// carries one <c>NnzActionParams</c> member per registered action type, built from the action's own
/// <see cref="ICommandAction.Fields"/>, and both invokes are generic over it — so a typo in the type or a missing
/// required field fails in the editor, not at run time.
/// </summary>
public sealed class SdkActionTypesTests
{
    private const string GenericInvoke =
        "invoke<T extends keyof NnzActionParams>(actionType: T, params: NnzActionParams[T],";

    private static readonly ICommandAction[] Stubs =
    [
        new StubAction(
            "stub_all_kinds",
            [
                new("name", PipelineActionFieldKind.Text, Required: true),
                new("count", PipelineActionFieldKind.Number),
                new("loud", PipelineActionFieldKind.Boolean),
                new(
                    "mode",
                    PipelineActionFieldKind.Enum,
                    Required: true,
                    Options: ["fast", "slow"]
                ),
                new("named_args", PipelineActionFieldKind.KeyValueMap),
                new("tags", PipelineActionFieldKind.Text, Repeatable: true),
                new("levels", PipelineActionFieldKind.Number, Repeatable: true),
            ]
        ),
        new StubAction("stub_no_params", []),
    ];

    private static string Emit(SdkContext context, IEnumerable<ICommandAction> actions) =>
        new SdkTypeEmitter(new EventCatalog(), null, actions).EmitTypeScript(context);

    private static string Member(string declaration, string actionType)
    {
        Match match = Regex.Match(
            declaration,
            $@"^  '{Regex.Escape(actionType)}': \{{(?<body>.*?)^  \}};",
            RegexOptions.Multiline | RegexOptions.Singleline
        );
        match.Success.Should().BeTrue($"NnzActionParams must have a member for '{actionType}'");
        return match.Groups["body"].Value;
    }

    [Theory]
    [InlineData(SdkContext.Script)]
    [InlineData(SdkContext.Widget)]
    public void Both_contexts_declare_the_generic_invoke_and_no_loose_params(SdkContext context)
    {
        string declaration = Emit(context, Stubs);

        declaration.Should().Contain("interface NnzActionParams {");
        declaration.Should().Contain(GenericInvoke);
        declaration.Should().NotContain("invoke(actionType: string");
        declaration.Should().NotContain("params?: Record<string, unknown>");
        declaration.Should().NotContain("params?: Record<string, any>");
    }

    [Fact]
    public void Field_kinds_map_to_the_type_the_action_reads()
    {
        string member = Member(Emit(SdkContext.Script, Stubs), "stub_all_kinds");

        member.Should().Contain("name: string;");
        member.Should().Contain("count?: number;");
        member.Should().Contain("loud?: boolean;");
        member.Should().Contain("mode: 'fast' | 'slow';");
        member.Should().Contain("named_args?: Record<string, string>;");
        member.Should().Contain("tags?: string[];");
        member.Should().Contain("levels?: number[];");
    }

    [Fact]
    public void An_action_without_fields_has_an_empty_params_object()
    {
        string member = Member(Emit(SdkContext.Widget, Stubs), "stub_no_params");

        member.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Params_are_optional_only_for_an_action_with_no_required_field()
    {
        string declaration = Emit(SdkContext.Script, Stubs);

        declaration
            .Should()
            .Contain("[K in keyof NnzActionParams]: {} extends NnzActionParams[K] ? K : never;");
        declaration
            .Should()
            .Contain(
                "invoke<T extends keyof NnzActionParams>(actionType: T, params: NnzActionParams[T],"
            );
        declaration
            .Should()
            .Contain(
                "invoke<T extends NnzActionsWithOptionalParams>(actionType: T, params?: NnzActionParams[T],"
            );
    }

    [Fact]
    public void Obs_switch_scene_requires_its_real_scene_field_as_a_string()
    {
        using ServiceProvider provider = BuildActionProvider();
        string member = Member(
            Emit(SdkContext.Script, provider.GetServices<ICommandAction>()),
            "obs_switch_scene"
        );

        member.Should().Contain("scene: string;");
        member.Should().NotContain("scene?:");
    }

    [Fact]
    public void Every_registered_action_type_has_exactly_one_member()
    {
        using ServiceProvider provider = BuildActionProvider();
        List<string> registered =
        [
            .. provider
                .GetServices<ICommandAction>()
                .Select(a => a.ActionType)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        string declaration = Emit(SdkContext.Script, provider.GetServices<ICommandAction>());
        int start = declaration.IndexOf("interface NnzActionParams {", StringComparison.Ordinal);
        int end = declaration.IndexOf("\n}", start, StringComparison.Ordinal);
        string block = declaration[start..end];
        List<string> members =
        [
            .. Regex
                .Matches(block, @"^  '([^']+)': \{", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value),
        ];

        registered.Count.Should().BeGreaterThan(100);
        members.Should().Equal(registered);
    }

    private static ServiceProvider BuildActionProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=sdk_action_types_test;Username=test;Password=test",
                }
            )
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = false }
        );
    }

    private sealed class StubAction : ICommandAction
    {
        public StubAction(string actionType, IReadOnlyList<PipelineActionFieldDescriptor> fields)
        {
            ActionType = actionType;
            Fields = fields;
        }

        public string ActionType { get; }
        public LocalizedText Category => new("pipeline.category.test");
        public LocalizedText Description => new("pipeline.stub.description");
        public IReadOnlyList<PipelineActionFieldDescriptor> Fields { get; }

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => throw new NotSupportedException();
    }
}
