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
using NomNomzBot.Infrastructure.TestRun;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The capture-mode decorator around the real <see cref="IScriptHostBridge"/> that makes a script test-run a true
/// DRY-RUN (custom-code.md §6). For a READ capability it delegates straight to the inner bridge, so the script sees
/// real data (users, balances, storage reads, now-playing, http.fetch, …). For a side-effecting WRITE capability
/// (chat/tts/widget/storage-write/reward/schedule/music-queue) it RECORDS the call + args into the shared
/// <see cref="ScriptCaptureSink"/> and returns a benign success primitive matching that capability's guest contract
/// — nothing is ever dispatched to chat, TTS, an overlay, the store, or Twitch. Fail-closed by construction: any key
/// NOT in the captured set falls through to the inner bridge unchanged (the grant already gated access).
/// </summary>
public sealed class CaptureScriptHostBridge(IScriptHostBridge inner, CaptureSink sink)
    : IScriptHostBridge
{
    // The side-effecting write capabilities and the benign primitive each returns to the guest so the script's own
    // control flow proceeds as if the write had succeeded. null = the real dispatch also returns null (e.g. chat.send)
    // or the value is built specially (tts.speak). Keys ABSENT here are reads and delegate to the real bridge.
    private static readonly IReadOnlyDictionary<string, string?> CapturedReturns = new Dictionary<
        string,
        string?
    >(StringComparer.Ordinal)
    {
        ["chat.send"] = null,
        ["chat.reply"] = null,
        ["music.queue"] = "true",
        ["storage.set"] = "ok",
        ["storage.delete"] = "ok",
        ["tts.speak"] = null,
        ["tts.voice.set"] = "ok",
        ["widget.emit"] = "ok",
        ["reward.update"] = "ok",
        ["schedule.pipeline"] = "ok",
    };

    public HostImportDelegate Resolve(string capabilityKey)
    {
        if (!CapturedReturns.TryGetValue(capabilityKey, out string? cannedReturn))
            return inner.Resolve(capabilityKey); // read capability — run for real

        return (key, args, _) =>
        {
            sink.Record(key, args);
            if (key is "chat.send" or "chat.reply")
                sink.AddChatOutput(args.Count > 0 ? args[0] : string.Empty);
            // tts.speak's guest wrapper JSON.parses a { voiceId, characterCount } object; hand it a benign one.
            if (key == "tts.speak")
            {
                int characterCount = args.Count > 0 ? args[0].Length : 0;
                string? voiceId = args.Count > 1 ? args[1] : null;
                return voiceId is null
                    ? $"{{\"voiceId\":null,\"characterCount\":{characterCount}}}"
                    : $"{{\"voiceId\":\"{voiceId}\",\"characterCount\":{characterCount}}}";
            }
            return cannedReturn;
        };
    }
}
