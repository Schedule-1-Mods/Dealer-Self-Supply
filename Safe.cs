namespace DealerSelfSupply
{
    // Runs one piece of the mod so that a failure in it stays in it. Each job has its own name:
    // an error is logged at most once every 30 seconds (instead of every frame), and a job that
    // keeps failing is switched off for a minute. A game update that removes something a job needs
    // therefore costs that one feature, not the rest of the mod - and not a log full of repeats.
    // (A missing game member throws when the method using it is first called, so the call has to
    // be inside this try - that is why everything version-sensitive is called through here.)
    internal static class Safe
    {
        private sealed class State
        {
            public int Failures;
            public float LastLogged = -999f;
            public float OffUntil;
        }

        private static readonly Dictionary<string, State> _states = new Dictionary<string, State>();

        public static void Reset() => _states.Clear();

        public static void Run(string job, Action action)
        {
            if (!_states.TryGetValue(job, out State s)) _states[job] = s = new State();
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < s.OffUntil) return;
            try
            {
                action();
                s.Failures = 0;
            }
            catch (Exception e)
            {
                s.Failures++;
                if (now - s.LastLogged > 30f)
                {
                    s.LastLogged = now;
                    string what = e is TypeLoadException || e is MissingMemberException
                        ? " (the game has changed something this needs - " + e.Message + ")"
                        : ": " + e.Message;
                    Core.Log.Warning($"{job} failed{what}");
                }
                if (s.Failures >= 20)
                {
                    s.OffUntil = now + 60f;
                    s.Failures = 0;
                    Core.Log.Warning($"{job} keeps failing - paused for a minute.");
                }
            }
        }
    }
}
