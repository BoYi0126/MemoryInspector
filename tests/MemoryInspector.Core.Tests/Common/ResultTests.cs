using MemoryInspector.Common;

namespace MemoryInspector.Core.Tests.Common;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void SuccessHasNoError()
    {
        var result = Result.Success();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.IsFailure);
        Assert.AreEqual(Error.None, result.Error);
    }

    [TestMethod]
    public void FailureContainsError()
    {
        var error = new Error(ErrorCode.NotFound, "The requested item was not found.");
        var result = Result.Failure(error);

        Assert.IsTrue(result.IsFailure);
        Assert.AreSame(error, result.Error);
    }

    [TestMethod]
    public void GenericSuccessExposesValue()
    {
        var result = Result<int>.Success(42);

        Assert.AreEqual(42, result.Value);
        Assert.IsTrue(result.TryGetValue(out var value));
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public void GenericFailureRejectsValueAccess()
    {
        var result = Result<int>.Failure(
            new Error(ErrorCode.InvalidState, "The operation is not ready."));

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.Value);
        Assert.IsFalse(result.TryGetValue(out _));
    }

    [TestMethod]
    public void MatchSelectsTheCorrectBranch()
    {
        var success = Result<int>.Success(10);
        var failure = Result<int>.Failure(
            new Error(ErrorCode.Validation, "Invalid value."));

        Assert.AreEqual("10", success.Match(value => value.ToString(), _ => "failure"));
        Assert.AreEqual(
            "Invalid value.",
            failure.Match(_ => "success", error => error.Message));
    }
}
