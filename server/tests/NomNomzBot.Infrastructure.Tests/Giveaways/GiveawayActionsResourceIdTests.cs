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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Giveaways.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Giveaways;

/// <summary>
/// Proves <c>open_giveaway</c>/<c>draw_giveaway</c>/<c>enter_giveaway</c> resolve their <c>giveaway_id</c>
/// parameter — a <see cref="PipelineActionFieldKind.ResourceId"/> field, the same class of bug as run_code's
/// code_script_id — whether the dashboard picker handed it a raw Guid or the API's 26-char ULID wire form.
/// The active-giveaway db fallback (<c>GiveawayActionSupport.ResolveActiveAsync</c>, used only when
/// <c>giveaway_id</c> is omitted) is pre-existing behavior, not touched by this fix, and is exercised
/// elsewhere — these tests cover only the id-resolution path this fix changed.
/// </summary>
public sealed class GiveawayActionsResourceIdTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
    private static readonly Guid GiveawayId = Guid.Parse("0192a000-0000-7000-8000-00000000f0aa");

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = Guid.Parse("0192a000-0000-7000-8000-00000000f002").ToString(),
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "",
        };

    private static ActionDefinition Action(string type, string? giveawayId) =>
        new()
        {
            Type = type,
            Parameters = giveawayId is null
                ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>
                {
                    ["giveaway_id"] = JsonSerializer.SerializeToElement(giveawayId),
                },
        };

    // ── open_giveaway (no db dependency — giveaway_id is Required, no active-giveaway fallback) ──

    [Fact]
    public async Task OpenGiveaway_resolves_a_ulid_form_giveaway_id()
    {
        IGiveawayService giveaways = Substitute.For<IGiveawayService>();
        giveaways
            .OpenAsync(Broadcaster, GiveawayId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(BuildDto()));
        OpenGiveawayAction sut = new(giveaways);
        string ulid = OwnedIdCodec.Encode(GiveawayId);

        ActionResult result = await sut.ExecuteAsync(Context(), Action("open_giveaway", ulid));

        result.Succeeded.Should().BeTrue();
        await giveaways
            .Received(1)
            .OpenAsync(Broadcaster, GiveawayId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenGiveaway_rejects_a_garbage_giveaway_id_without_calling_the_service()
    {
        IGiveawayService giveaways = Substitute.For<IGiveawayService>();
        OpenGiveawayAction sut = new(giveaways);

        ActionResult result = await sut.ExecuteAsync(Context(), Action("open_giveaway", "garbage"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("open_giveaway requires a 'giveaway_id'.");
        await giveaways
            .DidNotReceive()
            .OpenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── draw_giveaway (explicit-id path only; the null-id -> active-giveaway db fallback is untouched
    //    by this fix and is not exercised here) ──

    [Fact]
    public async Task DrawGiveaway_resolves_a_ulid_form_giveaway_id_without_touching_the_db_fallback()
    {
        IGiveawayService giveaways = Substitute.For<IGiveawayService>();
        giveaways
            .DrawAsync(Broadcaster, GiveawayId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<GiveawayWinnerDto>>([
                    new(
                        Guid.NewGuid(),
                        GiveawayId,
                        Guid.NewGuid(),
                        "alice",
                        DateTime.UtcNow,
                        "drawn",
                        false,
                        null,
                        null
                    ),
                ])
            );
        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();
        DrawGiveawayAction sut = new(giveaways, db);
        string ulid = OwnedIdCodec.Encode(GiveawayId);

        ActionResult result = await sut.ExecuteAsync(Context(), Action("draw_giveaway", ulid));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("alice");
        await giveaways
            .Received(1)
            .DrawAsync(Broadcaster, GiveawayId, Arg.Any<CancellationToken>());
        _ = db.DidNotReceive().Giveaways;
    }

    // ── enter_giveaway (explicit-id path only, same reasoning as draw_giveaway above) ──

    [Fact]
    public async Task EnterGiveaway_resolves_a_ulid_form_giveaway_id_without_touching_the_db_fallback()
    {
        IGiveawayService giveaways = Substitute.For<IGiveawayService>();
        Guid viewer = Guid.Parse("0192a000-0000-7000-8000-00000000f002");
        giveaways
            .EnterAsync(Broadcaster, GiveawayId, viewer, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new GiveawayEntryDto(
                        Guid.NewGuid(),
                        GiveawayId,
                        viewer,
                        "bob",
                        3,
                        DateTime.UtcNow
                    )
                )
            );
        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();
        EnterGiveawayAction sut = new(giveaways, db);
        string ulid = OwnedIdCodec.Encode(GiveawayId);

        ActionResult result = await sut.ExecuteAsync(Context(), Action("enter_giveaway", ulid));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("3");
        await giveaways
            .Received(1)
            .EnterAsync(Broadcaster, GiveawayId, viewer, Arg.Any<CancellationToken>());
        _ = db.DidNotReceive().Giveaways;
    }

    private static GiveawayDto BuildDto() =>
        new(
            GiveawayId,
            "Sub Giveaway",
            "keyword",
            "!enter",
            null,
            1,
            null,
            null,
            1,
            false,
            null,
            "announce",
            null,
            false,
            null,
            null,
            false,
            "open",
            DateTime.UtcNow,
            null,
            null,
            null,
            0,
            DateTime.UtcNow
        );
}
