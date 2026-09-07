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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Economy.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves the <c>jar_contribute</c> pipeline action: it contributes the triggering viewer's currency to the
/// named jar and surfaces the resulting balance, failing closed on a missing/invalid triggering viewer, an
/// unresolvable <c>jar_id</c>, or a non-positive amount. <c>jar_id</c> is a
/// <see cref="PipelineActionFieldKind.ResourceId"/> field — same class of bug as run_code's code_script_id
/// (the dashboard picker returns whatever id form the API last served, a 26-char ULID) — so it must tolerate
/// both wire forms exactly like RunCodeAction.
/// </summary>
public sealed class JarContributeActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-00000000e002");
    private static readonly Guid JarId = Guid.Parse("0192a000-0000-7000-8000-00000000e0aa");

    private static PipelineExecutionContext Context(string? triggeredByUserId = null) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = triggeredByUserId ?? Viewer.ToString(),
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "",
        };

    private static ActionDefinition Action(string? jarId, int amount = 10) =>
        new()
        {
            Type = "jar_contribute",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["jar_id"] = JsonSerializer.SerializeToElement(jarId ?? string.Empty),
                ["amount"] = JsonSerializer.SerializeToElement(amount),
            },
        };

    private static (JarContributeAction Action, ISavingsJarService Jars) Build(
        Result<JarMovementDto>? contributeResult = null
    )
    {
        ISavingsJarService jars = Substitute.For<ISavingsJarService>();
        jars.ContributeAsync(
                Arg.Any<Guid>(),
                Arg.Any<JarContributeRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                contributeResult
                    ?? Result.Success(
                        new JarMovementDto(
                            1,
                            JarId,
                            Broadcaster,
                            Viewer,
                            10,
                            "contribution",
                            42,
                            null,
                            Viewer,
                            DateTime.UtcNow
                        )
                    )
            );
        return (new(jars), jars);
    }

    [Fact]
    public async Task Contributes_to_the_named_jar_and_surfaces_the_balance_after()
    {
        (JarContributeAction action, ISavingsJarService jars) = Build();

        ActionResult result = await action.ExecuteAsync(Context(), Action(JarId.ToString()));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("42");
        await jars.Received(1)
            .ContributeAsync(
                Broadcaster,
                Arg.Is<JarContributeRequest>(r =>
                    r.JarId == JarId && r.ContributorUserId == Viewer && r.Amount == 10
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_ulid_form_jar_id_still_resolves()
    {
        (JarContributeAction action, ISavingsJarService jars) = Build();
        string ulid = OwnedIdCodec.Encode(JarId);

        ActionResult result = await action.ExecuteAsync(Context(), Action(ulid));

        result.Succeeded.Should().BeTrue();
        await jars.Received(1)
            .ContributeAsync(
                Broadcaster,
                Arg.Is<JarContributeRequest>(r => r.JarId == JarId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_garbage_jar_id_fails_without_contributing()
    {
        (JarContributeAction action, ISavingsJarService jars) = Build();

        ActionResult result = await action.ExecuteAsync(Context(), Action("not-an-id"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("jar_contribute requires a valid 'jar_id'.");
        await jars.DidNotReceive()
            .ContributeAsync(
                Arg.Any<Guid>(),
                Arg.Any<JarContributeRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_invalid_triggering_viewer_fails_without_contributing()
    {
        (JarContributeAction action, ISavingsJarService jars) = Build();

        ActionResult result = await action.ExecuteAsync(
            Context(triggeredByUserId: "not-a-viewer"),
            Action(JarId.ToString())
        );

        result.Succeeded.Should().BeFalse();
        await jars.DidNotReceive()
            .ContributeAsync(
                Arg.Any<Guid>(),
                Arg.Any<JarContributeRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_non_positive_amount_fails_without_contributing()
    {
        (JarContributeAction action, ISavingsJarService jars) = Build();

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(JarId.ToString(), amount: 0)
        );

        result.Succeeded.Should().BeFalse();
        await jars.DidNotReceive()
            .ContributeAsync(
                Arg.Any<Guid>(),
                Arg.Any<JarContributeRequest>(),
                Arg.Any<CancellationToken>()
            );
    }
}
