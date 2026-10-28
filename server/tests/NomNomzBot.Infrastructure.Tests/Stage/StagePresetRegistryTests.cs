// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Infrastructure.Stage;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Stage;

/// <summary>
/// The stage presets are registered classes: the registry lists exactly the four shipped presets with their
/// defaults, the pipeline action's <c>preset</c> options come from it, and every preset name has an English and a
/// Dutch string.
/// </summary>
public sealed class StagePresetRegistryTests
{
    private static ServiceProvider BuildProvider()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=stage_registry_test;Username=test;Password=test",
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

    [Fact]
    public void The_registry_lists_the_four_presets_with_their_default_durations()
    {
        StagePresetRegistry
            .All.ToDictionary(p => p.Id, p => p.DefaultDurationMs)
            .Should()
            .Equal(
                new Dictionary<string, int>
                {
                    ["confetti"] = 4000,
                    ["flash"] = 800,
                    ["run_across"] = 6000,
                    ["shake"] = 1500,
                }
            );
        StagePresetRegistry.Ids.Should().Equal("confetti", "flash", "run_across", "shake");
    }

    [Fact]
    public void Each_preset_declares_the_payload_fields_it_reads()
    {
        StagePresetRegistry
            .All.ToDictionary(p => p.Id, p => p.Uses)
            .Should()
            .Equal(
                new Dictionary<string, StagePresetFields>
                {
                    ["confetti"] = StagePresetFields.Intensity | StagePresetFields.Color,
                    ["flash"] = StagePresetFields.Intensity | StagePresetFields.Color,
                    ["run_across"] =
                        StagePresetFields.Text
                        | StagePresetFields.Intensity
                        | StagePresetFields.Color,
                    ["shake"] = StagePresetFields.Intensity,
                }
            );
    }

    [Fact]
    public void Find_returns_the_preset_for_a_known_id_and_nothing_for_an_unknown_or_blank_one()
    {
        StagePresetRegistry.Find("shake")!.Id.Should().Be("shake");
        StagePresetRegistry.Find(" flash ")!.Id.Should().Be("flash");
        StagePresetRegistry.Find("explode").Should().BeNull();
        StagePresetRegistry.Find("  ").Should().BeNull();
        StagePresetRegistry.Find(null).Should().BeNull();
    }

    [Fact]
    public void The_stage_effect_action_in_the_container_takes_its_preset_options_from_the_registry()
    {
        using ServiceProvider provider = BuildProvider();

        ICommandAction action = provider
            .GetServices<ICommandAction>()
            .Single(a => a.ActionType == "stage_effect");
        PipelineActionFieldDescriptor preset = action.Fields.Single(f => f.Name == "preset");

        preset.Kind.Should().Be(PipelineActionFieldKind.Enum);
        preset.Required.Should().BeTrue();
        preset.Options.Should().Equal(StagePresetRegistry.Ids);
        action
            .Fields.Select(f => f.Name)
            .Should()
            .Equal("preset", "text", "duration_ms", "intensity", "color");
    }

    [Fact]
    public void Every_preset_name_has_an_english_and_a_dutch_string()
    {
        DashboardStringsXmlCatalog catalog = new();

        foreach (IStagePreset preset in StagePresetRegistry.All)
        {
            catalog.TryGetEnglish(preset.DisplayName.Key, out string en).Should().BeTrue();
            en.Should().NotBeNullOrWhiteSpace(preset.DisplayName.Key);
            catalog.TryGetDutch(preset.DisplayName.Key, out string nl).Should().BeTrue();
            nl.Should().NotBeNullOrWhiteSpace(preset.DisplayName.Key);
        }
    }
}
