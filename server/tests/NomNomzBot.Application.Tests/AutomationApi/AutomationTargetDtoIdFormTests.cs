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
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Application.Tests.AutomationApi;

/// <summary>
/// The automation API (Stream Deck, token allow-lists) names a command or pipeline in a string id. Every list the
/// caller picks from sends ULIDs, so a target goes out as a ULID and a ULID comes in as the Guid the bot looks up
/// (live 2026-10-19: the same id-form gap left the dashboard's raid pipeline "not in this channel").
/// </summary>
public sealed class AutomationTargetDtoIdFormTests
{
    private static readonly Guid PipelineId = Guid.Parse("9f08aa54-76d6-49f3-ba02-3f5c79042914");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000b7c1");

    [Fact]
    public void A_stored_target_goes_out_with_its_id_as_a_ulid()
    {
        AutomationTargetDto pipeline = AutomationTargetDto.From(
            new BindingTarget(BindingTargetKind.Pipeline, PipelineId.ToString())
        );
        AutomationTargetDto command = AutomationTargetDto.From(
            new BindingTarget(BindingTargetKind.Command, CommandId.ToString())
        );

        pipeline.Should().Be(new AutomationTargetDto("pipeline", OwnedIdCodec.Encode(PipelineId)));
        command.Should().Be(new AutomationTargetDto("command", OwnedIdCodec.Encode(CommandId)));
    }

    [Theory]
    [InlineData("pipeline")]
    [InlineData("command")]
    public void A_ulid_from_a_list_comes_in_as_the_guid_the_bot_looks_up(string kind)
    {
        Guid id = kind == "pipeline" ? PipelineId : CommandId;

        Result<BindingTarget> target = new AutomationTargetDto(
            kind,
            OwnedIdCodec.Encode(id)
        ).ToTarget();

        target.IsSuccess.Should().BeTrue(target.ErrorMessage);
        target.Value.Id.Should().Be(id.ToString());
    }

    [Theory]
    [InlineData("pipeline", "9f08aa54-76d6-49f3-ba02-3f5c79042914")]
    [InlineData("command", "song_request")]
    public void A_raw_guid_and_a_built_in_command_key_come_in_unchanged(string kind, string id)
    {
        Result<BindingTarget> target = new AutomationTargetDto(kind, id).ToTarget();

        target.Value.Id.Should().Be(id);
    }

    [Fact]
    public void A_built_in_command_key_goes_out_unchanged()
    {
        AutomationTargetDto
            .From(new BindingTarget(BindingTargetKind.Command, "song_request"))
            .Id.Should()
            .Be("song_request");
    }
}
