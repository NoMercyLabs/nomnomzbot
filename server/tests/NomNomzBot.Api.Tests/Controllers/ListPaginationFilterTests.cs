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

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.Pagination;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves every list endpoint lets the client choose its own page: <c>?take=</c> and <c>?page=</c> slice the list
/// the endpoint returns, the totals travel in headers, and an endpoint that already pages itself is left alone.
/// A client that sends neither gets the list exactly as before.
/// </summary>
public sealed class ListPaginationFilterTests
{
    private sealed record Item(int N);

    private static List<Item> Items(int count) =>
        [.. Enumerable.Range(1, count).Select(n => new Item(n))];

    private static (ResultExecutingContext Context, HttpContext Http) Run(
        object? payload,
        string query,
        ActionDescriptor? action = null
    )
    {
        DefaultHttpContext http = new();
        http.Request.Method = "GET";
        http.Request.QueryString = new(query);

        ActionContext actionContext = new(
            http,
            new RouteData(),
            action ?? new ControllerActionDescriptor()
        );
        ResultExecutingContext context = new(
            actionContext,
            [],
            new OkObjectResult(payload),
            controller: new object()
        );

        new ListPaginationFilter()
            .OnResultExecutionAsync(
                context,
                () =>
                    Task.FromResult(
                        new ResultExecutedContext(actionContext, [], context.Result, new object())
                    )
            )
            .GetAwaiter()
            .GetResult();

        return (context, http);
    }

    private static List<Item> Sent(ResultExecutingContext context) =>
        ((StatusResponseDto<List<Item>>)((ObjectResult)context.Result).Value!).Data!;

    [Fact]
    public void Take_and_page_slice_the_list_the_endpoint_returned()
    {
        (ResultExecutingContext context, _) = Run(
            new StatusResponseDto<List<Item>> { Data = Items(25) },
            "?take=10&page=2"
        );

        Sent(context).Select(i => i.N).Should().Equal(Enumerable.Range(11, 10));
    }

    [Fact]
    public void The_total_and_the_page_travel_in_headers_so_the_client_knows_when_to_stop()
    {
        (_, HttpContext http) = Run(
            new StatusResponseDto<List<Item>> { Data = Items(25) },
            "?take=10&page=3"
        );

        http.Response.Headers["X-Total-Count"].ToString().Should().Be("25");
        http.Response.Headers["X-Page"].ToString().Should().Be("3");
        http.Response.Headers["X-Take"].ToString().Should().Be("10");
    }

    [Fact]
    public void A_page_past_the_end_is_an_empty_list_not_an_error()
    {
        (ResultExecutingContext context, _) = Run(
            new StatusResponseDto<List<Item>> { Data = Items(5) },
            "?take=10&page=4"
        );

        Sent(context).Should().BeEmpty();
    }

    [Fact]
    public void Take_alone_means_the_first_page()
    {
        (ResultExecutingContext context, _) = Run(
            new StatusResponseDto<List<Item>> { Data = Items(25) },
            "?take=3"
        );

        Sent(context).Select(i => i.N).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void A_take_beyond_the_ceiling_is_clamped_not_refused()
    {
        (_, HttpContext http) = Run(
            new StatusResponseDto<List<Item>> { Data = Items(5) },
            "?take=999999"
        );

        http.Response.Headers["X-Take"]
            .ToString()
            .Should()
            .Be(ListPaginationFilter.MaxTake.ToString());
    }

    [Fact]
    public void Without_take_or_page_the_list_is_untouched_and_no_headers_are_added()
    {
        (ResultExecutingContext context, HttpContext http) = Run(
            new StatusResponseDto<List<Item>> { Data = Items(25) },
            ""
        );

        Sent(context).Should().HaveCount(25);
        http.Response.Headers.ContainsKey("X-Total-Count").Should().BeFalse();
    }

    [Fact]
    public void A_response_that_is_not_a_list_is_left_alone()
    {
        (ResultExecutingContext context, HttpContext http) = Run(
            new StatusResponseDto<Item> { Data = new(1) },
            "?take=1"
        );

        ((StatusResponseDto<Item>)((ObjectResult)context.Result).Value!)
            .Data.Should()
            .Be(new Item(1));
        http.Response.Headers.ContainsKey("X-Total-Count").Should().BeFalse();
    }

    [Fact]
    public void An_endpoint_that_pages_itself_keeps_its_own_paging()
    {
        ControllerActionDescriptor own = new()
        {
            Parameters = [new ParameterDescriptor { Name = "take", ParameterType = typeof(int?) }],
        };

        (ResultExecutingContext context, HttpContext http) = Run(
            new StatusResponseDto<List<Item>> { Data = Items(25) },
            "?take=3",
            own
        );

        Sent(context).Should().HaveCount(25);
        http.Response.Headers.ContainsKey("X-Total-Count").Should().BeFalse();
    }

    [Theory]
    [InlineData("?take=abc")]
    [InlineData("?take=0")]
    [InlineData("?take=-4&page=0")]
    public void A_nonsense_take_or_page_falls_back_to_a_safe_value(string query)
    {
        (ResultExecutingContext context, _) = Run(
            new StatusResponseDto<List<Item>> { Data = Items(30) },
            query
        );

        Sent(context).Should().NotBeEmpty();
        Sent(context).First().N.Should().Be(1);
    }
}
