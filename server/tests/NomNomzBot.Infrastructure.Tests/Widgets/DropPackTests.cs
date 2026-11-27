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

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The drop game draws everything from a theme pack (drop-game spec section 4): the shipped <c>parachute</c> pack
/// must carry every slot of the pack format, every file its manifest names, and the widget's <c>pack_id</c>
/// setting must fall back to it for an unknown id, so an old widget row without the setting keeps working.
/// </summary>
public sealed class DropPackTests
{
    private static JsonElement Manifest()
    {
        byte[]? bytes = FirstPartyDropPacks.ReadFile("parachute", FirstPartyDropPacks.ManifestFile);
        bytes.Should().NotBeNull("the parachute pack ships a pack.json");
        return JsonDocument.Parse(bytes!).RootElement.Clone();
    }

    private static IEnumerable<string> Sources(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (property.Name == "src" && property.Value.ValueKind == JsonValueKind.String)
                    yield return property.Value.GetString()!;
                foreach (string nested in Sources(property.Value))
                    yield return nested;
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
            foreach (string nested in Sources(item))
                yield return nested;
        }
    }

    [Fact]
    public void The_parachute_manifest_carries_every_field_of_the_pack_format()
    {
        JsonElement pack = Manifest();

        pack.GetProperty("id").GetString().Should().Be("parachute");
        pack.GetProperty("kind").GetString().Should().Be("drop");
        foreach (string field in new[] { "name", "version", "author", "license" })
            pack.GetProperty(field).GetString().Should().NotBeNullOrWhiteSpace(field);
        pack.GetProperty("credits").GetArrayLength().Should().BePositive();
        pack.GetProperty("stage").GetProperty("w").GetInt32().Should().Be(1920);
        pack.GetProperty("stage").GetProperty("h").GetInt32().Should().Be(1080);

        JsonElement motion = pack.GetProperty("motion");
        motion.GetProperty("carrier").GetString().Should().Be("attached");
        motion.GetProperty("sway").GetDouble().Should().BePositive();

        JsonElement slots = pack.GetProperty("slots");
        foreach (string slot in new[] { "background", "foreground" })
            slots
                .TryGetProperty(slot, out _)
                .Should()
                .BeTrue($"the {slot} slot is declared, even when empty");
        foreach (string slot in new[] { "carrier", "payload" })
            slots.GetProperty(slot).GetProperty("src").GetString().Should().NotBeNullOrEmpty(slot);

        // The band art is optional (src null), but the scoring zone must still be drawn by its marker.
        JsonElement target = slots.GetProperty("target");
        target
            .TryGetProperty("src", out _)
            .Should()
            .BeTrue("the target declares its band art, even when null");
        target.GetProperty("hitbox").GetProperty("w").GetDouble().Should().BePositive();
        target
            .GetProperty("marker")
            .GetProperty("src")
            .GetString()
            .Should()
            .NotBeNullOrEmpty("the marker shows the zone");

        JsonElement landed = slots.GetProperty("landed");
        JsonElement grow = landed.GetProperty("growWithScore");
        grow.GetProperty("maxScale")
            .GetDouble()
            .Should()
            .BeGreaterThan(
                grow.GetProperty("minScale").GetDouble(),
                "a higher score grows a bigger seedling"
            );
        List<int> thresholds =
        [
            .. landed
                .GetProperty("stages")
                .EnumerateArray()
                .Select(s => s.GetProperty("minScore").GetInt32()),
        ];
        // One growth stage is enough: growWithScore already makes a higher score a bigger plant.
        thresholds.Should().NotBeEmpty().And.BeInAscendingOrder();
        thresholds
            .Should()
            .OnlyContain(t => t > 0, "a score of 0 is a miss, which draws the miss slot");
        landed
            .TryGetProperty("miss", out _)
            .Should()
            .BeTrue("the miss slot is declared, even when the payload itself stays");

        pack.GetProperty("effects").ValueKind.Should().Be(JsonValueKind.Array);
        pack.GetProperty("sounds").ValueKind.Should().Be(JsonValueKind.Object);
        pack.GetProperty("labels").GetProperty("tag").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// The parachute pack re-creates Coding Garden's SeedlingDrop as it played with its parachute (commit 88eeffd,
    /// client/styles.css and client/app.js): a 150 px canopy, a 56 px seed tucked 20 px into its lines, the name
    /// above the canopy in outlined type, a steady 10 degree sway, a seedling of score x 1.5 px grown over 5 s,
    /// the seed left standing on a miss, the ground on the screen's bottom edge, and no extra effects.
    /// </summary>
    [Fact]
    public void The_parachute_pack_matches_the_seedling_drop_reference()
    {
        JsonElement pack = Manifest();
        JsonElement slots = pack.GetProperty("slots");

        pack.GetProperty("motion").GetProperty("sway").GetDouble().Should().Be(10);

        JsonElement carrier = slots.GetProperty("carrier");
        carrier.GetProperty("size").GetProperty("w").GetInt32().Should().Be(150);
        carrier.GetProperty("size").GetProperty("h").GetInt32().Should().Be(135);

        JsonElement payload = slots.GetProperty("payload");
        payload.GetProperty("src").GetString().Should().Be("seed.svg", "the seed is what falls");
        payload.GetProperty("size").GetProperty("w").GetInt32().Should().Be(56);
        payload.GetProperty("size").GetProperty("h").GetInt32().Should().Be(72);
        payload
            .GetProperty("tuck")
            .GetInt32()
            .Should()
            .Be(20, "the seed hangs 20 px into the canopy lines");

        JsonElement target = slots.GetProperty("target");
        target
            .GetProperty("hitbox")
            .GetProperty("y")
            .GetInt32()
            .Should()
            .Be(
                target.GetProperty("size").GetProperty("h").GetInt32(),
                "drops land on the bottom edge"
            );
        JsonElement marker = target.GetProperty("marker").GetProperty("size");
        marker
            .GetProperty("w")
            .GetInt32()
            .Should()
            .Be(
                target.GetProperty("hitbox").GetProperty("w").GetInt32(),
                "the hill is the scoring zone"
            );

        JsonElement landed = slots.GetProperty("landed");
        landed
            .GetProperty("size")
            .GetProperty("h")
            .GetInt32()
            .Should()
            .Be(150, "score 100 is 150 px");
        landed.GetProperty("growWithScore").GetProperty("minScale").GetDouble().Should().Be(0);
        landed.GetProperty("growWithScore").GetProperty("maxScale").GetDouble().Should().Be(1);
        landed.GetProperty("growMs").GetInt32().Should().Be(5000);
        landed
            .TryGetProperty("miss", out JsonElement miss)
            .Should()
            .BeTrue("the miss slot is declared");
        miss.ValueKind.Should().Be(JsonValueKind.Null, "a miss leaves the seed itself standing");

        pack.GetProperty("effects").GetArrayLength().Should().Be(0, "SeedlingDrop has no burst");
        pack.GetProperty("labels").GetProperty("tag").GetString().Should().Be("outline");
    }

    [Fact]
    public void Every_file_the_manifest_names_is_a_themable_vector_the_pack_carries()
    {
        List<string> sources = [.. Sources(Manifest()).Distinct(StringComparer.Ordinal)];

        // SeedlingDrop's four pictures: parachute, seed, grass hill, seedling.
        sources.Should().HaveCountGreaterThanOrEqualTo(4);
        foreach (string src in sources)
        {
            byte[]? file = FirstPartyDropPacks.ReadFile("parachute", src);
            file.Should().NotBeNull($"pack.json names '{src}', so the pack must serve it");
            src.Should().EndWith(".svg", "the widget inlines the art so the theme can color it");
            string svg = Encoding.UTF8.GetString(file!);

            // The license sits after </svg>: a comment above the root breaks inlining and editors.
            svg.Should().StartWith("<svg", src);
            int close = svg.IndexOf("</svg>", StringComparison.Ordinal);
            close.Should().BePositive(src);
            svg[close..]
                .Should()
                .Contain("SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0", src);

            // Each copy is inlined many times on one page, so ids would clash.
            svg.Should().NotContain(" id=", src);
            // Every color is a drop variable, so a theme can recolor it; the fallback keeps the original look.
            string body = Regex.Replace(
                svg[..close],
                @"var\(--drop-[a-z-]+, #[0-9a-f]{6}\)",
                "VAR"
            );
            Regex
                .IsMatch(body, @"#[0-9a-fA-F]{3,8}\b|rgb\(")
                .Should()
                .BeFalse($"'{src}' holds a raw color outside a --drop variable");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("eagle-nest-not-installed")]
    [InlineData("../parachute")]
    public void An_unknown_pack_id_falls_back_to_parachute(string? packId)
    {
        FirstPartyDropPacks.Resolve(packId).Should().Be("parachute");
        FirstPartyDropPacks
            .ReadFile(packId, FirstPartyDropPacks.ManifestFile)
            .Should()
            .Equal(FirstPartyDropPacks.ReadFile("parachute", FirstPartyDropPacks.ManifestFile));
    }

    [Theory]
    [InlineData("../pack.json")]
    [InlineData("missing.svg")]
    [InlineData("")]
    public void A_file_the_pack_does_not_carry_is_not_found(string file)
    {
        FirstPartyDropPacks.ReadFile("parachute", file).Should().BeNull();
    }

    [Fact]
    public void The_drop_game_defaults_to_the_parachute_pack_and_offers_every_shipped_pack()
    {
        FirstPartyWidgetDefinition drop = FirstPartyWidgetCatalogue.All.Single(w =>
            w.Key == "drop_game"
        );
        drop.DefaultSettings["pack_id"].Should().Be(FirstPartyDropPacks.DefaultId);
        drop.DefaultSettings["boardAnchor"].Should().Be("top");

        WidgetSettingsSchema schema = new WidgetSettingsSchemaProvider().GetByKey("drop_game")!;
        WidgetSettingsField pack = schema.Fields.Single(f => f.Key == "pack_id");
        pack.Type.Should().Be("select");
        pack.Default.Should().Be("parachute");
        pack.Options!.Select(o => o.Value).Should().Equal(FirstPartyDropPacks.Ids);
        pack.Options!.Single().Label.Key.Should().Be("widget.drop_game.pack_id.option.parachute");

        WidgetSettingsField board = schema.Fields.Single(f => f.Key == "boardAnchor");
        board.Options!.Select(o => o.Value).Should().Equal(FirstPartyWidgetCatalogue.Anchors);
        schema.Fields.Should().NotContain(f => f.Key == FirstPartyWidgetCatalogue.AnchorSettingKey);

        WidgetSettingsSchema raffle = new WidgetSettingsSchemaProvider().GetByKey("raffle")!;
        raffle.Fields.Should().NotContain(f => f.Key == "pack_id" || f.Key == "boardAnchor");
    }
}
