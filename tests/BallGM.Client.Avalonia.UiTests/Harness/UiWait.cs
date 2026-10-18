using Avalonia.Threading;

namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>
/// Waits for background work from a headless test while keeping the UI thread's queue moving.
/// Headless tests stay synchronous: an async test yields the shared UI thread to other tests
/// mid-way, and their interleaving deadlocked the suite.
/// </summary>
internal static class UiWait
{
    public static T For<T>(Task<T> task, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Background work did not finish in time.");
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.RunJobs();
            }

            Thread.Sleep(2);
        }

        return task.GetAwaiter().GetResult();
    }

    public static void For(Task task, TimeSpan? timeout = null) => For(task.ContinueWith(_ => 0, TaskScheduler.Default), timeout);
}
