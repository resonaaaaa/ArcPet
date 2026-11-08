using System;

namespace ArcPet.Behavior
{
    public enum PomodoroState { Idle, Running, Paused, Completed }

    // Monotonic milliseconds in; no dependency on playback, windows or timers.
    public sealed class PomodoroTimer
    {
        private long deadline, pausedRemaining;
        public PomodoroState State { get; private set; }
        public int Minutes { get; private set; }
        public bool IsActive { get { return State == PomodoroState.Running || State == PomodoroState.Paused; } }
        public event EventHandler Completed;

        public bool Start(int minutes, long now)
        {
            if (minutes != 20 && minutes != 30 && minutes != 40 && minutes != 50 && minutes != 60)
                throw new ArgumentOutOfRangeException("minutes");
            if (IsActive) return false;
            Minutes = minutes;
            deadline = checked(now + minutes * 60000L);
            State = PomodoroState.Running;
            return true;
        }
        public long RemainingMilliseconds(long now)
        {
            return State == PomodoroState.Running ? Math.Max(0, deadline - now) :
                State == PomodoroState.Paused ? pausedRemaining : 0;
        }
        public void Tick(long now)
        {
            if (State != PomodoroState.Running || now < deadline) return;
            State = PomodoroState.Completed;
            var handler = Completed;
            if (handler != null) handler(this, EventArgs.Empty);
        }
        public void Pause(long now)
        {
            Tick(now);
            if (State != PomodoroState.Running) return;
            pausedRemaining = RemainingMilliseconds(now);
            State = PomodoroState.Paused;
        }
        public void Resume(long now)
        {
            if (State != PomodoroState.Paused) return;
            deadline = checked(now + pausedRemaining);
            State = PomodoroState.Running;
        }
        public void End()
        {
            State = PomodoroState.Idle;
            Minutes = 0;
            pausedRemaining = 0;
        }
    }
}
