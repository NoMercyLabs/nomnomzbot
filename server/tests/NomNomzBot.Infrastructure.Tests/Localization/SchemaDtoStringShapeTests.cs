// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.Tests.Localization;

/// <summary>
/// S-SCHEMA-I18N-b(a)'s structural half of the drift guard: a bare <c>string</c> property added to
/// <see cref="WidgetSettingsField"/> or <see cref="WidgetSettingsFieldOption"/> in the future — the exact way
/// <c>Label</c>/<c>Group</c>/<c>Label</c> (option) shipped as hardcoded English before this slice — fails the
/// build here, structurally, instead of shipping silently as an un-schematised English literal. Unlike
/// <see cref="SchemaLocalizationManifestTests"/> (which walks the REAL schema VALUES this class produces), this
/// test walks the DTO SHAPE via reflection: every <c>string</c>-typed property is either on the machine-value
/// allow-list (wire values compared/stored, never shown — <c>Key</c>, <c>Type</c>, <c>Value</c>) or it must be a
/// <see cref="LocalizedText"/>. A property this test cannot classify FAILS LOUD by design — the allow-list is the
/// only escape hatch, and adding to it is a deliberate, reviewable act.
///
/// <see cref="WidgetSettingsSchema.Name"/> is deliberately NOT swept here: it is the catalogue's seeded widget
/// display name (<c>FirstPartyWidgetDefinition.Name</c>, shared with <c>FirstPartyWidgetCatalogueSeeder</c>) —
/// per-tenant editable row data, not a schema-authoring literal, and the dashboard settings form does not
/// currently render it (it shows the tenant's own widget name instead). Tracked separately, not this slice.
/// </summary>
public sealed class SchemaDtoStringShapeTests
{
    // Wire/machine values: stored, compared, or used as a routing/selector key — never rendered to a human as
    // display text. Every other `string`-typed property on a swept type must be a LocalizedText.
    private static readonly IReadOnlyDictionary<
        Type,
        IReadOnlyCollection<string>
    > MachineStringPropertiesByType = new Dictionary<Type, IReadOnlyCollection<string>>
    {
        [typeof(WidgetSettingsField)] = new HashSet<string>
        {
            nameof(WidgetSettingsField.Key),
            nameof(WidgetSettingsField.Type),
        },
        [typeof(WidgetSettingsFieldOption)] = new HashSet<string>
        {
            nameof(WidgetSettingsFieldOption.Value),
        },
        [typeof(PipelineActionFieldDto)] = new HashSet<string>
        {
            nameof(PipelineActionFieldDto.Name),
            nameof(PipelineActionFieldDto.Kind),
        },
        // PipelineActionDescriptorDto.Category/Description are LocalizedText (S-SCHEMA-I18N-d, sourced from
        // ICommandAction.Category/Description) — only the machine routing key `Type` is a bare string here.
        [typeof(PipelineActionDescriptorDto)] = new HashSet<string>
        {
            nameof(PipelineActionDescriptorDto.Type),
        },
    };

    [Theory]
    [MemberData(nameof(SweptTypes))]
    public void Every_human_facing_string_property_is_a_localized_text_key(Type sweptType)
    {
        IReadOnlyCollection<string> machineProperties = MachineStringPropertiesByType[sweptType];

        List<string> bareStringProperties =
        [
            .. sweptType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(string))
                .Select(property => property.Name)
                .Where(name => !machineProperties.Contains(name)),
        ];

        bareStringProperties
            .Should()
            .BeEmpty(
                $"'{sweptType.Name}' has bare string propert{(bareStringProperties.Count == 1 ? "y" : "ies")} "
                    + $"[{string.Join(", ", bareStringProperties)}] that render to a human — either wrap "
                    + $"{(bareStringProperties.Count == 1 ? "it" : "them")} in {nameof(LocalizedText)}, or, if it "
                    + $"is genuinely a machine value never shown to a user, add it to {nameof(MachineStringPropertiesByType)}"
                    + $" for '{sweptType.Name}' in this test — never leave a rendered string un-schematised."
            );
    }

    public static TheoryData<Type> SweptTypes() => [.. MachineStringPropertiesByType.Keys];
}
