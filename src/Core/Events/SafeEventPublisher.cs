using System;
using System.Diagnostics;

namespace LineZero.Core.Events;

public static class SafeEventPublisher
{
    public static int Publish(Action? subscribers, string eventName)
    {
        ValidateEventName(eventName);
        if (subscribers is null)
        {
            return 0;
        }

        int failureCount = 0;
        Delegate[] invocationList = subscribers.GetInvocationList();
        for (int index = 0; index < invocationList.Length; index++)
        {
            Action subscriber = (Action)invocationList[index];
            try
            {
                subscriber();
            }
            catch (Exception exception)
            {
                failureCount++;
                ReportSubscriberFailure(eventName, subscriber, exception);
            }
        }

        return failureCount;
    }

    public static int Publish<T>(
        Action<T>? subscribers,
        T argument,
        string eventName)
    {
        ValidateEventName(eventName);
        if (subscribers is null)
        {
            return 0;
        }

        int failureCount = 0;
        Delegate[] invocationList = subscribers.GetInvocationList();
        for (int index = 0; index < invocationList.Length; index++)
        {
            Action<T> subscriber = (Action<T>)invocationList[index];
            try
            {
                subscriber(argument);
            }
            catch (Exception exception)
            {
                failureCount++;
                ReportSubscriberFailure(eventName, subscriber, exception);
            }
        }

        return failureCount;
    }

    public static int Publish<TFirst, TSecond>(
        Action<TFirst, TSecond>? subscribers,
        TFirst firstArgument,
        TSecond secondArgument,
        string eventName)
    {
        ValidateEventName(eventName);
        if (subscribers is null)
        {
            return 0;
        }

        int failureCount = 0;
        Delegate[] invocationList = subscribers.GetInvocationList();
        for (int index = 0; index < invocationList.Length; index++)
        {
            Action<TFirst, TSecond> subscriber =
                (Action<TFirst, TSecond>)invocationList[index];
            try
            {
                subscriber(firstArgument, secondArgument);
            }
            catch (Exception exception)
            {
                failureCount++;
                ReportSubscriberFailure(eventName, subscriber, exception);
            }
        }

        return failureCount;
    }

    private static void ValidateEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            throw new ArgumentException(
                "Event name must be non-empty.",
                nameof(eventName));
        }
    }

    private static void ReportSubscriberFailure(
        string eventName,
        Delegate subscriber,
        Exception exception)
    {
        string declaringType =
            subscriber.Method.DeclaringType?.FullName ?? "<unknown type>";
        Trace.TraceError(
            "Subscriber '{0}.{1}' failed while publishing event '{2}': {3}",
            declaringType,
            subscriber.Method.Name,
            eventName,
            exception);
    }
}
