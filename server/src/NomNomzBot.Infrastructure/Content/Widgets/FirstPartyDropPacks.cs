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

using System.Reflection;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// The drop game's first-party theme packs, embedded from <c>Content/Widgets/DropPacks/&lt;id&gt;/</c>: a
/// <c>pack.json</c> plus the art and sounds it names (drop-game spec section 4). The server never reads a pack's
/// art; it only serves the files. An unknown or missing pack id resolves to <see cref="DefaultId"/>.
/// </summary>
public static class FirstPartyDropPacks
{
    public const string DefaultId = "parachute";
    public const string ManifestFile = "pack.json";

    private const string Prefix = "NomNomzBot.Infrastructure.Content.Widgets.DropPacks/";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, byte[]>> Packs =
        Load();

    /// <summary>The id of every shipped pack, the default first.</summary>
    public static IReadOnlyList<string> Ids { get; } =
    [.. Packs.Keys.OrderBy(id => id == DefaultId ? 0 : 1).ThenBy(id => id, StringComparer.Ordinal)];

    /// <summary>The pack id itself when that pack ships, otherwise the default pack.</summary>
    public static string Resolve(string? packId) =>
        packId is not null && Packs.ContainsKey(packId) ? packId : DefaultId;

    /// <summary>One file of the resolved pack, or null when the pack carries no file of that name.</summary>
    public static byte[]? ReadFile(string? packId, string file) =>
        Packs.TryGetValue(Resolve(packId), out IReadOnlyDictionary<string, byte[]>? files)
        && files.TryGetValue(file, out byte[]? bytes)
            ? bytes
            : null;

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, byte[]>> Load()
    {
        Assembly assembly = typeof(FirstPartyDropPacks).Assembly;
        Dictionary<string, Dictionary<string, byte[]>> packs = new(StringComparer.Ordinal);

        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            string name = resourceName.Replace('\\', '/');
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            string[] parts = name[Prefix.Length..].Split('/', 2);
            if (parts.Length != 2)
                continue;

            using System.IO.Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            if (!packs.TryGetValue(parts[0], out Dictionary<string, byte[]>? files))
                packs[parts[0]] = files = new(StringComparer.Ordinal);
            files[parts[1]] = buffer.ToArray();
        }

        return packs
            .Where(pack => pack.Value.ContainsKey(ManifestFile))
            .ToDictionary(
                pack => pack.Key,
                pack => (IReadOnlyDictionary<string, byte[]>)pack.Value,
                StringComparer.Ordinal
            );
    }
}
