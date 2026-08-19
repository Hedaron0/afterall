namespace AfterAll.Run
{
    /// <summary>How a run stopped. Extracted/Completed both bank; Died loses everything unbanked.</summary>
    public enum RunOutcome
    {
        Died,
        Extracted,
        Completed
    }

    /// <summary>
    /// Snapshot of a finished run, handed to the UI the moment it ends. Built by RunDirector before
    /// the state is reset, so DepthReached is the depth the player actually got to — not the 0 the
    /// next run starts on.
    /// </summary>
    public readonly struct RunSummary
    {
        public readonly RunOutcome Outcome;
        public readonly int DepthReached;
        /// <summary>Value banked by THIS extraction. Always 0 on death.</summary>
        public readonly int BankedThisRun;
        /// <summary>Persistent MetaProgress total after this run was accounted for.</summary>
        public readonly int TotalBanked;
        public readonly int TargetDepth;
        public readonly int TargetBankedEchoes;

        public RunSummary(RunOutcome outcome, int depthReached, int bankedThisRun, int totalBanked,
                          int targetDepth, int targetBankedEchoes)
        {
            Outcome = outcome;
            DepthReached = depthReached;
            BankedThisRun = bankedThisRun;
            TotalBanked = totalBanked;
            TargetDepth = targetDepth;
            TargetBankedEchoes = targetBankedEchoes;
        }
    }
}
