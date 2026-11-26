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

using System.Globalization;
using System.Text.Json;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// The browser source size an overlay needs for its saved settings. The catalogue declares the smallest size at
/// the default settings; a setting that changes how much the overlay draws (the number of chat messages, the
/// number of rows or choices) or how large it draws (the scale) changes the size by the amount its CSS adds.
/// One place computes it, so the dashboard and the OBS install read the same answer.
/// </summary>
public static class FirstPartyWidgetObsSizing
{
    /// <summary>
    /// Pixels one chat message adds to the chat box at the 1.45 zoom of the theme. A message is a 30 px name row
    /// and the text, with 8 px padding above and below, a 4 px border on each side (the sticker look), and text at
    /// 1.5 times the font size; messages wrap, so the text is counted as 1.5 lines on average. Plus the 16 px gap
    /// to the next one. At the default font size of 16: (1.5 lines x 1.5 x 16 + 30 + 16 + 8 + 16) x 1.45 = 154 px.
    /// </summary>
    private const double ChatLineChrome = 30 + 16 + 8 + 16;
    private const double ChatAverageLines = 1.5;
    private const double ChatLineHeight = 1.5;
    private const double ThemeZoom = 1.45;

    /// <summary>The bottom padding and the shadow of the chat column, in px.</summary>
    private const double ChatColumnExtra = 12 * ThemeZoom + 8;

    /// <summary>
    /// A list row at the zoom of the theme: a 24 px number plus 12 px padding, plus the 5 px gap to the next row.
    /// (24 + 12 + 5) x 1.45 = 60 px.
    /// </summary>
    private const int RowPx = 60;

    /// <summary>A poll choice: 8 px margin, a 17.5 px label row, 4 px gap and an 18 px bar, at the zoom: 69 px.</summary>
    private const int PollChoicePx = 69;

    private const string ChatBoxKey = "chat_box";
    private const string MaxMessagesKey = "maxMessages";
    private const string FontSizeKey = "fontSize";
    private const string CountKey = "count";
    private const string MaxChoicesKey = "maxChoices";
    private const int DefaultRows = 5;
    private const int DefaultFontSize = 16;

    /// <summary>The size for <paramref name="definition"/> with the settings a streamer saved (an absent key means its default).</summary>
    public static ObsBrowserSource For(
        FirstPartyWidgetDefinition definition,
        IReadOnlyDictionary<string, object> saved
    )
    {
        ObsBrowserSource basis = definition.ObsSetup;
        int height = definition.Key switch
        {
            ChatBoxKey => ChatBoxHeight(definition, saved),
            "sr_queue" or "recent_followers" or "top_cheerers" => basis.Height
                + (Rows(definition, saved, CountKey) - DefaultRows) * RowPx,
            "poll_prediction" => basis.Height
                + (Rows(definition, saved, MaxChoicesKey) - DefaultRows) * PollChoicePx,
            _ => basis.Height,
        };
        bool placed = definition.DefaultSettings.ContainsKey(
            FirstPartyWidgetCatalogue.ScaleSettingKey
        );
        double scale = placed ? Scale(saved) : 1.0;
        (double extraWidth, double extraHeight) = placed ? MarginGrowth(definition, saved) : (0, 0);

        return basis with
        {
            Width = Math.Max(1, (int)Math.Ceiling(basis.Width * scale + extraWidth)),
            Height = Math.Max(1, (int)Math.Ceiling(Math.Max(1, height) * scale + extraHeight)),
        };
    }

    // The catalogue size holds the default edge distance on every side. A side margin, or a changed edge
    // distance where a side has no margin, moves the box by the difference; margins are screen pixels, so they
    // are not scaled.
    private static (double Width, double Height) MarginGrowth(
        FirstPartyWidgetDefinition definition,
        IReadOnlyDictionary<string, object> saved
    )
    {
        double defaultEdge =
            Number(definition.DefaultSettings, FirstPartyWidgetCatalogue.EdgeOffsetSettingKey)
            ?? FirstPartyWidgetCatalogue.DefaultEdgeOffset;
        double edge = Number(saved, FirstPartyWidgetCatalogue.EdgeOffsetSettingKey) ?? defaultEdge;
        // The same rule as the SDK: a negative or empty margin keeps the edge distance, a margin stops at 2000.
        double Side(string key) =>
            Number(saved, key) is >= 0 and double margin ? Math.Min(margin, 2000) : edge;
        IReadOnlyList<string> sides = FirstPartyWidgetCatalogue.MarginSettingKeys;

        return (
            Side(sides[1]) + Side(sides[3]) - 2 * defaultEdge,
            Side(sides[0]) + Side(sides[2]) - 2 * defaultEdge
        );
    }

    private static int ChatBoxHeight(
        FirstPartyWidgetDefinition definition,
        IReadOnlyDictionary<string, object> saved
    )
    {
        int messages = Rows(definition, saved, MaxMessagesKey);
        double fontSize = Number(saved, FontSizeKey) ?? DefaultFontSize;
        double perMessage = Math.Ceiling(
            (ChatAverageLines * ChatLineHeight * fontSize + ChatLineChrome) * ThemeZoom
        );

        return (int)Math.Ceiling(messages * perMessage + ChatColumnExtra);
    }

    private static int Rows(
        FirstPartyWidgetDefinition definition,
        IReadOnlyDictionary<string, object> saved,
        string key
    )
    {
        double? savedValue = Number(saved, key);
        double fallback = Number(definition.DefaultSettings, key) ?? DefaultRows;

        return Math.Max(1, (int)Math.Round(savedValue ?? fallback));
    }

    private static double Scale(IReadOnlyDictionary<string, object> saved)
    {
        double percent =
            Number(saved, FirstPartyWidgetCatalogue.ScaleSettingKey)
            ?? FirstPartyWidgetCatalogue.DefaultScale;

        return Math.Clamp(
                percent,
                FirstPartyWidgetCatalogue.MinScale,
                FirstPartyWidgetCatalogue.MaxScale
            ) / 100.0;
    }

    // A saved number is a JsonElement when it came from the database and a CLR number when it was authored in code.
    private static double? Number(IReadOnlyDictionary<string, object> settings, string key) =>
        settings.TryGetValue(key, out object? value) ? NumberOf(value) : null;

    private static double? NumberOf(object value)
    {
        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
            JsonElement { ValueKind: JsonValueKind.String } element
                when double.TryParse(
                    element.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed
                ) => parsed,
            IConvertible convertible and not string => convertible.ToDouble(
                CultureInfo.InvariantCulture
            ),
            _ => null,
        };
    }
}
