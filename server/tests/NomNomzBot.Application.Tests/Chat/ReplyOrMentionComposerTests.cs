// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Services;

namespace NomNomzBot.Application.Tests.Chat;

public class ReplyOrMentionComposerTests
{
    [Fact]
    public void Compose_WithNativeReplyTarget_ThreadsReplyAndLeavesMessageUntouched()
    {
        ReplyOrMentionPlan plan = ReplyOrMentionComposer.Compose(
            "parent-message-123",
            "Streamer_Eagle",
            "your request was queued"
        );

        Assert.True(plan.IsNativeReply);
        Assert.Equal("parent-message-123", plan.ReplyToMessageId);
        Assert.Equal("your request was queued", plan.Message);
    }

    [Fact]
    public void Compose_WithoutNativeReplyTarget_FallsBackToMentionPrefix()
    {
        ReplyOrMentionPlan plan = ReplyOrMentionComposer.Compose(
            null,
            "Streamer_Eagle",
            "your request was queued"
        );

        Assert.False(plan.IsNativeReply);
        Assert.Null(plan.ReplyToMessageId);
        Assert.Equal("@Streamer_Eagle your request was queued", plan.Message);
    }

    [Fact]
    public void Compose_WithEmptyReplyTarget_FallsBackToMentionPrefix()
    {
        ReplyOrMentionPlan plan = ReplyOrMentionComposer.Compose(
            string.Empty,
            "Streamer_Eagle",
            "your request was queued"
        );

        Assert.False(plan.IsNativeReply);
        Assert.Equal("@Streamer_Eagle your request was queued", plan.Message);
    }
}
