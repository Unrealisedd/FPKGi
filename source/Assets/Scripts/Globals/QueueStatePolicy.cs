public enum QueueStartupDisposition
{
    Keep,
    ResetToQueued,
    BlockUnknownInstall,
    BlockUnknownSubmission,
    RemoveCompleted,
    FailInvalid
}

public static class QueueStatePolicy
{
    public static bool IsKnown(string state)
    {
        return state == "queued" || state == "downloading" || state == "retrying" ||
               state == "failed" || state == "submitted" || state == "submitting" ||
               state == "installing" || state == "completed" || state == "blocked";
    }

    public static bool IsDispatchable(string state)
    {
        return state == "queued" || state == "failed";
    }

    public static string RepairInvalidAttemptState(string state)
    {
        return GetStartupDisposition(state) == QueueStartupDisposition.Keep &&
               IsDispatchable(state)
            ? "failed"
            : state;
    }

    public static QueueStartupDisposition GetStartupDisposition(string state)
    {
        if (state == "completed")
            return QueueStartupDisposition.RemoveCompleted;
        if (state == "installing")
            return QueueStartupDisposition.BlockUnknownInstall;
        if (state == "submitting")
            return QueueStartupDisposition.BlockUnknownSubmission;
        if (state == "downloading" || state == "retrying")
            return QueueStartupDisposition.ResetToQueued;
        return IsKnown(state)
            ? QueueStartupDisposition.Keep
            : QueueStartupDisposition.FailInvalid;
    }
}
