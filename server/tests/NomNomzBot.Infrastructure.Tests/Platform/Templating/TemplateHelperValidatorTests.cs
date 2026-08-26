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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// S042 save-time guard: proves an unknown/misspelled helper is REJECTED by name (with a nearby
/// suggestion when one exists), a context-inappropriate helper is REJECTED even though it is spelled
/// correctly, and a genuinely valid template is accepted — so the validator is not simply refusing
/// everything it sees.
/// </summary>
public sealed class TemplateHelperValidatorTests
{
    private readonly ITemplateHelperValidator _sut = new TemplateHelperValidator();

    [Fact]
    public void Misspelled_helper_is_rejected_naming_the_offending_key()
    {
        Result result = _sut.Validate("Hi {user.nmae}!", TemplateHelperContext.Command);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("user.nmae");
    }

    [Fact]
    public void Misspelled_helper_close_to_a_real_one_suggests_the_nearest_match()
    {
        Result result = _sut.Validate("Hi {user.nmae}!", TemplateHelperContext.Command);

        result.ErrorMessage.Should().Contain("user.name");
    }

    [Fact]
    public void CommandOnly_helper_is_rejected_on_an_event_response()
    {
        Result result = _sut.Validate(
            "Thanks for the raid, arg was {args.1}",
            TemplateHelperContext.EventResponse
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("args.1");
    }

    [Fact]
    public void Valid_command_template_with_multiple_real_helpers_is_accepted()
    {
        Result result = _sut.Validate(
            "Thanks {user.name}, arg was {args.1} on {channel.display}",
            TemplateHelperContext.Command
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Valid_event_response_template_is_accepted()
    {
        Result result = _sut.Validate(
            "Welcome {user.name} to {channel.display}, {presentTense} live!",
            TemplateHelperContext.EventResponse
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Valid_timer_template_never_referencing_a_trigger_user_is_accepted()
    {
        Result result = _sut.Validate(
            "{channel.display} has been live for {stream.uptime}!",
            TemplateHelperContext.Timer
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void UserOnly_helper_is_rejected_on_a_timer()
    {
        Result result = _sut.Validate("Welcome {user.name}!", TemplateHelperContext.Timer);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("user.name");
    }

    [Fact]
    public void Prefixed_families_resolve_correctly_args_transform_and_random()
    {
        Result result = _sut.Validate(
            "{args.2} {transform.upper:{user.name}} {random.number.100} {count.deaths}",
            TemplateHelperContext.Command
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Null_or_empty_template_is_always_valid()
    {
        _sut.Validate(null, TemplateHelperContext.Command).IsSuccess.Should().BeTrue();
        _sut.Validate(string.Empty, TemplateHelperContext.Command).IsSuccess.Should().BeTrue();
    }
}
