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
using NomNomzBot.Application.Abstractions.Templating;
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
/// The swept SET is discovered, not listed (see <see cref="SweptTypes"/>): every DTO the backend hands out from a
/// hardcoded static catalogue is swept automatically. It used to be the hand-maintained
/// <see cref="MachineStringPropertiesByType"/> key set alone — which is precisely why
/// <see cref="EventResponsePresetDto"/> shipped English chat sentences inline in C# without this guard noticing:
/// the DTO was never registered, so it was never looked at.
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
        // The event-response preset catalog: `EventType` is the wire/routing key both sides match on; the
        // default template it publishes is a LocalizedText key (it used to be an English sentence — the exact
        // bug the DISCOVERY widening below now catches structurally).
        [typeof(EventResponsePresetDto)] = new HashSet<string>
        {
            nameof(EventResponsePresetDto.EventType),
        },
        // Template helper registry: `Key`/`Prefix` are the placeholder text the resolver matches on, not prose.
        [typeof(TemplateHelperEntry)] = new HashSet<string>
        {
            nameof(TemplateHelperEntry.Key),
            nameof(TemplateHelperEntry.Prefix),
        },
    };

    [Theory]
    [MemberData(nameof(SweptTypes))]
    public void Every_human_facing_string_property_is_a_localized_text_key(Type sweptType)
    {
        IReadOnlyCollection<string> machineProperties = MachineStringPropertiesByType.TryGetValue(
            sweptType,
            out IReadOnlyCollection<string>? declared
        )
            ? declared
            : [];

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

    /// <summary>
    /// The sweep set is OPT-OUT, not opt-in — that is the whole fix. It is the explicit
    /// <see cref="MachineStringPropertiesByType"/> keys UNION every type the backend hands out from a
    /// hardcoded STATIC CATALOG in the Application assembly (a <c>public static</c> property whose value is a
    /// code-authored instance or collection of Application types). Those are exactly the objects whose string
    /// values are written by a developer in C# rather than typed by a tenant, so every human-facing string on
    /// them must be a translation key. A new static catalog DTO is swept the moment it exists; nobody has to
    /// remember to register it here.
    /// </summary>
    public static TheoryData<Type> SweptTypes() =>
        [
            .. MachineStringPropertiesByType
                .Keys.Concat(StaticCatalogueTypes())
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal),
        ];

    private static IEnumerable<Type> StaticCatalogueTypes()
    {
        Assembly application = typeof(LocalizedText).Assembly;

        return application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsPublic: true })
            .SelectMany(type =>
                type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                    .Select(property => property.PropertyType)
            )
            .Select(ElementTypeOf)
            .OfType<Type>()
            .Where(type =>
                type.Assembly == application && type is { IsClass: true, IsPublic: true }
            )
            .Where(type =>
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(property => property.PropertyType == typeof(string))
            )
            .Distinct();
    }

    /// <summary>The DTO a static catalogue property exposes: the element type of a sequence, or the type itself.</summary>
    private static Type? ElementTypeOf(Type propertyType)
    {
        if (propertyType == typeof(string) || propertyType.IsPrimitive)
            return null;

        if (propertyType.IsGenericType)
        {
            Type[] arguments = propertyType.GetGenericArguments();
            return arguments.Length == 1 ? arguments[0] : null;
        }

        return propertyType.IsArray ? propertyType.GetElementType() : propertyType;
    }
}
