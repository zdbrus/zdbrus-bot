using Firebot.Behaviors.Extra;

internal static class RunNowTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void WaitsForPlayer()
    {
        Check(Firebot.Core.UserActivityPolicy.ShouldWait(3f, true, false), "A recent click with a screen open must hold the task.");
        Check(!Firebot.Core.UserActivityPolicy.ShouldWait(30f, true, false), "A quiet player must not hold the task.");
        Check(!Firebot.Core.UserActivityPolicy.ShouldWait(3f, false, false), "No open screen means nothing of the player's to close.");
        Check(!Firebot.Core.UserActivityPolicy.ShouldWait(3f, true, true), "A task started with RUN must never wait.");
    }

    public static void KeepsStrangeDust()
    {
        Check(Firebot.Core.StrangeDustPolicy.CanSpend(100, 40, 0), "No reserve must allow spending.");
        Check(Firebot.Core.StrangeDustPolicy.CanSpend(1040, 40, 1000), "Spending down to exactly the reserve is allowed.");
        Check(!Firebot.Core.StrangeDustPolicy.CanSpend(1039, 40, 1000), "Spending below the reserve must be refused.");
        Check(!Firebot.Core.StrangeDustPolicy.CanSpend(double.NaN, 20, 0), "An unreadable balance must never spend.");
        Check(Firebot.Core.StrangeDustPolicy.CanSpend(50, 20, -5), "A negative reserve counts as none.");
    }

    public static void MakesDue()
    {
        var task = new TalentsTask();
        task.InitializeConfig("test.cfg");
        task.RestoreNextRun(DateTime.Now.AddHours(5));
        task.BubbleQuietUntil = DateTime.Now.AddHours(1);
        task.BubbleIdleStreak = 3;
        Check(task.DueTime > DateTime.Now, "Fixture should start not due.");
        task.RequestRunNow();
        Check(task.DueTime <= DateTime.Now && task.BubbleQuietUntil <= DateTime.Now && task.BubbleIdleStreak == 0,
            "Run now must make the task due immediately and clear the bubble cooldown.");
        Check(task.RunNowRequested, "Run now must flag the task so the scheduler picks it before lit bubbles.");
    }
}
