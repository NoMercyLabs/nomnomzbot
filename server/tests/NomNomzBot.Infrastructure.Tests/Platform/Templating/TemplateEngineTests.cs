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
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

public class TemplateEngineTests
{
    private static TemplateEngine Create() => new();

    // ─── Render(template, IReadOnlyDictionary) ───────────────────────────────

    [Fact]
    public void Render_EmptyTemplate_ReturnsEmpty()
    {
        TemplateEngine engine = Create();
        engine.Render(string.Empty, new Dictionary<string, string>()).Should().BeEmpty();
    }

    [Fact]
    public void Render_NullTemplate_ReturnsEmpty()
    {
        TemplateEngine engine = Create();
        engine.Render(null!, new Dictionary<string, string>()).Should().BeEmpty();
    }

    [Fact]
    public void Render_NoPlaceholders_ReturnsOriginal()
    {
        TemplateEngine engine = Create();
        string result = engine.Render("Hello world", new Dictionary<string, string>());
        result.Should().Be("Hello world");
    }

    [Fact]
    public void Render_KnownVariable_Substitutes()
    {
        TemplateEngine engine = Create();
        Dictionary<string, string> vars = new() { { "user", "Alice" } };
        string result = engine.Render("Hello {{user}}!", vars);

        result.Should().Be("Hello Alice!");
    }

    [Fact]
    public void Render_UnknownVariable_LeavesPlaceholderIntact()
    {
        TemplateEngine engine = Create();
        Dictionary<string, string> vars = new();
        string result = engine.Render("Hello {{unknown}}!", vars);

        result.Should().Be("Hello {{unknown}}!");
    }

    [Fact]
    public void Render_MultipleVariables_SubstitutesAll()
    {
        TemplateEngine engine = Create();
        Dictionary<string, string> vars = new() { { "a", "foo" }, { "b", "bar" } };
        string result = engine.Render("{{a}} and {{b}}", vars);

        result.Should().Be("foo and bar");
    }

    [Fact]
    public void Render_CaseInsensitiveKey_Substitutes()
    {
        TemplateEngine engine = Create();
        Dictionary<string, string> vars = new() { { "User", "Bob" } };
        string result = engine.Render("Hello {{user}}!", vars); // lowercase in template

        result.Should().Be("Hello Bob!");
    }

    [Fact]
    public void Render_NullValue_ReplacesWithEmpty()
    {
        TemplateEngine engine = Create();
        // Dictionary<string, string> can't have null values, but a cast to IReadOnlyDictionary could
        Dictionary<string, string?> vars = new() { { "user", null } };
        string result = engine.Render(
            "Hello {{user}}!",
            (IReadOnlyDictionary<string, string>)
                (IDictionary<string, string>)new Dictionary<string, string> { { "user", "" } }
        );

        result.Should().Be("Hello !");
    }

    [Fact]
    public void Render_SamePlaceholderTwice_BothSubstituted()
    {
        TemplateEngine engine = Create();
        Dictionary<string, string> vars = new() { { "x", "10" } };
        string result = engine.Render("{{x}} + {{x}}", vars);

        result.Should().Be("10 + 10");
    }

    [Fact]
    public void Render_WhitespaceInPlaceholder_Substitutes()
    {
        // TemplateEngine trims the variable name
        TemplateEngine engine = Create();
        Dictionary<string, string> vars = new() { { "user", "Carol" } };
        string result = engine.Render("Hello {{ user }}!", vars);

        result.Should().Be("Hello Carol!");
    }

    // ─── Render(template, variableName, variableValue) ───────────────────────

    [Fact]
    public void Render_SingleVar_Substitutes()
    {
        TemplateEngine engine = Create();
        string result = engine.Render("Hello {{name}}!", "name", "Dave");

        result.Should().Be("Hello Dave!");
    }

    [Fact]
    public void Render_SingleVar_UnrelatedPlaceholderUnchanged()
    {
        TemplateEngine engine = Create();
        string result = engine.Render("{{greeting}} {{name}}!", "name", "Eve");

        result.Should().Be("{{greeting}} Eve!");
    }

    // ─── RenderAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAsync_WithObjectDictionary_Substitutes()
    {
        TemplateEngine engine = Create();
        Dictionary<string, object> vars = new() { { "count", 42 } };
        string result = await engine.RenderAsync("Count: {{count}}", vars);

        result.Should().Be("Count: 42");
    }

    [Fact]
    public async Task RenderAsync_NullObjectValue_UsesEmpty()
    {
        TemplateEngine engine = Create();
        Dictionary<string, object> vars = new() { { "val", null! } };
        string result = await engine.RenderAsync("Value: {{val}}!", vars);

        result.Should().Be("Value: !");
    }

    [Fact]
    public async Task RenderAsync_IsCompletedSynchronously_NoActualAsync()
    {
        TemplateEngine engine = Create();
        Task<string> task = engine.RenderAsync(
            "Hello {{x}}",
            new Dictionary<string, object> { { "x", "world" } }
        );

        task.IsCompleted.Should().BeTrue();
        string result = await task;
        result.Should().Be("Hello world");
    }
}
