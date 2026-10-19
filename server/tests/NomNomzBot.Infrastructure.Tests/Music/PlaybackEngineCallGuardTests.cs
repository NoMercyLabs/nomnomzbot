// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Spec music-playback-determinism, test 30 (source-scan guard). A control surface (REST controller,
/// widget hub, Automation API handler, chat command, pipeline action) sends a director input. It never
/// names an engine (the Spotify or YouTube provider, an <c>IPlaybackEngine</c>, the YouTube player
/// dispatcher) at a music control call (play, pause, resume, skip, previous, stop, seek, volume, start).
/// Only the engines, their adapters and the director talk to an engine.
/// </summary>
public sealed class PlaybackEngineCallGuardTests
{
    /// <summary>Directories that hold control surfaces, relative to <c>server/src</c>.</summary>
    private static readonly string[] SurfaceRoots =
    [
        Path.Combine("NomNomzBot.Api"),
        Path.Combine("NomNomzBot.Infrastructure", "AutomationApi"),
        Path.Combine("NomNomzBot.Infrastructure", "Commands"),
        Path.Combine("NomNomzBot.Infrastructure", "Music", "PipelineActions"),
        Path.Combine("NomNomzBot.Infrastructure", "Music", "Pipeline"),
    ];

    /// <summary>An engine named as a type: the providers, the engine interfaces and adapters, the YouTube dispatcher.</summary>
    private static readonly Regex EngineName = new(
        @"\b(IMusicProvider|IMusicRemoteProvider|IMusicProviderPlayNow|IMusicProviderStartTrack|SpotifyMusicProvider|YouTubeMusicProvider|SpotifyPlaybackEngine|YouTubePlaybackEngine|IYouTubePlayerDispatcher|YouTubePlayerDispatcher|IPlaybackEngine\w*)\b",
        RegexOptions.Compiled
    );

    /// <summary>A playback control call: a member call named like a transport action.</summary>
    private static readonly Regex ControlCall = new(
        @"\.(Play|Pause|Resume|Skip|Previous|Stop|Seek|SetVolume|Start|PlayNow|StartTrack|ReplayCurrent)\w*Async\s*\(",
        RegexOptions.Compiled
    );

    private static readonly Regex BlockComment = new(
        @"/\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    private static readonly Regex LineComment = new(@"(?m)//.*$", RegexOptions.Compiled);

    [Fact]
    public void No_control_surface_names_an_engine_at_a_music_control_call()
    {
        List<string> offenders = FindOffenders();

        offenders
            .Should()
            .BeEmpty(
                "a control surface sends a director input (IPlaybackInputSink); it never names an engine "
                    + "at play, pause, skip, seek or volume. Route the call through the director"
            );
    }

    [Fact]
    public void The_scan_detects_an_engine_named_at_a_control_call()
    {
        IsOffence("await _provider.PauseAsync(id, ct); // IMusicProvider _provider")
            .Should()
            .BeFalse("a comment is not code");
        IsOffence("private readonly IMusicProvider _p; ... await _p.SkipAsync(id, ct);")
            .Should()
            .BeTrue();
        IsOffence("IYouTubePlayerDispatcher players; await players.ReplayCurrentAsync(id, ct);")
            .Should()
            .BeTrue();
        IsOffence("IMusicService music; await music.SkipAsync(id, by, null, ct);")
            .Should()
            .BeFalse("IMusicService is not an engine");
        IsOffence("IMusicProviderManageApi api; await api.StopAsync(ct);")
            .Should()
            .BeFalse("the provider manage API is an account action, not an engine");
    }

    private static bool IsOffence(string source)
    {
        string code = LineComment.Replace(BlockComment.Replace(source, string.Empty), string.Empty);

        return EngineName.IsMatch(code) && ControlCall.IsMatch(code);
    }

    private static List<string> FindOffenders()
    {
        string sourceRoot = ServerSourceRoot();

        return
        [
            .. SurfaceRoots
                .Select(root => Path.Combine(sourceRoot, root))
                .SelectMany(root =>
                    Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                )
                .Where(file => !IsBuildOutput(file))
                .Where(file => IsOffence(File.ReadAllText(file)))
                .Select(file => Path.GetRelativePath(sourceRoot, file).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal),
        ];
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    /// <summary>Walks up from the test binary to the <c>server/src</c> folder.</summary>
    private static string ServerSourceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;

        directory
            .Should()
            .NotBeNull("the test must be able to find the server source tree to scan it");

        return Path.Combine(directory!.FullName, "src");
    }
}
