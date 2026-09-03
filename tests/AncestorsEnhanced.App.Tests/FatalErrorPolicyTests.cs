using AncestorsEnhanced.App;

namespace AncestorsEnhanced.App.Tests;

public sealed class FatalErrorPolicyTests
{
    [Theory]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(System.Net.Sockets.SocketException))]
    [InlineData(typeof(System.Net.WebException))]
    [InlineData(typeof(System.Net.Http.HttpRequestException))]
    public void TransientFailuresAreRecoverable(Type exceptionType)
    {
        Exception exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.True(FatalErrorPolicy.IsRecoverable(exception));
    }

    [Fact]
    public void OperationCanceledExceptionsWrappedInAggregateAreRecoverable()
    {
        Assert.True(FatalErrorPolicy.IsRecoverable(new OperationCanceledException()));
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(ArgumentException))]
    public void UnknownFailuresAreFatal(Type exceptionType)
    {
        Exception exception = exceptionType == typeof(ArgumentException)
            ? new ArgumentException("test")
            : (Exception)Activator.CreateInstance(exceptionType)!;

        // AEC writes game files: an unknown UI exception must shut the tool down in a
        // controlled way instead of being swallowed as "handled".
        Assert.False(FatalErrorPolicy.IsRecoverable(exception));
    }

    [Fact]
    public void InnerTransientExceptionDoesNotMakeAnUnknownFailureRecoverable()
    {
        var wrapped = new InvalidOperationException("state broken", new OperationCanceledException());

        Assert.False(FatalErrorPolicy.IsRecoverable(wrapped));
    }
}
