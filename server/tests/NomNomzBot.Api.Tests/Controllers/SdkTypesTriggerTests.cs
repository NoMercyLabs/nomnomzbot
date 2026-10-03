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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>GET /sdk/types.d.ts?context=script&amp;trigger=…</c> serves the script types for one trigger, so the editor
/// can flag a variable key that trigger never sets. An unknown trigger is a 400, not a silent untyped fallback.
/// </summary>
public sealed class SdkTypesTriggerTests
{
    private static readonly Guid FollowScript = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
    private static readonly Guid LonelyScript = Guid.Parse("0192a000-0000-7000-8000-00000000f002");
    private static readonly Guid MysteryScript = Guid.Parse("0192a000-0000-7000-8000-00000000f003");

    private static SdkController Controller() =>
        new(new SdkTypeEmitter(new EventCatalog(), new OneSampleCatalog()), new FakeResolver());

    [Fact]
    public async Task A_known_trigger_returns_the_union_of_its_keys()
    {
        IActionResult result = await Controller().GetTypes("script", "channel.follow", null);

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/plain");
        content.Content.Should().Contain("type NnzVarKey = 'followed_at' | 'user';");
        content.Content.Should().Contain("getVar(key: string, dynamic: true)");
    }

    [Fact]
    public async Task An_unknown_trigger_is_a_400_with_the_error_envelope()
    {
        IActionResult result = await Controller().GetTypes("script", "nope", null);

        BadRequestObjectResult bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        StatusResponseDto<object> body = bad
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Status.Should().Be("error");
        body.Code.Should().Be("UNKNOWN_TRIGGER");
    }

    [Fact]
    public async Task A_trigger_on_the_widget_context_is_a_400()
    {
        IActionResult result = await Controller().GetTypes("widget", "channel.follow", null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task No_trigger_keeps_the_untyped_getVar()
    {
        IActionResult result = await Controller().GetTypes("script", null, null);

        result
            .Should()
            .BeOfType<ContentResult>()
            .Subject.Content.Should()
            .Contain("  getVar(key: string): string | null;");
    }

    [Fact]
    public async Task A_script_is_typed_with_the_union_of_the_keys_of_its_triggers()
    {
        IActionResult result = await Controller().GetTypes("script", null, FollowScript);

        string ts = result.Should().BeOfType<ContentResult>().Subject.Content!;
        ts.Should().Contain("type NnzVarKey");
        ts.Should().Contain("'followed_at'");
        ts.Should().Contain("'args.count'");
        ts.Should().NotContain("'typo'");
    }

    [Fact]
    public async Task A_script_with_no_trigger_gets_the_plain_untyped_getVar()
    {
        IActionResult result = await Controller().GetTypes("script", null, LonelyScript);

        string ts = result.Should().BeOfType<ContentResult>().Subject.Content!;
        ts.Should().NotContain("type NnzVarKey");
        ts.Should().Contain("  getVar(key: string): string | null;");
    }

    [Fact]
    public async Task A_script_with_a_trigger_that_cannot_be_listed_is_not_typed_narrow()
    {
        IActionResult result = await Controller().GetTypes("script", null, MysteryScript);

        string ts = result.Should().BeOfType<ContentResult>().Subject.Content!;
        ts.Should().NotContain("type NnzVarKey");
        ts.Should().Contain("  getVar(key: string): string | null;");
    }

    [Fact]
    public async Task An_unknown_script_is_a_404()
    {
        IActionResult result = await Controller().GetTypes("script", null, Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task A_script_and_a_trigger_together_are_a_400()
    {
        IActionResult result = await Controller()
            .GetTypes("script", "channel.follow", FollowScript);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task A_script_on_the_widget_context_is_a_400()
    {
        IActionResult result = await Controller().GetTypes("widget", null, FollowScript);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    private sealed class FakeResolver : ICodeScriptTriggerResolver
    {
        public Task<Result<IReadOnlyList<string>>> GetTriggerKeysAsync(
            Guid codeScriptId,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                codeScriptId == FollowScript
                    ? Result.Success<IReadOnlyList<string>>(["channel.follow", "command"])
                : codeScriptId == LonelyScript ? Result.Success<IReadOnlyList<string>>([])
                : codeScriptId == MysteryScript
                    ? Result.Success<IReadOnlyList<string>>(["command", "channel.mystery"])
                : Result.Failure<IReadOnlyList<string>>("Script not found.", "NOT_FOUND")
            );
    }

    private sealed class OneSampleCatalog : ITriggerSampleCatalog
    {
        private static readonly TriggerSample Follow = new(
            "channel.follow",
            "channel.follow",
            "42",
            "Viewer",
            new Dictionary<string, string> { ["user"] = "Viewer", ["followed_at"] = "t" }
        );

        public IReadOnlyList<TriggerSample> List() => [Follow];

        public TriggerSample? Find(string id) => id == Follow.Id ? Follow : null;
    }
}
