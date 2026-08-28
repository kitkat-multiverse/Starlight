using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Starlight.Common;

namespace Starlight.Rpc;

public delegate Task AsyncDataHandler(RpcMessage message);
public delegate Task AsyncMessageHandler<in T>(T msg, RpcMessage rpc) where T : IMessage;

/// <summary>
/// A remote-procedure-call (RPC) transport defines how the services
/// communicate with each other.
/// </summary>
public abstract class RpcTransport : IHostedService
{
    #region Transport

    /// <summary>
    /// Subscribes to a subject, registering a handler for messages relating to it.
    /// </summary>
    /// <returns>A subscription. Disposing this object will cancel the subscription, preventing the handler from receiving more messages.</returns>
    public abstract Task<IDisposable> Subscribe(string subject, AsyncDataHandler handler);

    /// <summary>
    /// Publishes a message to a subject. All handlers (across the network) will receive this message.
    /// </summary>
    public abstract Task Publish(string subject, RpcMessage message);

    #endregion

    /// <summary>
    /// Serializes the protobuf message into an <see cref="RpcMessage"/>.
    /// <br/>
    /// Used for abstraction/transport efficiency.
    /// </summary>
    protected virtual RpcMessage Serialize(IMessage message) => new(message.ToByteArray());

    /// <summary>
    /// Determines whether a subject currently has any responders (subscribers).
    /// <br/>
    /// Used by <see cref="Request{TRequest,TResponse}"/> to fail fast instead of
    /// waiting for a timeout. Transports that cannot cheaply determine this should
    /// return <c>true</c> so that requests fall back to the timeout behaviour.
    /// </summary>
    protected virtual bool HasResponders(string subject) => true;

    public virtual Task Publish(string subject, IMessage message)
        => Publish(subject, Serialize(message));

    /// <summary>
    /// Subscribes to a subject, registering a handler for messages relating to it.
    /// <br/>
    /// This method exclusively takes in a protobuf-type handler, requiring all received messages on
    /// the subject to deserialize to the type before invoking the handler with the data.
    /// </summary>
    /// <typeparam name="T">The protobuf type to deserialize messages as.</typeparam>
    /// <returns>A subscription. Disposing this object will cancel the subscription, preventing the handler from receiving more messages.</returns>
    public virtual Task<IDisposable> Subscribe<T>(string subject, AsyncMessageHandler<T> handler) where T : IMessage<T>
    {
        return Subscribe(subject, ActivityListener);

        async Task ActivityListener(RpcMessage message)
        {
            if (message.TryDeserialize<T>() is { } deserialized)
            {
                await handler(deserialized, message);
            }
        }
    }

    /// <summary>
    /// A true 'procedure call'.
    /// <br/>
    /// Publishes a message to the subject, then expects a reply from somewhere else.
    /// </summary>
    /// <param name="subject">The subject to publish the request on.</param>
    /// <param name="request">The request data.</param>
    /// <param name="timeout">The amount of time to wait before failing. Defaults to 5 seconds.</param>
    /// <param name="ct">The cancellation token to use for the request.</param>
    /// <typeparam name="TRequest">The protobuf message type for the request.</typeparam>
    /// <typeparam name="TResponse">The protobuf message type for the response.</typeparam>
    /// <exception cref="OperationCanceledException">If the request is canceled with <see cref="ct"/> instead of by timeout.</exception>
    /// <returns>The response data, deserialized.</returns>
    public virtual async Task<TResponse> Request<TRequest, TResponse>(
        string subject,
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken ct = default
    )
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage<TResponse>
    {
        timeout ??= TimeSpan.FromSeconds(5);

        // Fail fast if nobody is listening on the subject.
        if (!HasResponders(subject))
        {
            throw new NoRespondersException(subject);
        }

        // Prepare the request message.
        var replySubject = $"reply_{Random.Shared.NextUuid()}";
        var message = Serialize(request);
        message.Transport = this;
        message.ReplySubject = replySubject;

        // Subscribe to the reply subject.
        var tcs = new TaskCompletionSource<RpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        var subscription = await Subscribe(replySubject, msg => {
            tcs.TrySetResult(msg);
            return Task.CompletedTask;
        });

        // Send the request.
        await Publish(subject, message);

        // Wait for the specified interval before timing out.
        RpcMessage reply;

        try
        {
            reply = await tcs.Task.WaitAsync(timeout.Value, ct);
        }
        catch (TimeoutException)
        {
            throw new RequestTimeoutException(subject, timeout.Value);
        }
        finally
        {
            subscription.Dispose();
        }

        // Deserialize the response.
        return reply.Deserialize<TResponse>();
    }

    #region Lifecycle

    public abstract Task StartAsync(CancellationToken cancellationToken);

    public abstract Task StopAsync(CancellationToken cancellationToken);

    #endregion
}

public abstract class RpcException(string message) : Exception(message);

public sealed class RequestTimeoutException(string subject, TimeSpan period)
    : RpcException($"Requested data on '{subject}', but received no reply after {period}.");

public sealed class NoRespondersException(string subject)
    : RpcException($"Requested data on '{subject}', but no responders are available.");
