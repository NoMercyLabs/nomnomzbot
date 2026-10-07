// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Jint;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// An event sample the author edits in the widget editor is saved as the project file <c>events/&lt;type&gt;.json</c>
/// and comes back as that widget's sample on the next open. These tests run the editor's own module
/// (<c>Assets/editor/event-sample-files.js</c>) so the open, save and reset paths are the shipped ones.
/// </summary>
public sealed class EditorEventSampleFilesTests
{
    private static string Run(string expression)
    {
        Engine engine = new();
        engine.Modules.Add(
            "samples",
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "editor", "event-sample-files.js")
            )
        );
        engine.Modules.Add(
            "test",
            "import * as s from 'samples'; export const out = " + expression + ";"
        );
        return engine.Modules.Import("test").Get("out").ToString();
    }

    [Fact]
    public void Opening_a_project_splits_the_saved_samples_off_the_code_files()
    {
        string result = Run(
            """
            (function () {
                const r = s.splitSampleFiles({
                    'index.html': '<div></div>',
                    'events/follow.json': '{"user_name":"kitte","tier":2}',
                    'events/broken.json': '{nope',
                    'events/list.json': '[1]',
                    'events/a/b.json': '{}',
                });
                return JSON.stringify([Object.keys(r.code).sort(), r.samples]);
            })()
            """
        );

        result
            .Should()
            .Be("""[["events/a/b.json","index.html"],{"follow":{"user_name":"kitte","tier":2}}]""");
    }

    [Fact]
    public void Saving_writes_one_file_per_edited_sample_next_to_the_code_and_a_reset_type_writes_none()
    {
        string result = Run(
            """
            (function () {
                const files = s.joinSampleFiles({ 'index.html': 'x' }, { cheer: { bits: 500 } });
                const back = s.splitSampleFiles(files);
                const afterReset = s.joinSampleFiles(back.code, {});
                return JSON.stringify([Object.keys(files).sort(), back.samples, Object.keys(afterReset)]);
            })()
            """
        );

        result
            .Should()
            .Be("""[["events/cheer.json","index.html"],{"cheer":{"bits":500}},["index.html"]]""");
    }

    [Fact]
    public void An_edit_is_kept_unless_it_equals_the_stock_sample_or_is_not_a_json_object()
    {
        string result = Run(
            """
            JSON.stringify([
                s.editedSample('{"bits":500}', { bits: 100 }),
                s.editedSample('{ "bits": 100 }', { bits: 100 }),
                s.editedSample('{"bits":', { bits: 100 }) === undefined,
                s.editedSample('[1]', { bits: 100 }) === undefined,
            ])
            """
        );

        result.Should().Be("""[{"bits":500},null,true,true]""");
    }
}
