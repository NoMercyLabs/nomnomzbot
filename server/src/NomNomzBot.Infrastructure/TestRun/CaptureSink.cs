// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.TestRun;

/// <summary>
/// The shared accumulator behind every dry-run (script capture bridge + pipeline capturing actions). Records each
/// side-effecting call that WOULD have fired as a <see cref="CapturedEffectDto"/> and collects any chat text a
/// captured chat effect carried, so a test-run can surface both without touching a real surface. Not thread-safe: a
/// single sandbox / pipeline run is single-threaded, and each run owns its own sink.
/// </summary>
public sealed class CaptureSink
{
    private const int MaxPreviewLength = 500;

    private readonly List<CapturedEffectDto> _effects = [];
    private readonly List<string> _chatOutput = [];
    private readonly List<TimelineEntryDto> _timeline = [];

    public IReadOnlyList<CapturedEffectDto> Effects => _effects;
    public IReadOnlyList<string> ChatOutput => _chatOutput;

    /// <summary>Every captured effect, chat message and console line, in the order they happened.</summary>
    public IReadOnlyList<TimelineEntryDto> Timeline => _timeline;

    /// <summary>Record one captured effect from its host-call/action name and its argument list.</summary>
    public void Record(string name, IReadOnlyList<string> args) =>
        Record(name, string.Join(" | ", args));

    /// <summary>Record one captured effect from its name and an already-rendered argument preview.</summary>
    public void Record(string name, string argsPreview)
    {
        CapturedEffectDto effect = AddEffect(name, argsPreview);
        AddTimeline("effect", $"{effect.Name}: {effect.ArgsPreview}");
    }

    /// <summary>Record a chat send as one effect and one chat message, but ONE timeline row.</summary>
    public void RecordChat(string name, IReadOnlyList<string> args)
    {
        AddEffect(name, string.Join(" | ", args));
        AddChatOutput(args.Count > 0 ? args[0] : string.Empty);
    }

    public void AddChatOutput(string text)
    {
        _chatOutput.Add(text);
        AddTimeline("chat", text);
    }

    /// <summary>A line the script wrote with <c>console.*</c>, placed at the moment it happened.</summary>
    public void AddConsoleLine(string text) => AddTimeline("console", text);

    /// <summary>A <c>bot.send</c> message: a timeline row only, the direct output channel is reported separately.</summary>
    public void AddBotSend(string text) => AddTimeline("chat", text);

    private CapturedEffectDto AddEffect(string name, string argsPreview)
    {
        CapturedEffectDto effect = new(name, Preview(argsPreview));
        _effects.Add(effect);
        return effect;
    }

    private void AddTimeline(string kind, string text) =>
        _timeline.Add(new(_timeline.Count + 1, kind, text));

    private static string Preview(string raw) =>
        raw.Length <= MaxPreviewLength ? raw : raw[..MaxPreviewLength] + "…";
}
