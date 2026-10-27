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
using Jint;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.DevPlatform;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The editor's SDK side view is built from the declaration text the editor type-checks against. These tests run
/// the shipped module (<c>Assets/editor/sdk-reference.js</c>) on the real generated SDK text, so every call the
/// view lists is one the SDK declares, with its own signature and doc.
/// </summary>
public sealed class EditorSdkReferenceTests
{
    private static string Run(string dts, string expression)
    {
        Engine engine = new();
        engine.SetValue("DTS", dts);
        engine.Modules.Add(
            "ref",
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "editor", "sdk-reference.js")
            )
        );
        engine.Modules.Add(
            "test",
            "import * as r from 'ref'; export const out = " + expression + ";"
        );
        return engine.Modules.Import("test").Get("out").ToString();
    }

    private static string ScriptSdk() =>
        SdkRuntimeSurface.ScriptApiInterfaces() + "\n" + SdkRuntimeSurface.ScriptGlobals();

    private static JsonElement Find(string json, string path)
    {
        JsonElement node = JsonDocument.Parse(json).RootElement;
        node.GetProperty("path").GetString().Should().Be(path);
        return node;
    }

    [Fact]
    public void A_script_lists_nnz_random_pick_with_its_real_signature_and_doc()
    {
        string json = Run(
            ScriptSdk(),
            """
            (function () {
                const nnz = r.buildSdkReference(DTS).find((n) => n.name === 'nnz');
                const random = nnz.children.find((n) => n.name === 'random');
                return JSON.stringify(random.children.find((n) => n.name === 'pick'));
            })()
            """
        );

        JsonElement pick = Find(json, "nnz.random.pick");
        pick.GetProperty("kind").GetString().Should().Be("method");
        pick.GetProperty("signatures")[0]
            .GetString()
            .Should()
            .Be("pick<T>(items: readonly T[]): T");
        pick.GetProperty("doc").GetString().Should().Contain("A random item of the list");
    }

    [Fact]
    public void Every_global_and_nested_namespace_of_the_script_sdk_is_a_root_or_branch()
    {
        string result = Run(
            ScriptSdk(),
            """
            (function () {
                const tree = r.buildSdkReference(DTS);
                const nnz = tree.find((n) => n.name === 'nnz');
                const api = nnz.children.find((n) => n.name === 'api');
                const user = api.children.find((n) => n.name === 'user');
                return JSON.stringify([
                    tree.map((n) => n.name).sort(),
                    nnz.children.filter((n) => n.kind === 'namespace').map((n) => n.name),
                    user.children.map((n) => n.path),
                ]);
            })()
            """
        );

        result
            .Should()
            .StartWith(
                """[["bot","console","nnz"],["units","time","math","str","json","random","template","api"],["nnz.api.user.get","nnz.api.user.lookup"]]"""
            );
    }

    [Fact]
    public void Overloads_share_one_entry_and_a_widget_lists_the_overlay_sdk()
    {
        string result = Run(
            SdkRuntimeSurface.WidgetGlobals(),
            """
            (function () {
                const tree = r.buildSdkReference(DTS);
                const sdk = tree.find((n) => n.name === 'NomNomz');
                const on = sdk.children.filter((n) => n.name === 'on');
                return JSON.stringify([tree.map((n) => n.name), on.length, on[0].signatures.length > 0]);
            })()
            """
        );

        result.Should().Be("""[["NomNomz"],1,true]""");
    }

    [Fact]
    public void A_filter_keeps_only_matching_calls_with_their_namespaces_and_a_click_inserts_the_call()
    {
        string result = Run(
            ScriptSdk(),
            """
            (function () {
                const hits = r.filterSdkReference(r.buildSdkReference(DTS), 'random pick');
                const nnz = hits[0];
                const random = nnz.children[0];
                return JSON.stringify([
                    hits.length,
                    nnz.children.length,
                    random.children.map((n) => n.path),
                    r.insertTextFor(random.children[0]),
                    r.insertTextFor({ kind: 'property', path: 'bot.args' }),
                ]);
            })()
            """
        );

        result.Should().Be("""[1,1,["nnz.random.pick"],"nnz.random.pick()","bot.args"]""");
    }

    private const string HelperTypes = """
        type TemplateHelperName =
          | 'chat.me'
          | 'text.lower'
          | 'text.upper'
          | 'user'
          ;

        interface NnzTemplateHelperMap {
          'chat.me': 'Sends the text as an action';
          'text.lower': 'Makes the text lowercase';
          'text.upper': 'Makes the text UPPERCASE';
          'user': 'The viewer\'s display name';
        }
        """;

    [Fact]
    public void The_helpers_are_one_root_grouped_by_namespace_with_their_description()
    {
        string result = Run(
            HelperTypes,
            """
            (function () {
                const root = r.buildSdkReference(DTS).find((n) => n.name === 'template helpers');
                return JSON.stringify([
                    root.children.map((g) => g.name),
                    root.children.find((g) => g.name === 'text').children.map((h) => [h.path, h.doc]),
                    root.children.find((g) => g.name === 'general').children[0].doc,
                ]);
            })()
            """
        );

        result
            .Should()
            .Be(
                """[["chat","general","text"],[["text.lower","Makes the text lowercase"],["text.upper","Makes the text UPPERCASE"]],"The viewer's display name"]"""
            );
    }

    [Fact]
    public void A_filter_finds_a_helper_by_its_name_or_its_description_and_a_click_inserts_its_braces()
    {
        string result = Run(
            HelperTypes,
            """
            (function () {
                const byName = r.filterSdkReference(r.buildSdkReference(DTS), 'upper');
                const byDoc = r.filterSdkReference(r.buildSdkReference(DTS), 'lowercase');
                const leaf = byName[0].children[0].children[0];
                return JSON.stringify([leaf.path, byDoc[0].children[0].children[0].path, r.insertTextFor(leaf)]);
            })()
            """
        );

        result.Should().Be("""["text.upper","text.lower","{text.upper}"]""");
    }

    [Fact]
    public void The_generated_script_types_list_every_registry_row_as_a_helper()
    {
        using ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        string dts = new SdkTypeEmitter(new EventCatalog(), helpers: registry).EmitTypeScript(
            SdkContext.Script
        );

        string count = Run(
            dts,
            """
            (function () {
                const root = r.buildSdkReference(DTS).find((n) => n.name === 'template helpers');
                return root.children.reduce((sum, g) => sum + g.children.length, 0);
            })()
            """
        );

        count.Should().Be(registry.All.Count.ToString());
    }
}
