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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Gdpr;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Infrastructure.Identity.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity.Builtins;

/// <summary>
/// Proves the §9 in-chat data-subject rights surface (gdpr-crypto.md): <c>!forgetme</c> is two-step
/// (no erasure without a confirm inside the 60s window), <c>!mydata</c> records an export without
/// ever dumping the document into public chat, <c>!gdpr status</c> reports the caller's latest
/// request — and every command acts on the Twitch-verified CHATTER, never an argument target.
/// </summary>
public sealed class GdprBuiltinsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b201");
    private static readonly Guid SubjectId = Guid.Parse("0192a000-0000-7000-8000-00000000b202");
    private static readonly Guid RequestId = Guid.Parse("0192a000-0000-7000-8000-00000000b203");

    private sealed record Harness(
        ForgetMeBuiltin Forget,
        MyDataBuiltin MyData,
        GdprBuiltin Gdpr,
        IErasureService Erasure,
        IUserService Users,
        FakeTimeProvider Clock
    );

    private static BuiltinCommandContext Context(
        string args,
        string? customResponseTemplate = null
    ) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "tw-1",
            TriggeringUserDisplayName = "Viewer",
            TriggeringUserLogin = "viewer",
            RoleLevel = 0,
            Args = args,
            CustomResponseTemplate = customResponseTemplate,
        };

    private static ErasureRequestDto RequestDto(string requestType, string status) =>
        new(
            Id: RequestId,
            SubjectUserId: SubjectId,
            SubjectIdHash: "subject-hash",
            BroadcasterId: Channel,
            RequestType: requestType,
            RequestedBy: "self_service",
            Status: status,
            Scope: "deployment",
            CryptoShredApplied: true,
            AnonymizationApplied: true,
            RowsAffected: 12,
            ExportLocation: null,
            ExportFormat: null,
            FailureReason: null,
            RequestedAt: DateTime.UnixEpoch,
            CompletedAt: DateTime.UnixEpoch
        );

    private static Harness Build()
    {
        IErasureService erasure = Substitute.For<IErasureService>();
        erasure
            .RequestErasureAsync(Arg.Any<RequestErasureRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(RequestDto("erasure", "completed")));

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                "tw-1",
                "viewer",
                "Viewer",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        Id: SubjectId.ToString(),
                        Username: "viewer",
                        DisplayName: "Viewer",
                        ProfileImageUrl: null,
                        Email: null,
                        CreatedAt: DateTime.UnixEpoch,
                        LastLoginAt: DateTime.UnixEpoch
                    )
                )
            );

        // Real precedence surrogate: override wins when set, else the neutral fallback — matching
        // BuiltinResponseComposer's ladder without dragging the template resolver in.
        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();
        composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                BuiltinResponseRequest request = call.Arg<BuiltinResponseRequest>();
                return string.IsNullOrWhiteSpace(request.OverrideTemplate)
                    ? request.NeutralFallback
                    : request.OverrideTemplate!;
            });

        FakeTimeProvider clock = new();
        ErasureConfirmationTracker tracker = new(clock);
        GdprSelfServiceExecutor executor = new(erasure, users, composer, tracker);

        return new Harness(
            new ForgetMeBuiltin(executor),
            new MyDataBuiltin(executor),
            new GdprBuiltin(executor),
            erasure,
            users,
            clock
        );
    }

    [Fact]
    public async Task Forgetme_without_confirm_warns_and_creates_no_erasure_request()
    {
        Harness h = Build();

        Result<string> reply = await h.Forget.ExecuteAsync(Context(""));

        reply.Value.Should().Contain("cannot be undone").And.Contain("!forgetme confirm");
        await h.Erasure.DidNotReceiveWithAnyArgs().RequestErasureAsync(default!, default);
    }

    [Fact]
    public async Task Forgetme_confirm_inside_the_window_erases_the_chatting_viewer_as_self_service()
    {
        Harness h = Build();

        await h.Forget.ExecuteAsync(Context(""));
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        Result<string> reply = await h.Forget.ExecuteAsync(Context("confirm"));

        await h
            .Erasure.Received(1)
            .RequestErasureAsync(
                Arg.Is<RequestErasureRequest>(r =>
                    r.SubjectUserId == SubjectId
                    && r.BroadcasterId == Channel
                    && r.RequestedBy == "self_service"
                    && r.Scope == "deployment"
                ),
                Arg.Any<CancellationToken>()
            );
        reply.Value.Should().Contain("clean slate");
    }

    [Fact]
    public async Task Forgetme_confirm_after_the_window_expired_does_not_erase_and_asks_to_restart()
    {
        Harness h = Build();

        await h.Forget.ExecuteAsync(Context(""));
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        Result<string> reply = await h.Forget.ExecuteAsync(Context("confirm"));

        reply.Value.Should().Contain("no pending erasure");
        await h.Erasure.DidNotReceiveWithAnyArgs().RequestErasureAsync(default!, default);
    }

    [Fact]
    public async Task Forgetme_confirm_without_a_prior_prompt_does_not_erase()
    {
        Harness h = Build();

        Result<string> reply = await h.Forget.ExecuteAsync(Context("confirm"));

        reply.Value.Should().Contain("no pending erasure");
        await h.Erasure.DidNotReceiveWithAnyArgs().RequestErasureAsync(default!, default);
    }

    [Fact]
    public async Task Completion_reply_always_appends_the_mandatory_reentry_clause_even_over_a_custom_template()
    {
        Harness h = Build();

        await h.Forget.ExecuteAsync(Context(""));
        Result<string> reply = await h.Forget.ExecuteAsync(
            Context("confirm", customResponseTemplate: "Poof — gone!")
        );

        // Part 1 is the streamer's copy; part 2 (informed re-entry) is fixed and non-removable.
        reply.Value.Should().StartWith("Poof — gone!");
        reply.Value.Should().EndWith(GdprSelfServiceExecutor.MandatoryReEntryClause);
    }

    [Fact]
    public async Task Forgetme_ignores_argument_targets_it_can_only_ever_be_the_chatter()
    {
        Harness h = Build();

        // "@victim" is NOT a target — it is not "confirm", so the caller merely gets the warning.
        Result<string> reply = await h.Forget.ExecuteAsync(Context("@victim"));

        reply.Value.Should().Contain("!forgetme confirm");
        await h.Erasure.DidNotReceiveWithAnyArgs().RequestErasureAsync(default!, default);
        // The identity that was resolved is the chatting viewer's own.
        await h
            .Users.Received()
            .GetOrCreateAsync(
                "tw-1",
                "viewer",
                "Viewer",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Gdpr_forget_alias_shares_the_same_confirmation_window_as_forgetme()
    {
        Harness h = Build();

        Result<string> prompt = await h.Gdpr.ExecuteAsync(Context("forget"));
        Result<string> done = await h.Gdpr.ExecuteAsync(Context("forget confirm"));

        prompt.Value.Should().Contain("!forgetme confirm");
        done.Value.Should().Contain("clean slate");
        await h
            .Erasure.Received(1)
            .RequestErasureAsync(
                Arg.Is<RequestErasureRequest>(r => r.SubjectUserId == SubjectId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Mydata_records_an_export_for_the_chatter_and_never_dumps_the_document_into_chat()
    {
        Harness h = Build();
        h.Erasure.RequestExportAsync(Arg.Any<RequestExportRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new DataExportDto(
                        ErasureRequestId: RequestId,
                        ExportFormat: "json",
                        ExportLocation: "/api/v1/gdpr/requests/" + RequestId,
                        SizeBytes: 512,
                        RowsAffected: 9,
                        GeneratedAt: DateTime.UnixEpoch,
                        Document: "{\"email\":\"viewer@example.com\",\"marker\":\"PII-MARKER\"}"
                    )
                )
            );

        Result<string> reply = await h.MyData.ExecuteAsync(Context(""));

        await h
            .Erasure.Received(1)
            .RequestExportAsync(
                Arg.Is<RequestExportRequest>(r =>
                    r.SubjectUserId == SubjectId
                    && r.BroadcasterId == Channel
                    && r.RequestedBy == "self_service"
                ),
                Arg.Any<CancellationToken>()
            );
        // Chat is public: the reply is a pointer, never the data.
        reply.Value.Should().NotContain("PII-MARKER").And.NotContain("viewer@example.com");
        reply.Value.Should().Contain("never posted in chat");
    }

    [Fact]
    public async Task Gdpr_status_reports_the_callers_latest_request_self_scoped()
    {
        Harness h = Build();
        h.Erasure.ListRequestsAsync(
                Arg.Any<PaginationParams>(),
                SubjectId,
                null,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new PagedList<ErasureRequestDto>([RequestDto("erasure", "completed")], 1, 1, 1)
                )
            );

        Result<string> reply = await h.Gdpr.ExecuteAsync(Context("status"));

        // Self-scoping is the seam contract: subjectUserId is ALWAYS the resolved chatter.
        await h
            .Erasure.Received(1)
            .ListRequestsAsync(
                Arg.Any<PaginationParams>(),
                SubjectId,
                null,
                Arg.Any<CancellationToken>()
            );
        reply.Value.Should().Contain("erasure").And.Contain("completed");
    }

    [Fact]
    public async Task Gdpr_status_with_no_requests_replies_a_neutral_pointer_to_the_commands()
    {
        Harness h = Build();
        h.Erasure.ListRequestsAsync(
                Arg.Any<PaginationParams>(),
                SubjectId,
                null,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<ErasureRequestDto>([], 1, 1, 0)));

        Result<string> reply = await h.Gdpr.ExecuteAsync(Context("status"));

        reply.Value.Should().Contain("no data requests");
    }

    [Fact]
    public async Task Gdpr_with_an_unknown_verb_replies_usage()
    {
        Harness h = Build();

        Result<string> reply = await h.Gdpr.ExecuteAsync(Context("wat"));

        reply.Value.Should().Contain("!gdpr status");
        await h.Erasure.DidNotReceiveWithAnyArgs().RequestErasureAsync(default!, default);
    }

    [Fact]
    public void All_three_commands_are_reserved_always_on_everyone_floor()
    {
        Harness h = Build();

        IBuiltinCommand[] commands = [h.Forget, h.MyData, h.Gdpr];
        foreach (IBuiltinCommand command in commands)
        {
            command.IsReserved.Should().BeTrue();
            command.DefaultMinPermissionLevel.Should().Be(0);
        }
        h.Forget.BuiltinKey.Should().Be("forgetme");
        h.MyData.BuiltinKey.Should().Be("mydata");
        h.Gdpr.BuiltinKey.Should().Be("gdpr");
    }
}
