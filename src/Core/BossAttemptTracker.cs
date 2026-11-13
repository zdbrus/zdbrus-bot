namespace Firebot.Core;

internal static class BossAttemptTracker
{
    private static int _stage;
    private static int _attempts;

    public static int RecordAttempt(int stage)
    {
        if (stage <= 0) return 0;
        if (stage != _stage)
        {
            _stage = stage;
            _attempts = 0;
        }

        return ++_attempts;
    }

    public static int CountAtStage(int stage)
        => stage > 0 && stage == _stage ? _attempts : 0;

    public static void Reset()
    {
        _stage = 0;
        _attempts = 0;
    }
}
