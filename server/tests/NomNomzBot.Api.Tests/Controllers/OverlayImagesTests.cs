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

using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NetVips;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Infrastructure.Overlays.Images;
using NvImage = NetVips.Image;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// GET /api/v1/overlay/images, run through the real controller, optimizer and disk cache with real image bytes
/// made in the test. The only fake is the network: a handler that counts every outbound request.
/// </summary>
public sealed class OverlayImagesTests : IDisposable
{
    private const string Cdn = "https://static-cdn.jtvnw.net/emoticons/v2/1/default/dark/3.0";

    private readonly string _cacheDir = Path.Combine(
        Path.GetTempPath(),
        "nnz-image-tests-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
            Directory.Delete(_cacheDir, recursive: true);
    }

    private static NvImage Solid(int width, int height, double[] rgb) =>
        NvImage.Black(width, height, 3).NewFromImage(rgb).Cast(Enums.BandFormat.Uchar);

    private static byte[] Png(int width, int height = 40) =>
        Solid(width, height, [200, 40, 40]).PngsaveBuffer();

    private static byte[] Gif(int frames, int size = 64)
    {
        NvImage[] pages = Enumerable
            .Range(0, frames)
            .Select(i => Solid(size, size, [60 * (i + 1), 255 - 70 * i, 30]))
            .ToArray();
        NvImage strip = NvImage
            .Arrayjoin(pages, across: 1)
            .Mutate(m => m.Set(GValue.GIntType, "page-height", size));
        return strip.GifsaveBuffer();
    }

    private static HttpResponseMessage Ok(byte[] body, string contentType = "image/png")
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }

    private (OverlayImagesController Controller, CountingHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? baseUrl = null,
        long cacheCapBytes = 512L * 1024 * 1024
    )
    {
        CountingHandler handler = new(respond);
        OverlayImageOptimizer optimizer = new(
            new FixedClientFactory(handler),
            new OverlayImageDiskCache(_cacheDir, cacheCapBytes),
            NullLogger<OverlayImageOptimizer>.Instance
        );
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = baseUrl })
            .Build();
        OverlayImagesController controller = new(optimizer, config)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, handler);
    }

    private static (int Width, int Frames) Inspect(PhysicalFileResult file)
    {
        byte[] bytes = File.ReadAllBytes(file.FileName);
        bytes.Take(4).Should().Equal("RIFF"u8.ToArray());
        bytes.Skip(8).Take(4).Should().Equal("WEBP"u8.ToArray());
        using NvImage image = NvImage.NewFromBuffer(bytes, "n=-1");
        int frames = image.Contains("n-pages") ? (int)image.Get("n-pages") : 1;
        return (image.Width, frames);
    }

    [Theory]
    [InlineData(1, 32)]
    [InlineData(33, 64)]
    [InlineData(100, 128)]
    [InlineData(200, 256)]
    public async Task A_width_snaps_up_to_its_bucket_and_the_output_is_webp_of_that_width(
        int requested,
        int bucket
    )
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(_ => Ok(Png(600)));

        IActionResult result = await controller.Get(Cdn, requested, CancellationToken.None);

        PhysicalFileResult file = result.Should().BeOfType<PhysicalFileResult>().Subject;
        file.ContentType.Should().Be("image/webp");
        Inspect(file).Width.Should().Be(bucket);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_width_over_1024_is_1024()
    {
        (OverlayImagesController controller, _) = Build(_ => Ok(Png(2000)));

        IActionResult result = await controller.Get(Cdn, 5000, CancellationToken.None);

        Inspect(result.Should().BeOfType<PhysicalFileResult>().Subject).Width.Should().Be(1024);
    }

    [Fact]
    public async Task A_source_narrower_than_its_bucket_is_never_upscaled()
    {
        (OverlayImagesController controller, _) = Build(_ => Ok(Png(100)));

        IActionResult result = await controller.Get(Cdn, 512, CancellationToken.None);

        Inspect(result.Should().BeOfType<PhysicalFileResult>().Subject).Width.Should().Be(100);
    }

    [Fact]
    public async Task A_three_frame_gif_comes_out_as_a_three_frame_webp()
    {
        byte[] gif = Gif(frames: 3, size: 64);
        NvImage.NewFromBuffer(gif, "n=-1").Get("n-pages").Should().Be(3);
        (OverlayImagesController controller, _) = Build(_ => Ok(gif, "image/gif"));

        IActionResult result = await controller.Get(Cdn, 32, CancellationToken.None);

        (int width, int frames) = Inspect(result.Should().BeOfType<PhysicalFileResult>().Subject);
        width.Should().Be(32);
        frames.Should().Be(3);
    }

    [Fact]
    public async Task The_response_is_cacheable_for_a_year_with_an_etag_and_a_matching_etag_gets_304()
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(_ => Ok(Png(300)));
        await controller.Get(Cdn, 64, CancellationToken.None);
        string etag = controller.Response.Headers.ETag.ToString();
        controller
            .Response.Headers.CacheControl.ToString()
            .Should()
            .Be("public, max-age=31536000, immutable");
        etag.Should().MatchRegex("^\"[0-9a-f]{32}\"$");

        controller.Request.Headers.IfNoneMatch = etag;
        IActionResult second = await controller.Get(Cdn, 64, CancellationToken.None);

        second.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(304);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task The_second_identical_call_is_a_cache_hit_with_no_outbound_request()
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(_ => Ok(Png(300)));

        await controller.Get(Cdn, 100, CancellationToken.None);
        handler.Calls.Should().Be(1);
        IActionResult again = await controller.Get(Cdn, 120, CancellationToken.None);

        again.Should().BeOfType<PhysicalFileResult>();
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Another_bucket_of_the_same_source_is_its_own_cache_entry()
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(_ => Ok(Png(600)));

        await controller.Get(Cdn, 64, CancellationToken.None);
        await controller.Get(Cdn, 256, CancellationToken.None);

        handler.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData("https://evil.example/a.png")]
    [InlineData("http://static-cdn.jtvnw.net/a.png")]
    [InlineData("https://user:pw@static-cdn.jtvnw.net/a.png")]
    [InlineData("https://static-cdn.jtvnw.net:8443/a.png")]
    [InlineData("https://static-cdn.jtvnw.net.evil.example/a.png")]
    [InlineData("https://cdn.jsdelivr.net/npm/other/a.png")]
    [InlineData("https://cdn.jsdelivr.net/gh/jdecked/twemoji/../../evil/a.png")]
    [InlineData("https://127.0.0.1/a.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task A_refused_source_is_a_400_and_makes_no_outbound_request(string src)
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(_ => Ok(Png(100)));

        IActionResult result = await controller.Get(src, 64, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("https://static-cdn.jtvnw.net/a.png")]
    [InlineData("https://cdn.7tv.app/emote/1/4x.webp")]
    [InlineData("https://cdn.betterttv.net/emote/1/3x.webp")]
    [InlineData("https://cdn.frankerfacez.com/emote/1/4")]
    [InlineData("https://cdn.jsdelivr.net/gh/jdecked/twemoji@latest/assets/svg/1f600.png")]
    [InlineData("https://i.scdn.co/image/abc")]
    [InlineData("https://i.ytimg.com/vi/a/hq.jpg")]
    [InlineData("https://cdn.discordapp.com/avatars/1/a.png")]
    [InlineData("https://media.tenor.com/a.gif")]
    [InlineData("https://media.giphy.com/media/a/giphy.gif")]
    [InlineData("https://media3.giphy.com/media/a/giphy.gif")]
    [InlineData("https://bot.example.org/api/v1/assets/a.png")]
    public async Task An_allowed_host_is_fetched_which_also_proves_the_counter_counts(string src)
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(
            _ => Ok(Png(100)),
            baseUrl: "https://bot.example.org"
        );

        IActionResult result = await controller.Get(src, 64, CancellationToken.None);

        result.Should().BeOfType<PhysicalFileResult>();
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_zero_or_negative_width_is_a_400_with_no_outbound_request()
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(_ => Ok(Png(100)));

        IActionResult result = await controller.Get(Cdn, 0, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_redirect_to_an_allowed_host_is_followed()
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(request =>
            request.RequestUri!.Host == "static-cdn.jtvnw.net"
                ? Redirect("https://cdn.7tv.app/final.png")
                : Ok(Png(100))
        );

        IActionResult result = await controller.Get(Cdn, 64, CancellationToken.None);

        result.Should().BeOfType<PhysicalFileResult>();
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task A_redirect_to_a_host_off_the_list_is_refused_and_not_requested()
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(_ =>
            Redirect("https://evil.example/a.png")
        );

        IActionResult result = await controller.Get(Cdn, 64, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task More_than_three_redirects_are_refused()
    {
        (OverlayImagesController controller, CountingHandler handler) = Build(_ =>
            Redirect("https://cdn.7tv.app/again.png")
        );

        IActionResult result = await controller.Get(Cdn, 64, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        handler.Calls.Should().Be(4);
    }

    [Fact]
    public async Task A_source_that_declares_more_than_10_MB_is_refused()
    {
        (OverlayImagesController controller, _) = Build(_ =>
        {
            HttpResponseMessage response = Ok(Png(100));
            response.Content.Headers.ContentLength = 11 * 1024 * 1024;
            return response;
        });

        IActionResult result = await controller.Get(Cdn, 64, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task A_source_that_streams_past_10_MB_is_refused_while_streaming()
    {
        EndlessStream body = new();
        (OverlayImagesController controller, _) = Build(_ => new HttpResponseMessage(
            HttpStatusCode.OK
        )
        {
            Content = new StreamContent(body),
        });

        IActionResult result = await controller.Get(Cdn, 64, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        body.BytesServed.Should().BeLessThan(10L * 1024 * 1024 + 1024 * 1024);
    }

    [Fact]
    public async Task An_svg_source_is_a_400()
    {
        byte[] svg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>"u8.ToArray();
        (OverlayImagesController controller, _) = Build(_ => Ok(svg, "image/svg+xml"));

        IActionResult result = await controller.Get(Cdn, 64, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Bytes_that_are_not_an_image_are_a_400()
    {
        (OverlayImagesController controller, _) = Build(_ =>
            Ok("<html>not an image</html>"u8.ToArray(), "text/html")
        );

        IActionResult result = await controller.Get(Cdn, 64, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task A_source_host_that_fails_is_a_502()
    {
        (OverlayImagesController controller, _) = Build(_ => new HttpResponseMessage(
            HttpStatusCode.NotFound
        ));

        IActionResult result = await controller.Get(Cdn, 64, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task The_cache_drops_its_oldest_file_first_when_it_passes_its_cap()
    {
        (OverlayImagesController probe, _) = Build(_ => Ok(Png(300)));
        PhysicalFileResult first = (PhysicalFileResult)
            await probe.Get(Cdn + "/1", 64, CancellationToken.None);
        long oneEntry = new FileInfo(first.FileName).Length;
        Directory.Delete(_cacheDir, recursive: true);

        (OverlayImagesController controller, CountingHandler handler) = Build(
            _ => Ok(Png(300)),
            cacheCapBytes: oneEntry * 2 + oneEntry / 2
        );
        PhysicalFileResult a = (PhysicalFileResult)
            await controller.Get(Cdn + "/a", 64, CancellationToken.None);
        File.SetLastWriteTimeUtc(a.FileName, DateTime.UtcNow.AddMinutes(-30));
        PhysicalFileResult b = (PhysicalFileResult)
            await controller.Get(Cdn + "/b", 64, CancellationToken.None);
        File.SetLastWriteTimeUtc(b.FileName, DateTime.UtcNow.AddMinutes(-20));
        PhysicalFileResult c = (PhysicalFileResult)
            await controller.Get(Cdn + "/c", 64, CancellationToken.None);

        File.Exists(a.FileName).Should().BeFalse();
        File.Exists(b.FileName).Should().BeTrue();
        File.Exists(c.FileName).Should().BeTrue();
        handler.Calls.Should().Be(3);
    }

    private static HttpResponseMessage Redirect(string to)
    {
        HttpResponseMessage response = new(HttpStatusCode.Found);
        response.Headers.Location = new Uri(to);
        return response;
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FixedClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class EndlessStream : Stream
    {
        public long BytesServed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => BytesServed;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)1, offset, count);
            BytesServed += count;
            return count;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
