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

using FluentAssertions;
using Jint;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Every sized first-party overlay carries one shared layout block (where it sits, how far from that edge, how
/// large) with the place it sits at today as the default, the four full-screen overlays and the two sound-only
/// ones carry none, and the browser source size follows the saved settings. The schema, the catalogue and the
/// pages are the real ones; nothing here is a copy.
/// </summary>
public sealed class WidgetLayoutSettingsTests
{
    // Where each sized overlay sat before the anchor setting existed.
    private static readonly Dictionary<string, string> AnchorToday = new()
    {
        ["alerts"] = "top",
        ["goal_bar"] = "bottom",
        ["labels"] = "bottom-left",
        ["raffle"] = "bottom",
        ["event_ticker"] = "center",
        ["chat_box"] = "bottom-left",
        ["now_playing"] = "bottom-left",
        ["sr_queue"] = "top-right",
        ["media_share"] = "center",
        ["tts_caption"] = "bottom",
        ["poll_prediction"] = "left",
        ["redemption_alert"] = "top",
        ["countdown_timer"] = "center",
        ["custom_data"] = "bottom-right",
        ["recent_followers"] = "top-left",
        ["sub_train"] = "top",
        ["socials"] = "bottom-right",
        ["top_cheerers"] = "top-right",
        ["lucky_feather"] = "bottom",
    };

    private static readonly string[] FullScreenKeys =
    [
        "drop_game",
        "emote_wall",
        "raid_recap",
        "stage",
    ];

    private static readonly string[] LayoutKeys = ["anchor", "edgeOffset", "scale"];

    private static WidgetSettingsSchema SchemaOf(string key) =>
        new WidgetSettingsSchemaProvider().GetByKey(key)!;

    [Fact]
    public void The_19_sized_overlays_each_have_anchor_edge_offset_and_scale_with_the_place_they_sit_today()
    {
        AnchorToday.Should().HaveCount(19);

        foreach ((string key, string anchor) in AnchorToday)
        {
            WidgetSettingsSchema schema = SchemaOf(key);
            Dictionary<string, WidgetSettingsField> fields = schema.Fields.ToDictionary(f => f.Key);

            fields.Should().ContainKeys(LayoutKeys, key);
            fields["anchor"].Type.Should().Be("select", key);
            fields["anchor"]
                .Options!.Select(o => o.Value)
                .Should()
                .Equal(FirstPartyWidgetCatalogue.Anchors, key);
            fields["anchor"].Default.Should().Be(anchor, key);
            fields["edgeOffset"].Type.Should().Be("number", key);
            fields["edgeOffset"].Default.Should().Be(key == "alerts" ? 86 : 4, key);
            fields["scale"].Default.Should().Be(100, key);
            fields["scale"].Min.Should().Be(50, key);
            fields["scale"].Max.Should().Be(200, key);
        }
    }

    [Fact]
    public void The_full_screen_and_the_sound_only_overlays_have_no_layout_block()
    {
        string[] withoutLayout =
        [
            .. FullScreenKeys,
            WidgetKeyAliases.Audio,
            WidgetKeyAliases.MusicPlayer,
        ];

        foreach (string key in withoutLayout)
            SchemaOf(key).Fields.Select(f => f.Key).Should().NotContain(LayoutKeys, key);
    }

    [Fact]
    public void The_old_position_setting_is_gone_from_both_schemas()
    {
        SchemaOf("tts_caption").Fields.Select(f => f.Key).Should().NotContain("position");
        SchemaOf("poll_prediction").Fields.Select(f => f.Key).Should().NotContain("position");
    }

    [Theory]
    [InlineData("tts_caption.vue", "top")]
    [InlineData("tts_caption.vue", "bottom")]
    [InlineData("poll_prediction.vue", "left")]
    [InlineData("poll_prediction.vue", "right")]
    public void A_saved_old_position_value_still_maps_to_the_anchor_the_page_lays_out_by(
        string asset,
        string oldPosition
    )
    {
        string source = WidgetAssetSource.Load(asset);
        string function = WidgetAssetSource
            .ExtractBlock(source, @"function\s+anchorOf\s*\(")
            .Replace(
                "function anchorOf(s: NnzWidgetSettings): string | undefined {",
                "function anchorOf(s) {"
            );
        Engine engine = new();
        engine.Execute(function);

        engine
            .Evaluate($"anchorOf({{ position: '{oldPosition}' }})")
            .AsString()
            .Should()
            .Be(oldPosition);
        engine
            .Evaluate($"anchorOf({{ position: '{oldPosition}', anchor: 'top-left' }})")
            .AsString()
            .Should()
            .Be("top-left", "a saved anchor wins over the old position");
    }

    [Fact]
    public void The_default_chat_box_fits_its_5_messages_under_1080_and_each_message_adds_its_own_height()
    {
        FirstPartyWidgetDefinition chat = FirstPartyWidgetCatalogue.Find("chat_box")!;
        Dictionary<string, object> defaults = new(chat.DefaultSettings);

        ObsBrowserSource basic = FirstPartyWidgetObsSizing.For(chat, defaults);
        ObsBrowserSource sixMessages = FirstPartyWidgetObsSizing.For(
            chat,
            new Dictionary<string, object>(defaults) { ["maxMessages"] = 6 }
        );
        ObsBrowserSource bigFont = FirstPartyWidgetObsSizing.For(
            chat,
            new Dictionary<string, object>(defaults) { ["fontSize"] = 24 }
        );

        defaults["maxMessages"].Should().Be(5);
        basic.Height.Should().BeLessThan(1080);
        basic.Should().Be(chat.ObsSetup, "the catalogue size is the size at the default settings");
        // Rendered in every theme: a one-line message with its name row is 114 px, plus a 23 px gap. The size
        // counts 1.5 lines of text, so a message is 154 px and 5 wrapped messages still fit.
        (sixMessages.Height - basic.Height)
            .Should()
            .Be(154, "one message, name row included, adds 154 px at font size 16");
        bigFont.Height.Should().BeGreaterThan(basic.Height);
    }

    [Fact]
    public void The_scale_setting_scales_the_browser_source_and_a_row_count_adds_its_rows()
    {
        FirstPartyWidgetDefinition queue = FirstPartyWidgetCatalogue.Find("sr_queue")!;

        ObsBrowserSource doubled = FirstPartyWidgetObsSizing.For(
            queue,
            new Dictionary<string, object> { ["scale"] = 200 }
        );
        ObsBrowserSource tenRows = FirstPartyWidgetObsSizing.For(
            queue,
            new Dictionary<string, object> { ["count"] = 10 }
        );

        doubled.Width.Should().Be(queue.ObsSetup.Width * 2);
        doubled.Height.Should().Be(queue.ObsSetup.Height * 2);
        (tenRows.Height - queue.ObsSetup.Height).Should().Be(5 * 60);
    }

    [Fact]
    public void The_19_sized_overlays_each_have_four_empty_side_margins_from_0_to_2000()
    {
        foreach (string key in AnchorToday.Keys)
        {
            Dictionary<string, WidgetSettingsField> fields = SchemaOf(key)
                .Fields.ToDictionary(f => f.Key);

            foreach (string side in FirstPartyWidgetCatalogue.MarginSettingKeys)
            {
                fields.Should().ContainKey(side, key);
                (fields[side].Type, fields[side].Default, fields[side].Min, fields[side].Max)
                    .Should()
                    .Be(("number", "", 0, 2000), $"{key}.{side}");
            }
        }
    }

    // Owner, 2026-11-26: "i need to be able to set margin on all sides". A side margin larger than the edge
    // distance moves the box inward, so the browser source grows by that much or the box is cut off.
    [Fact]
    public void A_side_margin_larger_than_the_edge_distance_grows_the_browser_source_by_the_difference()
    {
        FirstPartyWidgetDefinition queue = FirstPartyWidgetCatalogue.Find("sr_queue")!;

        ObsBrowserSource margins = FirstPartyWidgetObsSizing.For(
            queue,
            new Dictionary<string, object>
            {
                ["marginTop"] = 32,
                ["marginRight"] = 32,
                ["marginBottom"] = "",
                ["marginLeft"] = 0,
            }
        );
        ObsBrowserSource biggerEdge = FirstPartyWidgetObsSizing.For(
            queue,
            new Dictionary<string, object> { ["edgeOffset"] = 20 }
        );

        (margins.Width - queue.ObsSetup.Width).Should().Be(28 - 4, "right +28, left -4");
        (margins.Height - queue.ObsSetup.Height).Should().Be(28, "top +28, bottom keeps the edge");
        (biggerEdge.Width - queue.ObsSetup.Width).Should().Be(32);
        (biggerEdge.Height - queue.ObsSetup.Height).Should().Be(32);
    }

    [Fact]
    public void An_overlay_without_a_layout_block_keeps_its_size_whatever_is_saved()
    {
        FirstPartyWidgetDefinition stage = FirstPartyWidgetCatalogue.Find("stage")!;

        FirstPartyWidgetObsSizing
            .For(stage, new Dictionary<string, object> { ["scale"] = 200 })
            .Should()
            .Be(stage.ObsSetup);
    }
}
