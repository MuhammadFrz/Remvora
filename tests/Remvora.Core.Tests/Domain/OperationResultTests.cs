using FluentAssertions;
using Remvora.Core.Domain.Results;

namespace Remvora.Core.Tests.Domain;

public sealed class OperationResultTests
{
    [Fact]
    public void SuccessResult_HasCorrectState()
    {
        var result = OperationResult.Success("test-value");

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be("test-value");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FailureResult_ContainsStructuredErrorCode()
    {
        var result = OperationResult.Failure<string>(
            ErrorCode.AccessDenied,
            "Access to file denied",
            @"C:\Protected\file.txt",
            "Win32 error 5");

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Value.Should().BeNull();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(ErrorCode.AccessDenied);
        result.Error.Message.Should().Be("Access to file denied");
        result.Error.Target.Should().Be(@"C:\Protected\file.txt");
        result.Error.Details.Should().Be("Win32 error 5");
    }

    [Fact]
    public void NonGenericResult_HandlesSuccessAndFailure()
    {
        var ok = OperationResult.Success();
        ok.IsSuccess.Should().BeTrue();
        ok.Error.Should().BeNull();

        var fail = OperationResult.Failure(ErrorCode.Timeout, "Process timed out");
        fail.IsSuccess.Should().BeFalse();
        fail.Error!.Code.Should().Be(ErrorCode.Timeout);
    }
}
