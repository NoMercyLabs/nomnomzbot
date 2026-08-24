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
using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

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
}
