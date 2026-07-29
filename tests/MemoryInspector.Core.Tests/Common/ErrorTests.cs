using MemoryInspector.Common;

namespace MemoryInspector.Core.Tests.Common;

[TestClass]
public sealed class ErrorTests
{
    [TestMethod]
    public void ErrorChainPreservesCauseAndDisplayMessages()
    {
        var inner = new Error(ErrorCode.Io, "Snapshot could not be read.");
        var outer = new Error(ErrorCode.Unexpected, "Scan could not be restored.")
            .WithCause(inner);

        CollectionAssert.AreEqual(
            new[] { outer, inner },
            outer.EnumerateChain().ToArray());
        Assert.AreEqual(
            "Scan could not be restored. → Snapshot could not be read.",
            outer.ToDisplayMessage());
    }

    [TestMethod]
    public void ErrorPreservesOriginalExceptionForLogging()
    {
        var exception = new IOException("Diagnostic detail.");
        var error = new Error(
            ErrorCode.Io,
            "The file operation failed.",
            exception);

        Assert.AreSame(exception, error.Exception);
        Assert.AreEqual("The file operation failed.", error.ToDisplayMessage());
    }

    [TestMethod]
    public void FailureErrorRequiresReadableMessage()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => _ = new Error(ErrorCode.Validation, " "));
    }

    [TestMethod]
    public void NoneCannotContainFailureDetails()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => _ = new Error(ErrorCode.None, "Not actually empty."));
    }
}
