// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Identity.Providers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the alejo.io pronoun client: it parses the id-keyed v1 payload into pronoun records carrying the
/// API's subject/object/singular, drops entries missing a field, and — best-effort — returns null on a
/// non-success status or a transport failure rather than throwing (so the seeder falls back to its bundle).
/// </summary>
public sealed class AlejoPronounClientTests
{
    // A trimmed but representative slice of GET /v1/pronouns: a two-part pronoun and a singular singleton.
    private const string Payload = """
        {
          "theythem": { "name": "theythem", "subject": "They", "object": "Them", "singular": false },
          "any": { "name": "any", "subject": "Any", "object": "Any", "singular": true }
        }
        """;

    private static AlejoPronounClient Client(StubHandler handler)
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AlejoHttpClient.Name).Returns(_ => new HttpClient(handler));
        return new AlejoPronounClient(factory, NullLogger<AlejoPronounClient>.Instance);
    }

    // ── Mapping (the Parse contract, exercised through the real HTTP path) ────

    [Fact]
    public async Task Parses_the_keyed_payload_into_records()
    {
        IReadOnlyList<PronounRecord>? records = await Client(
                new StubHandler(HttpStatusCode.OK, Payload)
            )
            .FetchAsync();

        records.Should().NotBeNull();
        records!.Should().HaveCount(2);

        // The two-part pronoun keeps the API's display casing on the record (the seeder lowercases/derives Name).
        PronounRecord theyThem = records.Single(r => r.Subject == "They");
        theyThem.Object.Should().Be("Them");
        theyThem.Singular.Should().BeFalse();

        // The singleton — subject == object — is preserved as-is; the seeder collapses it to "any".
        PronounRecord any = records.Single(r => r.Subject == "Any");
        any.Object.Should().Be("Any");
        any.Singular.Should().BeTrue();
    }

    [Fact]
    public void Parse_drops_entries_missing_a_subject_or_object()
    {
        const string partial = """
            {
              "good": { "subject": "He", "object": "Him", "singular": true },
              "bad":  { "subject": "", "object": "Them", "singular": false }
            }
            """;

        IReadOnlyList<PronounRecord> records = AlejoPronounClient.Parse(partial);

        records.Should().ContainSingle();
        records[0].Subject.Should().Be("He");
    }

    // ── Best-effort failure: null, never a throw ─────────────────────────────

    [Fact]
    public async Task Returns_null_on_a_non_success_status()
    {
        IReadOnlyList<PronounRecord>? records = await Client(
                new StubHandler(HttpStatusCode.ServiceUnavailable, "nope")
            )
            .FetchAsync();

        records.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_and_does_not_throw_on_a_transport_failure()
    {
        AlejoPronounClient client = Client(StubHandler.Throwing());

        Func<Task> act = () => client.FetchAsync();

        await act.Should().NotThrowAsync();
        (await client.FetchAsync()).Should().BeNull();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string? _body;
        private readonly bool _throws;

        private StubHandler(HttpStatusCode status, string? body, bool throws)
        {
            _status = status;
            _body = body;
            _throws = throws;
        }

        public StubHandler(HttpStatusCode status, string body)
            : this(status, body, throws: false) { }

        public static StubHandler Throwing() => new(HttpStatusCode.OK, body: null, throws: true);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (_throws)
                throw new HttpRequestException("connection refused");

            return Task.FromResult(
                new HttpResponseMessage(_status)
                {
                    Content = new StringContent(
                        _body ?? string.Empty,
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
