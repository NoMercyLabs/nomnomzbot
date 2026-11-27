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

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The overlay serves the drop game's theme packs beside its theme (<c>/overlay/drop-packs/{packId}/{file}</c>):
/// the manifest and every file it names, an unknown pack as the default <c>parachute</c>, and nothing outside a
/// pack's own files.
/// </summary>
public sealed class OverlayDropPackControllerTests
{
    private static OverlayDropPackController Controller() =>
        new() { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };

    private static FileContentResult Served(string packId, string file) =>
        Controller().Get(packId, file).Should().BeOfType<FileContentResult>().Subject;

    private static JsonElement ManifestOf(string packId) =>
        JsonDocument.Parse(Served(packId, "pack.json").FileContents).RootElement.Clone();

    [Fact]
    public void The_manifest_is_served_as_json_and_every_file_it_names_as_an_image()
    {
        FileContentResult manifest = Served("parachute", "pack.json");
        manifest.ContentType.Should().StartWith("application/json");

        JsonElement slots = ManifestOf("parachute").GetProperty("slots");
        List<string> files =
        [
            slots.GetProperty("carrier").GetProperty("src").GetString()!,
            slots.GetProperty("payload").GetProperty("src").GetString()!,
            slots.GetProperty("target").GetProperty("marker").GetProperty("src").GetString()!,
            slots.GetProperty("landed").GetProperty("miss").GetProperty("src").GetString()!,
            .. slots
                .GetProperty("landed")
                .GetProperty("stages")
                .EnumerateArray()
                .Select(s => s.GetProperty("src").GetString()!),
        ];

        foreach (string file in files)
        {
            FileContentResult art = Served("parachute", file);
            art.ContentType.Should().Be("image/svg+xml", file);
            art.FileContents.Should().NotBeEmpty(file);
        }
    }

    [Fact]
    public void An_unknown_pack_is_served_as_the_default_parachute_pack()
    {
        ManifestOf("eagle-nest-not-installed")
            .GetProperty("id")
            .GetString()
            .Should()
            .Be("parachute");
    }

    [Theory]
    [InlineData("parachute", "missing.svg")]
    [InlineData("parachute", "..%2F..%2Fappsettings.json")]
    [InlineData("..", "pack.json.bak")]
    public void A_file_outside_the_pack_is_not_found(string packId, string file)
    {
        Controller().Get(packId, file).Should().BeOfType<NotFoundResult>();
    }
}
