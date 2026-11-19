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
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The widget surface declares <c>defineSettings</c> as a generic call, so the settings it returns are typed from
/// the declared form: the emitted definitions carry the declaration shape, the per-field value type and the call.
/// </summary>
public sealed class SdkDefineSettingsTypesTests
{
    private static string Dts() =>
        new SdkTypeEmitter(new EventCatalog())
            .EmitTypeScript(SdkContext.Widget)
            .ReplaceLineEndings("\n");

    [Fact]
    public void The_widget_sdk_declares_defineSettings_generic_in_the_declaration()
    {
        Dts()
            .Should()
            .Contain(
                "  defineSettings<const D extends NnzSettingsDeclaration>(declaration: D): NnzDeclaredSettings<D>;"
            );
    }

    [Fact]
    public void The_declared_settings_type_maps_each_key_to_its_field_value_type()
    {
        string dts = Dts();

        dts.Should()
            .Contain("interface NnzSettingsDeclaration {\n  fields: readonly NnzSettingField[];");
        dts.Should().Contain("F extends { type: 'bool' } ? boolean");
        dts.Should().Contain("F extends { type: 'number' } ? number");
        dts.Should().Contain("{ [F in D['fields'][number] as F['key']]: NnzSettingValue<F> }");
    }
}
