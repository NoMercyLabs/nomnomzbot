// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Include;

/// <summary>A template command and a read-only built-in available to the matrix channel.</summary>
internal static class IncludeSurfaceFixture
{
    public static IEnumerable<string> ShapesFor(string key) =>
        key switch
        {
            "include.cmd" => [":hug"],
            "include.builtin" => [":uptime"],
            _ => [],
        };

    public static void Configure(ServiceCollection services, ChannelContext channel)
    {
        channel.Commands["hug"] = new CachedCommand
        {
            Name = "hug",
            TemplateResponses = ["A hug for the viewer"],
            Tier = "template",
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
        };

        IBuiltinCommand uptime = Substitute.For<IBuiltinCommand>();
        uptime.BuiltinKey.Returns("uptime");
        uptime.WhisperSafe.Returns(true);
        uptime.DefaultMinPermissionLevel.Returns(0);
        uptime
            .ExecuteAsync(Arg.Any<BuiltinCommandContext>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success("The stream is live."));
        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        catalog.Get("uptime").Returns(uptime);
        services.AddSingleton(catalog);
    }
}
