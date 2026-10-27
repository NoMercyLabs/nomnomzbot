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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Lookups;

/// <summary>
/// The outside sources of catalog section 9 as fixed bodies (the lookup fixture of section 5). It answers by
/// host and query, records every request and never leaves the process, so no test calls a live service.
/// </summary>
internal sealed class StubLookupHandler : HttpMessageHandler
{
    private const string Amsterdam =
        """{"results":[{"name":"Amsterdam","latitude":52.37403,"longitude":4.88969,"timezone":"Europe/Amsterdam"}]}""";

    private const string Tokyo =
        """{"results":[{"name":"Tokyo","latitude":35.6895,"longitude":139.69171,"timezone":"Asia/Tokyo"}]}""";

    private const string AmsterdamForecast = """
        {"current":{"temperature_2m":12.2,"apparent_temperature":9.8,"relative_humidity_2m":81,
        "weather_code":61,"wind_speed_10m":18.4,"wind_direction_10m":225},
        "daily":{"weather_code":[61,80],"temperature_2m_max":[13.0,14.4],"temperature_2m_min":[8.0,9.2]}}
        """;

    private const string EurUsd =
        """{"date":"2026-10-26","base":"EUR","quote":"USD","rate":1.162}""";

    private readonly bool _hang;
    private readonly HttpStatusCode _status;

    public StubLookupHandler(bool hang = false, HttpStatusCode status = HttpStatusCode.OK)
    {
        _hang = hang;
        _status = status;
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CountFor(string host) => Requests.Count(r => r.RequestUri!.Host == host);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Add(request);
        if (_hang)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(
                BodyFor(request.RequestUri!),
                Encoding.UTF8,
                "application/json"
            ),
        };
    }

    private static string BodyFor(Uri uri)
    {
        string query = Uri.UnescapeDataString(uri.Query);
        return uri.Host switch
        {
            "geocoding-api.open-meteo.com" when query.Contains("name=Amsterdam") => Amsterdam,
            "geocoding-api.open-meteo.com" when query.Contains("name=Tokyo") => Tokyo,
            "geocoding-api.open-meteo.com" => "{}",
            "api.open-meteo.com" => AmsterdamForecast,
            "api.frankfurter.dev" when uri.AbsolutePath == "/v2/rate/eur/usd" => EurUsd,
            "api.mymemory.translated.net" => Translation(query),
            _ => "{}",
        };
    }

    private static string Translation(string query) =>
        query switch
        {
            _ when query.Contains("q=good morning&langpair=en|nl") => Reply("goedemorgen"),
            _ when query.Contains("q=guten Morgen&langpair=de|en") => Reply("good morning"),
            _ when query.Contains("q=hello world&langpair=en|nl") => Reply("hallo wereld"),
            _ => """{"responseData":{"translatedText":""},"responseStatus":403}""",
        };

    private static string Reply(string text) =>
        $$"""{"responseData":{"translatedText":"{{text}}"},"responseStatus":200}""";
}
