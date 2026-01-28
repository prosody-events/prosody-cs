using Prosody;
using Prosody.Native;
using Xunit;

namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for Prosody exception types and FFI error code mapping.
/// </summary>
public class ExceptionTests
{
    [Fact]
    public void ProsodyException_CanBeCreatedWithMessage()
    {
        var ex = new ProsodyException("test message");

        Assert.Equal("test message", ex.Message);
    }

    [Fact]
    public void ProsodyException_CanBeCreatedWithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new ProsodyException("test message", inner);

        Assert.Equal("test message", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ProsodyConnectionException_InheritsFromProsodyException()
    {
        var ex = new ProsodyConnectionException("connection failed");

        Assert.IsAssignableFrom<ProsodyException>(ex);
    }

    [Fact]
    public void TransientException_IsPermanentReturnsFalse()
    {
        var ex = new TransientException("transient error");

        Assert.False(ex.IsPermanent);
    }

    [Fact]
    public void TransientException_InheritsFromEventHandlerException()
    {
        var ex = new TransientException("transient error");

        Assert.IsAssignableFrom<EventHandlerException>(ex);
    }

    [Fact]
    public void PermanentException_IsPermanentReturnsTrue()
    {
        var ex = new PermanentException("permanent error");

        Assert.True(ex.IsPermanent);
    }

    [Fact]
    public void PermanentException_InheritsFromEventHandlerException()
    {
        var ex = new PermanentException("permanent error");

        Assert.IsAssignableFrom<EventHandlerException>(ex);
    }

    [Fact]
    public void TransientException_CanBeCreatedWithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new TransientException("transient", inner);

        Assert.Same(inner, ex.InnerException);
        Assert.False(ex.IsPermanent);
    }

    [Fact]
    public void PermanentException_CanBeCreatedWithInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new PermanentException("permanent", inner);

        Assert.Same(inner, ex.InnerException);
        Assert.True(ex.IsPermanent);
    }

    [Fact]
    public void FFIErrorCode_Ok_IsOkReturnsTrue()
    {
        var errorCode = FFIErrorCode.Ok;

        Assert.True(errorCode.IsOk);
        Assert.False(errorCode.IsNullPassed);
        Assert.False(errorCode.IsPanic);
        Assert.False(errorCode.IsInvalidArgument);
        Assert.False(errorCode.IsConnectionFailed);
        Assert.False(errorCode.IsCancelled);
        Assert.False(errorCode.IsInternal);
        Assert.False(errorCode.IsInvalidContext);
        Assert.False(errorCode.IsAlreadySubscribed);
        Assert.False(errorCode.IsNotSubscribed);
    }

    [Fact]
    public void FFIErrorCode_NullPassed_IsNullPassedReturnsTrue()
    {
        var errorCode = FFIErrorCode.NullPassed;

        Assert.False(errorCode.IsOk);
        Assert.True(errorCode.IsNullPassed);
    }

    [Fact]
    public void FFIErrorCode_ConnectionFailed_IsConnectionFailedReturnsTrue()
    {
        var errorCode = FFIErrorCode.ConnectionFailed;

        Assert.False(errorCode.IsOk);
        Assert.True(errorCode.IsConnectionFailed);
    }

    [Fact]
    public void FFIErrorCode_Cancelled_IsCancelledReturnsTrue()
    {
        var errorCode = FFIErrorCode.Cancelled;

        Assert.False(errorCode.IsOk);
        Assert.True(errorCode.IsCancelled);
    }

    [Fact]
    public void FFIErrorCode_AlreadySubscribed_IsAlreadySubscribedReturnsTrue()
    {
        var errorCode = FFIErrorCode.AlreadySubscribed;

        Assert.False(errorCode.IsOk);
        Assert.True(errorCode.IsAlreadySubscribed);
    }

    [Fact]
    public void FFIErrorCode_NotSubscribed_IsNotSubscribedReturnsTrue()
    {
        var errorCode = FFIErrorCode.NotSubscribed;

        Assert.False(errorCode.IsOk);
        Assert.True(errorCode.IsNotSubscribed);
    }

    [Fact]
    public void FFIErrorCode_InvalidContext_IsInvalidContextReturnsTrue()
    {
        var errorCode = FFIErrorCode.InvalidContext;

        Assert.False(errorCode.IsOk);
        Assert.True(errorCode.IsInvalidContext);
    }

    [Fact]
    public void FFIErrorCode_ToString_ReturnsExpectedStrings()
    {
        Assert.Equal("Ok", FFIErrorCode.Ok.ToString());
        Assert.Equal("NullPassed", FFIErrorCode.NullPassed.ToString());
        Assert.Equal("Panic", FFIErrorCode.Panic.ToString());
        Assert.Equal("InvalidArgument", FFIErrorCode.InvalidArgument.ToString());
        Assert.Equal("ConnectionFailed", FFIErrorCode.ConnectionFailed.ToString());
        Assert.Equal("Cancelled", FFIErrorCode.Cancelled.ToString());
        Assert.Equal("Internal", FFIErrorCode.Internal.ToString());
        Assert.Equal("InvalidContext", FFIErrorCode.InvalidContext.ToString());
        Assert.Equal("AlreadySubscribed", FFIErrorCode.AlreadySubscribed.ToString());
        Assert.Equal("NotSubscribed", FFIErrorCode.NotSubscribed.ToString());
    }
}
