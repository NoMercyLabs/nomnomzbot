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
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Tests.Models;

public class ErrorsTests
{
    [Fact]
    public void NotAuthenticated_ReturnsFailureWithAuthCode()
    {
        Result result = Errors.NotAuthenticated();

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("AUTH_REQUIRED");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void InsufficientPermission_IncludesAction()
    {
        Result result = Errors.InsufficientPermission("delete users");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        result.ErrorMessage.Should().Contain("delete users");
    }

    [Fact]
    public void FeatureNotEnabled_IncludesFeatureName()
    {
        Result result = Errors.FeatureNotEnabled("SongRequests");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FEATURE_DISABLED");
        result.ErrorMessage.Should().Contain("SongRequests");
    }

    [Fact]
    public void ScopeMissing_IncludesScopeAndFeature()
    {
        Result result = Errors.ScopeMissing("moderator:read", "ModQueue");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SCOPE_MISSING");
        result.ErrorMessage.Should().Contain("moderator:read");
        result.ErrorMessage.Should().Contain("ModQueue");
    }

    [Fact]
    public void NotFound_ReturnsTypedFailure()
    {
        Result<string> result = Errors.NotFound<string>("User", "uid-123");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        result.ErrorMessage.Should().Contain("User");
        result.ErrorMessage.Should().Contain("uid-123");
    }

    [Fact]
    public void ValidationFailed_WithMessage_ReturnsFailure()
    {
        Result result = Errors.ValidationFailed("Field is required");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("Field is required");
    }

    [Fact]
    public void ValidationFailed_WithDictionary_JoinsErrors()
    {
        Dictionary<string, string[]> errors = new()
        {
            { "Name", ["required"] },
            { "Email", ["invalid format"] },
        };

        Result result = Errors.ValidationFailed(errors);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("Name").And.Contain("required");
    }

    [Fact]
    public void ExternalServiceUnavailable_IncludesServiceName()
    {
        Result result = Errors.ExternalServiceUnavailable("Spotify");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
        result.ErrorMessage.Should().Contain("Spotify");
    }

    [Fact]
    public void TokenExpired_IncludesServiceName()
    {
        Result result = Errors.TokenExpired("Twitch");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TOKEN_EXPIRED");
        result.ErrorMessage.Should().Contain("Twitch");
    }

    [Fact]
    public void RateLimited_IncludesActionAndSeconds()
    {
        Result result = Errors.RateLimited("login", TimeSpan.FromSeconds(30));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("RATE_LIMITED");
        result.ErrorMessage.Should().Contain("30");
    }

    [Fact]
    public void AlreadyExists_IncludesEntityAndIdentifier()
    {
        Result result = Errors.AlreadyExists("Command", "!so");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("ALREADY_EXISTS");
        result.ErrorMessage.Should().Contain("Command").And.Contain("!so");
    }

    [Fact]
    public void ChannelNotFound_ReturnsFailureWithCode()
    {
        Result result = Errors.ChannelNotFound("ch-789");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }

    [Fact]
    public void ChannelNotFoundTyped_ReturnsTypedFailure()
    {
        Result<string> result = Errors.ChannelNotFound<string>("ch-789");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }

    [Fact]
    public void ChannelNotOnboarded_ReturnsCorrectCode()
    {
        Result result = Errors.ChannelNotOnboarded("ch-999");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_ONBOARDED");
    }
}
