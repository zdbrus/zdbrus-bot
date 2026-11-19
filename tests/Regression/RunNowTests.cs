using Firebot.Behaviors.Extra;

internal static class RunNowTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
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
