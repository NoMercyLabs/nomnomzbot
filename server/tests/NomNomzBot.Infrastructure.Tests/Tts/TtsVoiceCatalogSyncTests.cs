// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the voice-catalogue sync (tts.md §7, decision 7): a provider's live voices land in the TtsVoice table
/// with their rich metadata (accent/age/styles/tags/description/preview url) intact; a second run updates the
/// existing rows rather than duplicating them; a provider that returns nothing never wipes what the seed / an
/// earlier run wrote; and the seed's IsDefault flag survives a metadata refresh.
/// </summary>
public sealed class TtsVoiceCatalogSyncTests
{
    // A test-only ITtsProvider whose GetVoicesAsync returns a fixed list — the sync only ever calls that method.
    private sealed class FakeProvider : ITtsProvider
    {
        private readonly IReadOnlyList<TtsVoiceInfo> _voices;

        public FakeProvider(params TtsVoiceInfo[] voices) => _voices = voices;

        public Task<TtsSynthesisResult> SynthesizeAsync(
            string text,
            string voiceId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("The catalogue sync never synthesizes.");

        public Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_voices);
    }

    private static TtsVoiceCatalogSync Build(
        TtsTestDbContext db,
        params ITtsProvider[] providers
    ) => new(providers, db, NullLogger<TtsVoiceCatalogSync>.Instance);

    private static TtsVoiceInfo Rachel() =>
        new()
        {
            Id = "el-rachel",
            Name = "Rachel",
            DisplayName = "Rachel",
            Locale = "en-US",
            Gender = "female",
            Provider = "elevenlabs",
            Accent = "American",
            Age = "young",
            Styles = ["calm", "narration"],
            Tags = ["audiobook"],
            Description = "Warm conversational narrator",
            PreviewUrl = "https://cdn.elevenlabs/rachel.mp3",
        };

    [Fact]
    public async Task Sync_persists_provider_voices_with_their_metadata()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        TtsVoiceCatalogSync sut = Build(
            db,
            new FakeProvider(
                Rachel(),
                new TtsVoiceInfo
                {
                    Id = "el-adam",
                    Name = "Adam",
                    DisplayName = "Adam",
                    Locale = "en-US",
                    Gender = "male",
                    Provider = "elevenlabs",
                }
            )
        );

        await sut.SyncAsync();

        List<TtsVoice> rows = await db.TtsVoices.ToListAsync();
        rows.Should().HaveCount(2);

        TtsVoice rachel = rows.Single(v => v.Id == "el-rachel");
        rachel.Provider.Should().Be("elevenlabs");
        rachel.Accent.Should().Be("American");
        rachel.Age.Should().Be("young");
        rachel.Description.Should().Be("Warm conversational narrator");
        rachel.PreviewUrl.Should().Be("https://cdn.elevenlabs/rachel.mp3");
        // Styles and tags are stored as JSON arrays (the shape TtsConfigService.ParseList reads back).
        rachel.StylesJson.Should().Be("[\"calm\",\"narration\"]");
        rachel.TagsJson.Should().Be("[\"audiobook\"]");

        // A voice with no metadata leaves those columns null rather than storing an empty array.
        TtsVoice adam = rows.Single(v => v.Id == "el-adam");
        adam.StylesJson.Should().BeNull();
        adam.TagsJson.Should().BeNull();
        adam.Accent.Should().BeNull();
    }

    [Fact]
    public async Task Re_running_updates_the_existing_row_instead_of_duplicating_it()
    {
        TtsTestDbContext db = TtsTestDbContext.New();

        await Build(db, new FakeProvider(Rachel())).SyncAsync();

        // The provider now reports fresh metadata for the same voice id.
        TtsVoiceInfo changed = new()
        {
            Id = "el-rachel",
            Name = "Rachel",
            DisplayName = "Rachel v2",
            Locale = "en-GB",
            Gender = "female",
            Provider = "elevenlabs",
            Accent = "British",
            Tags = ["gaming"],
        };
        await Build(db, new FakeProvider(changed)).SyncAsync();

        List<TtsVoice> rows = await db.TtsVoices.ToListAsync();
        rows.Should().ContainSingle("the second run upserts by id, it does not insert a duplicate");

        TtsVoice row = rows.Single();
        row.DisplayName.Should().Be("Rachel v2");
        row.Locale.Should().Be("en-GB");
        row.Accent.Should().Be("British");
        row.TagsJson.Should().Be("[\"gaming\"]");
        // Metadata absent on the second pass is cleared, not left stale from the first.
        row.StylesJson.Should().BeNull();
        row.Description.Should().BeNull();
        row.PreviewUrl.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_provider_never_wipes_existing_voices()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        // A pre-existing (seeded) Edge voice marked default.
        db.TtsVoices.Add(
            new TtsVoice
            {
                Id = "en-US-AriaNeural",
                Name = "AriaNeural",
                DisplayName = "Aria (US)",
                Locale = "en-US",
                Gender = "Female",
                Provider = "edge",
                IsDefault = true,
            }
        );
        await db.SaveChangesAsync();

        // Two providers: one keyless (empty), one that adds a new voice.
        await Build(
                db,
                new FakeProvider(), // empty — mimics an unconfigured Azure/ElevenLabs key
                new FakeProvider(Rachel())
            )
            .SyncAsync();

        List<TtsVoice> rows = await db.TtsVoices.ToListAsync();
        rows.Select(v => v.Id).Should().BeEquivalentTo(["en-US-AriaNeural", "el-rachel"]);

        // The seeded Edge voice is untouched — still present, still the default.
        TtsVoice aria = rows.Single(v => v.Id == "en-US-AriaNeural");
        aria.Provider.Should().Be("edge");
        aria.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Refreshing_a_seeded_voice_preserves_its_default_flag()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsVoices.Add(
            new TtsVoice
            {
                Id = "en-US-AriaNeural",
                Name = "AriaNeural",
                DisplayName = "Aria (US)",
                Locale = "en-US",
                Gender = "Female",
                Provider = "edge",
                IsDefault = true,
            }
        );
        await db.SaveChangesAsync();

        // The provider re-reports the same voice with enriched metadata (no IsDefault on the provider shape).
        await Build(
                db,
                new FakeProvider(
                    new TtsVoiceInfo
                    {
                        Id = "en-US-AriaNeural",
                        Name = "AriaNeural",
                        DisplayName = "Aria (US)",
                        Locale = "en-US",
                        Gender = "Female",
                        Provider = "edge",
                        Accent = "American",
                    }
                )
            )
            .SyncAsync();

        TtsVoice aria = await db.TtsVoices.SingleAsync(v => v.Id == "en-US-AriaNeural");
        aria.IsDefault.Should().BeTrue("the sync must not clear an operator/seed default");
        aria.Accent.Should().Be("American");
    }
}
