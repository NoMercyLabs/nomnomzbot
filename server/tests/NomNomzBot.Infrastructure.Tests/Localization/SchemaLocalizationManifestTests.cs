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
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Localization;

/// <summary>
/// S-SCHEMA-I18N-redesign's two-part drift guard, backend half:
///
/// (a) Every <see cref="LocalizedText"/> KEY the backend actually emits today — every first-party widget
///     settings field's Label/Help (<see cref="WidgetSettingsSchemaProvider.GetAll"/>) and every
///     <see cref="ICommandAction"/> field's optional Description, resolved from the REAL production DI graph
///     (same assembly scan the app boots with) — must equal the committed key manifest
///     (<c>server/i18n/schema-i18n-keys.manifest.json</c>) exactly. A key emitted here but missing from the
///     manifest, or a manifest key no longer emitted, fails loud with the exact diff — regenerate the manifest
///     (see <see cref="RegenerateManifestEnvVar"/>) as part of the same change that added/removed the key.
///
/// (b) Every key in the manifest has a non-blank <c>en</c> AND <c>nl</c> entry in the dashboard's committed
///     translation files (<see cref="DashboardStringsXmlCatalog"/>) — so a key can never ship with only English,
///     or with neither language at all.
///
/// Nothing here is a hand-written list of expected strings: both the backend key set and the translation lookup
/// walk the REAL schema/DI graph and the REAL committed strings.xml files, so the guard cannot drift silently.
/// The Kotlin-side counterpart (<c>SchemaLocalizationManifestTest.kt</c>) reads the same manifest file to prove
/// the resource NAMES it derives (dots → underscores) actually resolve through the real Compose Resources
/// pipeline, for both languages.
/// </summary>
public sealed class SchemaLocalizationManifestTests
{
    // Set to any non-empty value and run this test to regenerate the committed manifest from the real schema —
    // e.g. `REGENERATE_SCHEMA_I18N_MANIFEST=1 dotnet test --filter SchemaLocalizationManifestTests`. Never set in
    // CI; the whole point of the guard is that CI compares against what is already committed.
    private const string RegenerateManifestEnvVar = "REGENERATE_SCHEMA_I18N_MANIFEST";
    private const string ManifestRelativePath = "server/i18n/schema-i18n-keys.manifest.json";

    [Fact]
    public void Real_schema_keys_match_the_committed_manifest_exactly()
    {
        IReadOnlyList<string> actualKeys = CollectRealKeys();
        string manifestPath = ResolveManifestPath();

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RegenerateManifestEnvVar)))
        {
            File.WriteAllText(manifestPath, ManifestJson(actualKeys));
            return;
        }

        IReadOnlyList<string> manifestKeys = ReadManifest(manifestPath);

        List<string> missingFromManifest = [.. actualKeys.Except(manifestKeys).OrderBy(k => k)];
        List<string> goneFromSchema = [.. manifestKeys.Except(actualKeys).OrderBy(k => k)];

        if (missingFromManifest.Count > 0 || goneFromSchema.Count > 0)
        {
            missingFromManifest
                .Should()
                .BeEmpty(
                    "the schema emits keys the manifest doesn't know about — regenerate "
                        + $"{ManifestRelativePath} (set {RegenerateManifestEnvVar}=1 and rerun this test), "
                        + $"missing: {string.Join(", ", missingFromManifest)}"
                );
            goneFromSchema
                .Should()
                .BeEmpty(
                    "the manifest has keys the schema no longer emits — regenerate "
                        + $"{ManifestRelativePath}, stale: {string.Join(", ", goneFromSchema)}"
                );
        }
    }

    [Fact]
    public void Every_manifest_key_has_both_english_and_dutch_translations()
    {
        DashboardStringsXmlCatalog catalog = new();
        IReadOnlyList<string> manifestKeys = ReadManifest(ResolveManifestPath());

        manifestKeys.Should().NotBeEmpty("the manifest should be regenerated from the real schema");

        foreach (string key in manifestKeys)
        {
            catalog
                .TryGetEnglish(key, out string en)
                .Should()
                .BeTrue($"'{key}' needs an English entry in values/strings.xml");
            en.Should().NotBeNullOrWhiteSpace($"'{key}' needs non-blank English text");

            catalog
                .TryGetDutch(key, out string nl)
                .Should()
                .BeTrue($"'{key}' needs a Dutch entry in values-nl/strings.xml");
            nl.Should().NotBeNullOrWhiteSpace($"'{key}' needs non-blank Dutch text");
        }
    }

    private static IReadOnlyList<string> CollectRealKeys()
    {
        List<string> keys = [];

        WidgetSettingsSchemaProvider widgetSchemas = new();
        foreach (WidgetSettingsSchema schema in widgetSchemas.GetAll())
        foreach (WidgetSettingsField field in schema.Fields)
        {
            keys.Add(field.Label.Key);
            keys.Add(field.Group.Key);
            if (field.Help is not null)
                keys.Add(field.Help.Key);
            if (field.Options is not null)
                foreach (WidgetSettingsFieldOption option in field.Options)
                    keys.Add(option.Label.Key);
        }

        using ServiceProvider provider = BuildActionProvider();
        foreach (ICommandAction action in provider.GetServices<ICommandAction>())
        {
            keys.Add(action.Category.Key);
            keys.Add(action.Description.Key);
            foreach (PipelineActionFieldDescriptor field in action.Fields)
            {
                if (field.Description is not null)
                    keys.Add(field.Description.Key);
            }
        }

        // Event-response preset default templates: the dashboard pre-fills the message input from these, so the
        // catalog serves a translation KEY per event type — never the English sentence it used to inline.
        foreach (EventResponsePresetDto preset in EventResponsePresetCatalog.Presets)
            keys.Add(preset.DefaultTemplate.Key);

        // S042: every template helper registry entry's description key, same real-schema-walk contract.
        foreach (TemplateHelperEntry helper in TemplateHelperRegistry.All)
            keys.Add(helper.Description.Key);

        return [.. keys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal)];
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
                        "Host=localhost;Database=i18n_manifest_test;Username=test;Password=test",
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

    private static string ManifestJson(IReadOnlyList<string> keys) =>
        JsonSerializer.Serialize(new { keys }, new JsonSerializerOptions { WriteIndented = true })
        + "\n";

    private static IReadOnlyList<string> ReadManifest(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Schema i18n key manifest not found: '{path}'.", path);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return
        [
            .. document
                .RootElement.GetProperty("keys")
                .EnumerateArray()
                .Select(element => element.GetString()!),
        ];
    }

    private static string ResolveManifestPath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, ManifestRelativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate '{ManifestRelativePath}' above '{AppContext.BaseDirectory}'."
        );
    }
}
