namespace Prosody.Tests.Unit;

/// <summary>
/// Tests for implementing the IProsodyHandler interface and error classification.
/// </summary>
public sealed class ProsodyHandlerTests
{
    /// <summary>
    /// Test implementation of IProsodyHandler that completes successfully.
    /// </summary>
    private sealed class SuccessHandler : IProsodyHandler
    {
        public int MessageCount { get; private set; }
        public int TimerCount { get; private set; }

        public Task OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            MessageCount++;
            return Task.CompletedTask;
        }

        public Task OnTimerAsync(Context context, Timer timer, CancellationToken cancellationToken)
        {
            TimerCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void CanImplementIProsodyHandler()
    {
        IProsodyHandler handler = new SuccessHandler();
        Assert.NotNull(handler);
    }

    /// <summary>
    /// Test handler that uses async/await properly.
    /// </summary>
    private sealed class AsyncHandler : IProsodyHandler
    {
        public TimeSpan Delay { get; init; } = TimeSpan.FromMilliseconds(10);

        public async Task OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Delay, cancellationToken);
        }

        public async Task OnTimerAsync(
            Context context,
            Timer timer,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(Delay, cancellationToken);
        }
    }

    [Fact]
    public void CanCreateAsyncImplementation()
    {
        IProsodyHandler handler = new AsyncHandler();
        Assert.NotNull(handler);
    }

    #region PermanentException Tests

    [Fact]
    public void PermanentExceptionImplementsIPermanentError()
    {
        var ex = new PermanentException("test");
        Assert.IsAssignableFrom<IPermanentError>(ex);
    }

    [Fact]
    public void PermanentExceptionPreservesMessage()
    {
        const string message = "Something went wrong";
        var ex = new PermanentException(message);
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void PermanentExceptionPreservesInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new PermanentException("outer", inner);

        Assert.Multiple(
            () => Assert.Equal("outer", ex.Message),
            () => Assert.Same(inner, ex.InnerException)
        );
    }

    #endregion

    #region PermanentErrorAttribute Tests

    [Fact]
    public void PermanentErrorAttributeMatchesExactType()
    {
        var attr = new PermanentErrorAttribute(typeof(InvalidOperationException));

        Assert.Multiple(
            () => Assert.True(attr.IsMatch(new InvalidOperationException())),
            () => Assert.False(attr.IsMatch(new ArgumentException()))
        );
    }

    [Fact]
    public void PermanentErrorAttributeMatchesSubtypes()
    {
        // ArgumentNullException derives from ArgumentException
        var attr = new PermanentErrorAttribute(typeof(ArgumentException));

        Assert.Multiple(
            () => Assert.True(attr.IsMatch(new ArgumentException())),
            () => Assert.True(attr.IsMatch(new ArgumentNullException())),
            () => Assert.True(attr.IsMatch(new ArgumentOutOfRangeException())),
            () => Assert.False(attr.IsMatch(new InvalidOperationException()))
        );
    }

    [Fact]
    public void PermanentErrorAttributeMatchesMultipleTypes()
    {
        var attr = new PermanentErrorAttribute(
            typeof(ArgumentException),
            typeof(InvalidOperationException)
        );

        Assert.Multiple(
            () => Assert.True(attr.IsMatch(new ArgumentException())),
            () => Assert.True(attr.IsMatch(new InvalidOperationException())),
            () => Assert.True(attr.IsMatch(new ArgumentNullException())), // Subtype
            () => Assert.False(attr.IsMatch(new NotSupportedException()))
        );
    }

    [Fact]
    public void PermanentErrorAttributeWithEmptyTypesMatchesNothing()
    {
        var attr = new PermanentErrorAttribute();

        Assert.Multiple(
            () => Assert.False(attr.IsMatch(new InvalidOperationException())),
            () => Assert.False(attr.IsMatch(new ArgumentException()))
        );
    }

    [Fact]
    public void PermanentErrorAttributeThrowsOnNullTypes()
    {
        Assert.Throws<ArgumentNullException>(() => new PermanentErrorAttribute(null!));
    }

    [Fact]
    public void PermanentErrorAttributeThrowsOnNonExceptionType()
    {
        Assert.Throws<ArgumentException>(() => new PermanentErrorAttribute(typeof(string)));
    }

    #endregion

    #region Handler with Attribute Tests

    /// <summary>
    /// Handler that uses PermanentErrorAttribute to declare permanent exceptions.
    /// </summary>
    private sealed class AttributeHandler : IProsodyHandler
    {
        [PermanentError(typeof(FormatException), typeof(ArgumentException))]
        public Task OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            return Task.CompletedTask;
        }

        public Task OnTimerAsync(Context context, Timer timer, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void CanApplyPermanentErrorAttribute()
    {
        var handler = new AttributeHandler();
        var method = handler.GetType().GetMethod(nameof(IProsodyHandler.OnMessageAsync));
        var attr = method
            ?.GetCustomAttributes(typeof(PermanentErrorAttribute), true)
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.IsType<PermanentErrorAttribute>(attr);
    }

    #endregion

    #region Custom Permanent Exception Tests

    /// <summary>
    /// Custom domain exception that implements IPermanentError.
    /// </summary>
    private sealed class OrderValidationException : Exception, IPermanentError
    {
        public OrderValidationException() { }

        public OrderValidationException(string message)
            : base(message) { }

        public OrderValidationException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    [Fact]
    public void CustomExceptionCanImplementIPermanentError()
    {
        var ex = new OrderValidationException("Invalid order");

        Assert.Multiple(
            () => Assert.IsAssignableFrom<IPermanentError>(ex),
            () => Assert.IsAssignableFrom<Exception>(ex),
            () => Assert.Equal("Invalid order", ex.Message)
        );
    }

    /// <summary>
    /// Handler that throws custom IPermanentError exception.
    /// </summary>
    private sealed class CustomExceptionHandler : IProsodyHandler
    {
        public Task OnMessageAsync(
            Context context,
            Message message,
            CancellationToken cancellationToken
        )
        {
            throw new OrderValidationException("Order is invalid");
        }

        public Task OnTimerAsync(Context context, Timer timer, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task HandlerCanThrowCustomPermanentException()
    {
        var handler = new CustomExceptionHandler();

        var ex = await Assert.ThrowsAsync<OrderValidationException>(
            () => handler.OnMessageAsync(null!, null!, CancellationToken.None)
        );

        Assert.IsAssignableFrom<IPermanentError>(ex);
    }

    #endregion
}
