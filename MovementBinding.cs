using System.Reflection;

namespace DealerSelfSupply
{
    // Finds, by name, how a game version spells the NPC movement calls that changed between 0.4.6
    // and 0.4.7 - and calls them. Knows nothing about the game's types (it is handed one), so it can
    // be tested against stand-ins shaped like each version (Tools/selftest) and not just in the game.
    //
    //   what                0.4.6                              0.4.7
    //   has a destination   HasDestination                     _hasDestination
    //   is paused           IsPaused                           (a zero-speed entry on the speed controller)
    //   resume              ResumeMovement()                   (the game un-pauses itself)
    //   speed multiplier    MoveSpeedMultiplier                SpeedController.SetSpeedMultiplier(v) / ._speedMultiplier
    // Anything not found is reported as null / false, and the caller uses Unity's own agent instead.
    internal sealed class MovementBinding
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly PropertyInfo _hasDestination, _isPaused, _speed, _controller, _controllerSpeed;
        private readonly MethodInfo _resume, _setControllerSpeed;
        public string Report { get; }

        public MovementBinding(Type movement)
        {
            var report = new List<string>();
            _hasDestination = Find(movement, typeof(bool), "HasDestination", "_hasDestination");
            report.Add("destination: " + (_hasDestination?.Name ?? "agent.hasPath"));
            _isPaused = Find(movement, typeof(bool), "IsPaused");
            report.Add("paused: " + (_isPaused?.Name ?? "agent.isStopped"));
            _resume = movement.GetMethod("ResumeMovement", Any, null, Type.EmptyTypes, null);
            report.Add("resume: " + (_resume != null ? "ResumeMovement()" : "agent.isStopped = false"));

            _speed = Find(movement, typeof(float), "MoveSpeedMultiplier");
            if (_speed != null && _speed.SetMethod == null) _speed = null;
            if (_speed != null) report.Add("speed: MoveSpeedMultiplier");
            else
            {
                _controller = movement.GetProperty("SpeedController", Any);
                _setControllerSpeed = _controller?.PropertyType.GetMethod("SetSpeedMultiplier", Any, null, new[] { typeof(float) }, null);
                _controllerSpeed = _controller != null ? Find(_controller.PropertyType, typeof(float), "_speedMultiplier") : null;
                if (_controller != null && _setControllerSpeed != null) report.Add("speed: SpeedController.SetSpeedMultiplier");
                else { _controller = null; report.Add("speed: not available (dealers walk at normal speed)"); }
            }
            Report = string.Join("; ", report);
        }

        private static PropertyInfo Find(Type t, Type returns, params string[] names)
        {
            foreach (string n in names)
            {
                var p = t.GetProperty(n, Any);
                if (p != null && p.GetMethod != null && p.PropertyType == returns) return p;
            }
            return null;
        }

        public bool? HasDestination(object m) => _hasDestination != null ? (bool?)(bool)_hasDestination.GetValue(m) : null;
        public bool? IsPaused(object m) => _isPaused != null ? (bool?)(bool)_isPaused.GetValue(m) : null;

        // True if the game's own resume call was made.
        public bool Resume(object m)
        {
            if (_resume == null) return false;
            _resume.Invoke(m, null);
            return true;
        }

        public float? GetSpeed(object m)
        {
            if (_speed != null) return (float)_speed.GetValue(m);
            if (_controller != null && _controllerSpeed != null) return (float)_controllerSpeed.GetValue(_controller.GetValue(m));
            return null;
        }

        public bool SetSpeed(object m, float value)
        {
            if (_speed != null) { _speed.SetValue(m, value); return true; }
            if (_controller != null) { _setControllerSpeed.Invoke(_controller.GetValue(m), new object[] { value }); return true; }
            return false;
        }
    }
}
