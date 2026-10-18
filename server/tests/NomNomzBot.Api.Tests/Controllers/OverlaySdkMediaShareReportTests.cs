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
/// <c>NomNomz.reportMediaSharePlayerState</c>, run for real: the served SDK executes in Jint and the
/// media-share page reports its player. Proves the hub frame carries the page's own widget id with the
/// request, state, position, duration and volume, and that the hub's answer reaches the widget.
/// </summary>
public sealed class OverlaySdkMediaShareReportTests
{
    private const char RecordSeparator = (char)30;

    private static JsonElement LastFrame(OverlaySdkRuntime page)
    {
        string frames = page.Text("__sockets[0].sent[__sockets[0].sent.length - 1]");
        return JsonDocument.Parse(frames.TrimEnd(RecordSeparator)).RootElement;
    }

    [Fact]
    public void A_report_invokes_ReportMediaSharePlayerState_with_the_page_widget_id_and_every_field()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Evaluate(
            "NomNomz.reportMediaSharePlayerState('0192b000-0000-7000-8000-000000000001', 'playing', 12.6, 213, 70)"
        );

        JsonElement frame = LastFrame(page);
        frame.GetProperty("type").GetInt32().Should().Be(1);
        frame.GetProperty("target").GetString().Should().Be("ReportMediaSharePlayerState");
        JsonElement args = frame.GetProperty("arguments");
        args.GetArrayLength().Should().Be(6);
        args[0].GetString().Should().Be("widget-1");
        args[1].GetString().Should().Be("0192b000-0000-7000-8000-000000000001");
        args[2].GetString().Should().Be("playing");
        args[3].GetInt32().Should().Be(13);
        args[4].GetInt32().Should().Be(213);
        args[5].GetInt32().Should().Be(70);
    }

    [Fact]
    public void A_report_with_no_request_sends_an_empty_request_id()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();

        page.Evaluate("NomNomz.reportMediaSharePlayerState(null, 'idle', 0, 0, 100)");

        LastFrame(page).GetProperty("arguments")[1].GetString().Should().BeEmpty();
    }

    [Fact]
    public void The_hub_answer_resolves_the_report_promise()
    {
        OverlaySdkRuntime page = OverlaySdkRuntime.Connected();
        page.Evaluate(
            "var __answer = 'pending'; NomNomz.reportMediaSharePlayerState('r', 'ended', 1, 1, 100).then(function (r) { __answer = String(r); });"
        );
        string invocationId = LastFrame(page).GetProperty("invocationId").GetString()!;

        page.Complete(invocationId, true);

        page.Text("__answer").Should().Be("true");
    }
}
