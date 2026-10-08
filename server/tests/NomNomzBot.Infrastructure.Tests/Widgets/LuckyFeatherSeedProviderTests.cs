// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Marketplace.FirstPartyBundles;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the Lucky Feather card survives an overlay reload: the holder the steal script stored is replayed as the
/// <c>steal</c> frame the widget already listens to, with the stored fields intact, only for the channel that owns
/// the holder, and never invented when no holder is stored.
/// </summary>
public sealed class LuckyFeatherSeedProviderTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000f2"
    );
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private const string StoredHolder =
        """{"id":"42","displayName":"Ann","avatarUrl":"https://cdn.example/ann.png","paint":{"backgroundImage":"url(x)","color":null,"textShadow":"0 0 2px #000","isImageOnly":true}}""";

    private readonly FakeStorage _storage = new();

    private Task<IReadOnlyList<WidgetSeedFrame>> Seed(Guid channel) =>
        new LuckyFeatherSeedProvider(_storage).SeedAsync(
            channel,
            new() { BroadcasterId = channel },
            CancellationToken.None
        );

    [Fact]
    public async Task Stored_holder_seeds_one_steal_frame_with_the_stored_fields()
    {
        await _storage.SetAsync(Broadcaster, LuckyFeatherBundle.HolderStorageKey, StoredHolder);

        IReadOnlyList<WidgetSeedFrame> frames = await Seed(Broadcaster);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("steal");
        JsonElement data = JsonSerializer.SerializeToElement(frames[0].Data, Wire);
        data.GetProperty("previousHolder").ValueKind.Should().Be(JsonValueKind.Null);
        JsonElement holder = data.GetProperty("newHolder");
        holder.GetProperty("id").GetString().Should().Be("42");
        holder.GetProperty("displayName").GetString().Should().Be("Ann");
        holder.GetProperty("avatarUrl").GetString().Should().Be("https://cdn.example/ann.png");
        JsonElement paint = holder.GetProperty("paint");
        paint.GetProperty("backgroundImage").GetString().Should().Be("url(x)");
        paint.GetProperty("textShadow").GetString().Should().Be("0 0 2px #000");
        paint.GetProperty("isImageOnly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task No_stored_holder_seeds_nothing()
    {
        (await Seed(Broadcaster)).Should().BeEmpty();
    }

    [Fact]
    public async Task Another_channels_holder_never_leaks()
    {
        await _storage.SetAsync(
            OtherBroadcaster,
            LuckyFeatherBundle.HolderStorageKey,
            StoredHolder
        );

        (await Seed(Broadcaster)).Should().BeEmpty();
        (await Seed(OtherBroadcaster)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"displayName":"No id"}""")]
    [InlineData("null")]
    public async Task Unreadable_stored_value_seeds_nothing(string stored)
    {
        await _storage.SetAsync(Broadcaster, LuckyFeatherBundle.HolderStorageKey, stored);

        (await Seed(Broadcaster)).Should().BeEmpty();
    }

    private sealed class FakeStorage : IScriptStorageService
    {
        private readonly Dictionary<(Guid, string), string> _rows = [];

        public Task<string?> GetAsync(
            Guid broadcasterId,
            string key,
            CancellationToken ct = default
        ) => Task.FromResult(_rows.GetValueOrDefault((broadcasterId, key)));

        public Task<Result> SetAsync(
            Guid broadcasterId,
            string key,
            string value,
            CancellationToken ct = default
        )
        {
            _rows[(broadcasterId, key)] = value;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(
            Guid broadcasterId,
            string key,
            CancellationToken ct = default
        )
        {
            _rows.Remove((broadcasterId, key));
            return Task.FromResult(Result.Success());
        }

        public Task<IReadOnlyList<string>> ListAsync(
            Guid broadcasterId,
            string? prefix = null,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<string>>([
                .. _rows.Keys.Where(k => k.Item1 == broadcasterId).Select(k => k.Item2),
            ]);
    }
}
