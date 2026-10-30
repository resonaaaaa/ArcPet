using System;
using System.Diagnostics;

namespace ArcPet.Animation
{
    // Host calls Update from its own event loop; this library has no UI dependency.
    public interface IPlaybackClock
    {
        long ElapsedMilliseconds { get; }
        void Restart();
        void Stop();
    }
    public sealed class MonotonicPlaybackClock : IPlaybackClock
    {
        private readonly Stopwatch watch = new Stopwatch();
        public long ElapsedMilliseconds { get { return watch.ElapsedMilliseconds; } }
        public void Restart() { watch.Restart(); }
        public void Stop() { watch.Stop(); }
    }
    public sealed class AnimationFrameChangedEventArgs : EventArgs
    {
        public AnimationClip Clip { get; private set; }
        public int FrameIndex { get; private set; }
        public AnimationFrame Frame { get { return Clip.Frames[FrameIndex]; } }
        public AnimationFrameChangedEventArgs(AnimationClip clip, int frameIndex)
        { Clip = clip; FrameIndex = frameIndex; }
    }
    public interface IAnimationPlayer
    {
        event EventHandler<AnimationFrameChangedEventArgs> FrameChanged;
        event EventHandler Completed;
        bool IsPlaying { get; }
        int CurrentFrameIndex { get; }
        void Play(AnimationClip clip);
        void Update();
        void Stop();
    }
    public sealed class AnimationPlayer : IAnimationPlayer
    {
        private readonly IPlaybackClock clock;
        private AnimationClip current;
        public event EventHandler<AnimationFrameChangedEventArgs> FrameChanged;
        public event EventHandler Completed;
        public bool IsPlaying { get; private set; }
        public int CurrentFrameIndex { get; private set; }
        public AnimationPlayer(IPlaybackClock clock)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            this.clock = clock; CurrentFrameIndex = -1;
        }
        public void Play(AnimationClip clip)
        {
            if (clip == null) throw new ArgumentNullException("clip");
            current = clip; CurrentFrameIndex = -1; IsPlaying = true; clock.Restart();
            Update();
        }
        public void Update()
        {
            if (!IsPlaying) return;
            long elapsed = clock.ElapsedMilliseconds;
            int index = current.GetFrameIndex(elapsed);
            if (index != CurrentFrameIndex)
            {
                CurrentFrameIndex = index;
                var handler = FrameChanged;
                if (handler != null) handler(this, new AnimationFrameChangedEventArgs(current, index));
            }
            if (!current.Loop && elapsed >= current.DurationMilliseconds)
            {
                Stop();
                var handler = Completed;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }
        public void Stop() { IsPlaying = false; clock.Stop(); }
    }
}
