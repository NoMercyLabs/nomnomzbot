// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Common.Models;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? ErrorMessage { get; }
    public string? ErrorDetail { get; }
    public string? ErrorCode { get; }

    protected Result(bool isSuccess, string? errorMessage, string? errorDetail, string? errorCode)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        ErrorDetail = errorDetail;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null, null);

    public static Result Failure(
        string errorMessage,
        string? errorCode = null,
        string? errorDetail = null
    ) => new(false, errorMessage, errorDetail, errorCode);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(
        string errorMessage,
        string? errorCode = null,
        string? errorDetail = null
    ) => Result<T>.Failure(errorMessage, errorCode, errorDetail);

    public Result<T> WithValue<T>(T value) =>
        IsSuccess
            ? Result<T>.Success(value)
            : Result<T>.Failure(ErrorMessage!, ErrorCode, ErrorDetail);
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
        string? errorCode
    )
        : base(isSuccess, errorMessage, errorDetail, errorCode)
    {
        _value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null, null, null);

    public static new Result<T> Failure(
        string errorMessage,
        string? errorCode = null,
        string? errorDetail = null
    ) => new(false, default, errorMessage, errorDetail, errorCode);
}
