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

    public IReadOnlyList<CapturedEffectDto> Effects => _effects;
    public IReadOnlyList<string> ChatOutput => _chatOutput;

    /// <summary>Record one captured effect from its host-call/action name and its argument list.</summary>
    public void Record(string name, IReadOnlyList<string> args) =>
        _effects.Add(new CapturedEffectDto(name, Preview(string.Join(" | ", args))));

    /// <summary>Record one captured effect from its name and an already-rendered argument preview.</summary>
    public void Record(string name, string argsPreview) =>
        _effects.Add(new CapturedEffectDto(name, Preview(argsPreview)));

    public void AddChatOutput(string text) => _chatOutput.Add(text);

    private static string Preview(string raw) =>
        raw.Length <= MaxPreviewLength ? raw : raw[..MaxPreviewLength] + "…";
}
