using System;
using System.Collections.Generic;

namespace LineZero.Tests.Framework;

public static class TestAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new TestAssertionException(message);
        }
    }

    public static void False(bool condition, string message)
    {
        True(!condition, message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestAssertionException(
                $"{message} Expected: {expected}. Actual: {actual}.");
        }
    }

    public static void Same(object expected, object actual, string message)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new TestAssertionException(message);
        }
    }

    public static void NearlyEqual(
        double expected,
        double actual,
        double tolerance,
        string message)
    {
        if (!double.IsFinite(expected) ||
            !double.IsFinite(actual) ||
            !double.IsFinite(tolerance) ||
            tolerance < 0.0 ||
            Math.Abs(expected - actual) > tolerance)
        {
            throw new TestAssertionException(
                $"{message} Expected: {expected:R}. Actual: {actual:R}. " +
                $"Tolerance: {tolerance:R}.");
        }
    }

    public static TException Throws<TException>(Action action, string message)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new TestAssertionException(
                $"{message} Expected {typeof(TException).Name}, but received " +
                $"{exception.GetType().Name}.",
                exception);
        }

        throw new TestAssertionException(
            $"{message} Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}

public sealed class TestAssertionException : Exception
{
    public TestAssertionException(string message)
        : base(message)
    {
    }

    public TestAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
