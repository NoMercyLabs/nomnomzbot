// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// Regression guard for the ungated-mutation fix (custom-events.md §5, sound-system.md §5, pronouns.md §5):
/// <c>[RequireAction]</c> is an ASP.NET authorization attribute, so it is invisible to a unit test that calls a
/// controller method directly (bypassing the MVC filter pipeline entirely) — the only way to prove a route is
/// actually gated, and gated on the RIGHT key, is to read the attribute back off the action method. A dropped
/// or mis-keyed attribute here is exactly how a moderator regains the ability to clear an Editor floor.
/// </summary>
public sealed class RequireActionCoverageTests
{
    private static string RequiredActionKeyOf(Type controllerType, string methodName)
    {
        MethodInfo method =
            controllerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(controllerType.Name, methodName);

        RequireActionAttribute? attribute = method.GetCustomAttribute<RequireActionAttribute>();
        attribute.Should().NotBeNull($"{controllerType.Name}.{methodName} must be Gate-2 gated");
        return attribute!.ActionKey;
    }

    [Theory]
    [InlineData(nameof(CustomDataSourcesController.List), "customdata:read")]
    [InlineData(nameof(CustomDataSourcesController.ListPresets), "customdata:read")]
    [InlineData(nameof(CustomDataSourcesController.Get), "customdata:read")]
    [InlineData(nameof(CustomDataSourcesController.Create), "customdata:write")]
    [InlineData(nameof(CustomDataSourcesController.Update), "customdata:write")]
    [InlineData(nameof(CustomDataSourcesController.Delete), "customdata:write")]
    [InlineData(nameof(CustomDataSourcesController.Test), "customdata:write")]
    public void CustomDataSourcesController_action_carries_the_expected_action_key(
        string methodName,
        string expectedActionKey
    )
    {
        RequiredActionKeyOf(typeof(CustomDataSourcesController), methodName)
            .Should()
            .Be(expectedActionKey);
    }

    [Theory]
    [InlineData(nameof(SoundClipsController.List), "sounds:read")]
    [InlineData(nameof(SoundClipsController.Get), "sounds:read")]
    [InlineData(nameof(SoundClipsController.Upload), "sounds:write")]
    [InlineData(nameof(SoundClipsController.Update), "sounds:write")]
    [InlineData(nameof(SoundClipsController.Delete), "sounds:write")]
    [InlineData(nameof(SoundClipsController.Preview), "sounds:write")]
    public void SoundClipsController_action_carries_the_expected_action_key(
        string methodName,
        string expectedActionKey
    )
    {
        RequiredActionKeyOf(typeof(SoundClipsController), methodName)
            .Should()
            .Be(expectedActionKey);
    }

    [Fact]
    public void PronounsController_PutMe_carries_the_self_write_action_key()
    {
        RequiredActionKeyOf(typeof(PronounsController), nameof(PronounsController.PutMe))
            .Should()
            .Be("pronouns:self:write");
    }

    [Theory]
    [InlineData(nameof(ChatController.GetMessages), "chat:read")]
    [InlineData(nameof(ChatController.SendMessage), "chat:send")]
    public void ChatController_action_carries_the_expected_action_key(
        string methodName,
        string expectedActionKey
    )
    {
        RequiredActionKeyOf(typeof(ChatController), methodName).Should().Be(expectedActionKey);
    }

    [Theory]
    [InlineData(nameof(MusicController.GetConfig), "music:config:read")]
    [InlineData(nameof(MusicController.AddToQueue), "music:request:submit")]
    [InlineData(nameof(MusicController.Seek), "music:remote:control")]
    [InlineData(nameof(MusicController.SetShuffle), "music:remote:control")]
    [InlineData(nameof(MusicController.SetRepeat), "music:remote:control")]
    [InlineData(nameof(MusicController.GetDevices), "music:remote:control")]
    [InlineData(nameof(MusicController.Transfer), "music:remote:control")]
    [InlineData(nameof(MusicController.PlayContext), "music:remote:control")]
    [InlineData(nameof(MusicController.GetPlaylists), "music:library:write")]
    [InlineData(nameof(MusicController.ListBlockedTracks), "music:config:read")]
    [InlineData(nameof(MusicController.BlockTrack), "music:queue:moderate")]
    [InlineData(nameof(MusicController.UnblockTrack), "music:queue:moderate")]
    public void MusicController_action_carries_the_expected_action_key(
        string methodName,
        string expectedActionKey
    )
    {
        RequiredActionKeyOf(typeof(MusicController), methodName).Should().Be(expectedActionKey);
    }

    [Theory]
    [InlineData(nameof(StreamController.GetStreamInfo), "stream:read")]
    [InlineData(nameof(StreamController.GetStatus), "stream:read")]
    [InlineData(nameof(StreamController.SearchCategories), "stream:read")]
    public void StreamController_read_carries_the_stream_read_key(
        string methodName,
        string expectedActionKey
    )
    {
        RequiredActionKeyOf(typeof(StreamController), methodName).Should().Be(expectedActionKey);
    }

    [Fact]
    public void ChannelsController_GetChannel_carries_the_dashboard_read_key()
    {
        RequiredActionKeyOf(typeof(ChannelsController), nameof(ChannelsController.GetChannel))
            .Should()
            .Be("dashboard:read");
    }

    [Fact]
    public void UsersController_SearchUsers_carries_the_community_read_key()
    {
        RequiredActionKeyOf(typeof(UsersController), nameof(UsersController.SearchUsers))
            .Should()
            .Be("community:read");
    }
}
