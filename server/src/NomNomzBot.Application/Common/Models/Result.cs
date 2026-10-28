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

namespace NomNomzBot.Application.Common.Models;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? ErrorMessage { get; }
    public string? ErrorDetail { get; }
    public string? ErrorCode { get; }

    /// <summary>
    /// Typed, structured facts about the failure (a record the producer and its caller agree on) — for callers
    /// that phrase their own reply from data instead of parsing <see cref="ErrorMessage"/>. Null when the
    /// failure carries none.
    /// </summary>
    public object? ErrorData { get; }

    protected Result(
        bool isSuccess,
        string? errorMessage,
        string? errorDetail,
        string? errorCode,
        object? errorData = null
    )
    {
        ErrorData = errorData;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        ErrorDetail = errorDetail;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null, null);

    public static Result Failure(
        string? errorMessage,
        string? errorCode = null,
        string? errorDetail = null,
        object? errorData = null
    ) => new(false, errorMessage, errorDetail, errorCode, errorData);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(
        string? errorMessage,
        string? errorCode = null,
        string? errorDetail = null,
        object? errorData = null
    ) => Result<T>.Failure(errorMessage, errorCode, errorDetail, errorData);

    public Result<T> WithValue<T>(T value) =>
        IsSuccess
            ? Result<T>.Success(value)
            : Result<T>.Failure(ErrorMessage!, ErrorCode, ErrorDetail, ErrorData);
}

public class Result<T> : Result
{
    private readonly T? _value;

    public T Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException(
                $"Cannot access Value on a failed Result. Error: {ErrorMessage}"
            );

    private Result(
        bool isSuccess,
        T? value,
        string? errorMessage,
        string? errorDetail,
        string? errorCode,
        object? errorData = null
    )
        : base(isSuccess, errorMessage, errorDetail, errorCode, errorData)
    {
        _value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null, null, null);

    public static new Result<T> Failure(
        string? errorMessage,
        string? errorCode = null,
        string? errorDetail = null,
        object? errorData = null
    ) => new(false, default, errorMessage, errorDetail, errorCode, errorData);
}
