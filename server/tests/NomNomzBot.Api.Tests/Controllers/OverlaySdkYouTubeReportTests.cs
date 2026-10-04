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

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>NomNomz.reportYouTubePlayerState</c>, run for real: the served SDK executes in Jint and a YouTube player
/// page reports its state. Proves the hub frame carries the page's own widget id with the video, state and
/// position, and that the hub's answer reaches the widget.
/// </summary>
public sealed class OverlaySdkYouTubeReportTests
{
    private const char RecordSeparator = (char)30;

    private static JsonElement LastFrame(OverlaySdkRuntime page)
    {
        string frames = page.Text("__sockets[0].sent[__sockets[0].sent.length - 1]");
        return JsonDocument.Parse(frames.TrimEnd(RecordSeparator)).RootElement;
    }

    [Fact]
    public void A_report_invokes_ReportYouTubePlayerState_with_the_page_widget_id_video_state_and_position()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Evaluate("NomNomz.reportYouTubePlayerState('dQw4w9WgXcQ', 'PLAYING', 12500)");

        JsonElement frame = LastFrame(page);
        frame.GetProperty("type").GetInt32().Should().Be(1);
        frame.GetProperty("target").GetString().Should().Be("ReportYouTubePlayerState");
        JsonElement args = frame.GetProperty("arguments");
        args.GetArrayLength().Should().Be(4);
        args[0].GetString().Should().Be("widget-1");
        args[1].GetString().Should().Be("dQw4w9WgXcQ");
        args[2].GetString().Should().Be("PLAYING");
        args[3].GetInt64().Should().Be(12500);
    }

    [Fact]
    public void The_hub_answer_resolves_the_report_promise()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();
        page.Evaluate(
            "var __answer = 'pending'; NomNomz.reportYouTubePlayerState('v', 'ENDED', 0).then(function (r) { __answer = String(r); });"
        );
        string invocationId = LastFrame(page).GetProperty("invocationId").GetString()!;

        page.Complete(invocationId, true);

        page.Text("__answer").Should().Be("true");
    }
}
