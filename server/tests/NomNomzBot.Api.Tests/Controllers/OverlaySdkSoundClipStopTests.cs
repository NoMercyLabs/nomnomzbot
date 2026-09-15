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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-OBS-06: sound clips had no single-playback enforcement and no stop control — every <c>PlaySound</c> made
/// a brand new, untracked <c>&lt;audio&gt;</c> element, so two clips fired close together played on top of
/// each other forever with nothing able to stop either one.
/// <para>
/// The SDK is inline script served as text, so these assert on the served script — the only place the
/// behaviour exists (same pattern as <see cref="OverlaySdkTtsPlaybackTests"/>).
/// </para>
/// </summary>
public sealed class OverlaySdkSoundClipStopTests
{
    private static string Sdk()
    {
        OverlaySdkController controller = new()
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        ContentResult result = (ContentResult)controller.Get();
        return result.Content!;
    }

    private static string PlaySoundBody()
    {
        string sdk = Sdk();
        int start = sdk.IndexOf("function playSound(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the SDK must still play sound clips");
        int next = sdk.IndexOf("// TTS plays one utterance", start, StringComparison.Ordinal);
        return sdk[start..(next > start ? next : sdk.Length)];
    }

    private static string StopSoundBody()
    {
        string sdk = Sdk();
        int start = sdk.IndexOf("function stopSound(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the SDK must still be able to stop a sound clip");
        int next = sdk.IndexOf("// Cached browser voice list", start, StringComparison.Ordinal);
        return sdk[start..(next > start ? next : sdk.Length)];
    }

    [Fact]
    public void Starting_an_unhandled_clip_stops_whichever_unhandled_clip_is_already_playing()
    {
        string play = PlaySoundBody();

        int pauseCall = play.IndexOf("currentSound.pause()", StringComparison.Ordinal);
        int createCall = play.IndexOf("createElement(\"audio\")", StringComparison.Ordinal);

        pauseCall.Should().BeGreaterThan(-1, "the previous clip must be stopped, not left running");
        // Order is the behaviour: the old clip is paused BEFORE the new element is even created, so there is
        // never a moment where two unhandled clips are both alive.
        pauseCall.Should().BeLessThan(createCall);
    }

    [Fact]
    public void A_newly_started_unhandled_clip_becomes_the_new_current_sound()
    {
        string play = PlaySoundBody();

        play.Should().Contain("currentSound = el;");
    }

    [Fact]
    public void A_handled_clip_replaces_only_its_own_handle_and_leaves_the_current_slot_alone()
    {
        string play = PlaySoundBody();

        int handleBranch = play.IndexOf("if (payload.handle)", StringComparison.Ordinal);
        int elseBranch = play.IndexOf("} else if (currentSound)", StringComparison.Ordinal);
        handleBranch.Should().BeGreaterThan(-1);
        elseBranch.Should().BeGreaterThan(handleBranch);
        // The handle branch pauses ITS OWN prior handle, not the unhandled current slot.
        play[handleBranch..elseBranch].Should().Contain("existingHandled.pause()");
    }

    [Fact]
    public void Stop_all_pauses_the_current_unhandled_clip_and_every_handled_one()
    {
        string stop = StopSoundBody();

        int allBranch = stop.IndexOf("if (payload.all)", StringComparison.Ordinal);
        int handleBranch = stop.IndexOf("if (payload.handle)", StringComparison.Ordinal);
        allBranch.Should().BeGreaterThan(-1);
        handleBranch.Should().BeGreaterThan(allBranch);

        string all = stop[allBranch..handleBranch];
        all.Should().Contain("currentSound.pause()");
        all.Should().Contain("soundHandles[h].pause()");
    }

    [Fact]
    public void Stop_with_no_handle_and_no_all_stops_the_current_unhandled_clip()
    {
        string stop = StopSoundBody();

        int handleBranch = stop.IndexOf("if (payload.handle)", StringComparison.Ordinal);
        handleBranch.Should().BeGreaterThan(-1);
        string tail = stop[(handleBranch + 1)..];

        // The fallback after the handle branch's own return must still reach the current-slot stop.
        tail.Should().Contain("if (currentSound) { currentSound.pause(); currentSound = null; }");
    }
}
