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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves S015 (moderation.md J.6): a regex filter's pattern is validated at save time with a diagnostic that
/// names the actual compile failure — never a bare "invalid" — and nothing persists when it fails; a valid
/// pattern saves and actually matches through the same code path the chat pipeline uses; and the dashboard
/// "test this filter" seam (<see cref="IChatFilterService.TestPattern"/>) reports match/no-match plus a compile
/// error without ever persisting or falling back to literal matching. Also proves the shared match timeout
/// (<see cref="ChatFilterService.MatchTimeout"/>, 100ms) stops a catastrophic-backtracking pattern instead of
/// hanging the caller.
/// </summary>
public sealed class ChatFilterServiceTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    [Fact]
    public async Task CreateAsync_rejects_an_invalid_regex_with_a_reason_and_persists_nothing()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> result = await service.CreateAsync(
            Broadcaster,
            new CreateChatFilterRequest
            {
                FilterType = ChatFilterType.Regex,
                Name = "unbalanced-paren",
                Action = ChatFilterAction.Delete,
                Pattern = "(foo", // missing closing paren — a genuine, nameable compile failure
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        // Not a bare "invalid" — carries .NET's own parser diagnostic naming the reason.
        result.ErrorMessage.Should().Contain("Not enough )'s");

        (await db.ChatFilters.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_saves_a_valid_regex_and_it_actually_matches()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> result = await service.CreateAsync(
            Broadcaster,
            new CreateChatFilterRequest
            {
                FilterType = ChatFilterType.Regex,
                Name = "shouty-spam",
                Action = ChatFilterAction.Delete,
                Pattern = @"\bfree\s+bits\b",
            }
        );

        result.IsSuccess.Should().BeTrue();
        ChatFilter persisted = await db.ChatFilters.SingleAsync();
        persisted.Pattern.Should().Be(@"\bfree\s+bits\b");

        // The tester seam, run against the SAME persisted pattern, proves it actually matches.
        Result<ChatFilterTestResult> matchTest = service.TestPattern(
            new TestChatFilterRequest
            {
                FilterType = ChatFilterType.Regex,
                Pattern = persisted.Pattern,
                SampleMessage = "get your free bits here",
            }
        );
        matchTest.IsSuccess.Should().BeTrue();
        matchTest.Value.IsMatch.Should().BeTrue();
        matchTest.Value.CompileError.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_invalid_regex_and_leaves_the_stored_pattern_untouched()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterDto> created = await service.CreateAsync(
            Broadcaster,
            new CreateChatFilterRequest
            {
                FilterType = ChatFilterType.Regex,
                Name = "original",
                Action = ChatFilterAction.Delete,
                Pattern = "good-pattern",
            }
        );

        Result<ChatFilterDto> updated = await service.UpdateAsync(
            Broadcaster,
            created.Value.Id,
            new UpdateChatFilterRequest { Pattern = "[unterminated" }
        );

        updated.IsFailure.Should().BeTrue();
        updated.ErrorCode.Should().Be("VALIDATION_FAILED");

        ChatFilter stored = await db.ChatFilters.SingleAsync();
        stored.Pattern.Should().Be("good-pattern"); // the bad edit never overwrote it
    }

    [Fact]
    public void TestPattern_reports_the_compile_error_for_a_bad_regex_without_persisting()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterTestResult> result = service.TestPattern(
            new TestChatFilterRequest
            {
                FilterType = ChatFilterType.Regex,
                Pattern = "[a-z", // unterminated character class
                SampleMessage = "abc",
            }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsMatch.Should().BeFalse();
        result.Value.CompileError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TestPattern_reports_no_match_for_a_valid_regex_that_does_not_match()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        Result<ChatFilterTestResult> result = service.TestPattern(
            new TestChatFilterRequest
            {
                FilterType = ChatFilterType.Regex,
                Pattern = @"^giveaway$",
                SampleMessage = "hello world",
            }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsMatch.Should().BeFalse();
        result.Value.CompileError.Should().BeNull();
    }

    [Fact]
    public void TestPattern_times_out_on_catastrophic_backtracking_instead_of_hanging()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        // Classic catastrophic-backtracking shape: (a+)+ against a long near-miss string with no trailing 'b'.
        Result<ChatFilterTestResult> result = service.TestPattern(
            new TestChatFilterRequest
            {
                FilterType = ChatFilterType.Regex,
                Pattern = "^(a+)+$",
                SampleMessage = new string('a', 40) + "!",
            }
        );

        // The call returns promptly (xUnit's own timeout would fail the test if MatchTimeout were not honored)
        // and reports the timeout as a compile/runtime error rather than a match.
        result.IsSuccess.Should().BeTrue();
        result.Value.IsMatch.Should().BeFalse();
        result.Value.CompileError.Should().Contain("did not finish matching");
    }

    [Fact]
    public void TestPattern_never_falls_back_to_literal_matching_for_an_invalid_pattern()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ChatFilterService service = new(db);

        // If this pattern were ever treated as a literal string, it would match itself verbatim.
        const string invalidPattern = "(unterminated";
        Result<ChatFilterTestResult> result = service.TestPattern(
            new TestChatFilterRequest
            {
                FilterType = ChatFilterType.Regex,
                Pattern = invalidPattern,
                SampleMessage = invalidPattern,
            }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsMatch.Should().BeFalse();
        result.Value.CompileError.Should().NotBeNullOrEmpty();
    }
}
