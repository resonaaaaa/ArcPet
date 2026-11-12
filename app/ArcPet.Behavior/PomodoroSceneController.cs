using System;
using System.Collections.Generic;
using ArcPet.Animation;

namespace ArcPet.Behavior
{
    // Owns only scene sequencing. Commands queue at whole-clip boundaries.
    public sealed class PomodoroSceneController
    {
        private const int MinimumPageInterval = 10000, MaximumPageInterval = 12000;
        // The approved 5.61 s loop blinked three times. Halve that rate.
        private const int BlinkInterval = 3740;
        private readonly IDictionary<string,AnimationClip> clips;
        private readonly IRandomSource random;
        private long deadline, pageAt, blinkAt, breathsRemaining;
        private bool wantsRunning, ending;
        public string Stage { get; private set; }
        public bool IsActive { get { return Stage != null; } }
        public bool IsEnding { get { return ending; } }
        public event EventHandler<PlaybackRequestedEventArgs> PlaybackRequested;
        public event EventHandler Finished;
        public PomodoroSceneController(IDictionary<string,AnimationClip> clips) : this(clips,new RandomSource()) { }
        public PomodoroSceneController(IDictionary<string,AnimationClip> clips, IRandomSource random)
        { this.clips=clips;this.random=random; }
        private void Play(string stage,long now)
        { Play(stage,clips["pomodoro_"+stage],now); }
        private void Play(string stage,AnimationClip clip,long now)
        {
            Stage=stage;deadline=checked(now+clip.DurationMilliseconds);
            var handler=PlaybackRequested;if(handler!=null)handler(this,new PlaybackRequestedEventArgs(clip));
        }
        private void PlanPage(long now,long pageTime)
        {
            int interval=random.Next(MinimumPageInterval,MaximumPageInterval+1);
            pageAt=checked(now+interval);
            long normalBreath=clips["pomodoro_read"].DurationMilliseconds;
            breathsRemaining=Math.Max(1,(interval-pageTime+normalBreath/2)/normalBreath);
        }
        private void BeginReading(long now)
        {
            blinkAt=checked(now+BlinkInterval);
            PlanPage(now,0);
            ContinueReading(now);
        }
        private void ContinueReading(long now)
        {
            if(breathsRemaining==0 || now>=pageAt)
            {
                var page=clips["pomodoro_read_page"];
                PlanPage(now,page.DurationMilliseconds);
                Play("read",page,now);
                return;
            }

            // Fit complete breaths into the chosen interval so the page never
            // interrupts a breath or a blink. Recompute after any late tick.
            var breath=clips["pomodoro_read"];
            long duration=Math.Max(1,(pageAt-now)/breathsRemaining);
            breathsRemaining--;
            bool blink=now+duration>=blinkAt;
            if(blink)
            {
                breath=clips["pomodoro_read_blink"];
                do { blinkAt=checked(blinkAt+BlinkInterval); } while(blinkAt<=now);
            }
            var frames=new List<AnimationFrame>();
            long sourceTime=0, frameStart=0;
            duration=Math.Max(duration,breath.Frames.Count);
            foreach(var frame in breath.Frames)
            {
                sourceTime+=frame.DurationMilliseconds;
                long frameEnd=Math.Max(frameStart+1,sourceTime*duration/breath.DurationMilliseconds);
                frames.Add(new AnimationFrame(frame.AssetPath,(int)(frameEnd-frameStart)));
                frameStart=frameEnd;
            }
            Play("read",new AnimationClip(breath.Id,false,frames),now);
        }
        public void Start(long now)
        { if(IsActive)return;ending=false;wantsRunning=true;Play("start",now); }
        public void SetPaused(bool paused) { if(IsActive && !ending)wantsRunning=!paused; }
        public void End() { if(IsActive)ending=true; }
        public void Tick(long now)
        {
            if(!IsActive || now<deadline)return;
            if(Stage=="end")
            {
                Stage=null;ending=false;var handler=Finished;if(handler!=null)handler(this,EventArgs.Empty);return;
            }
            if(Stage=="start" || Stage=="resume") BeginReading(now);
            else if(Stage=="read")
            {
                if(ending || !wantsRunning)Play(ending ? "stretch" : "pause",now);
                else ContinueReading(now);
            }
            else if(Stage=="pause") Play("paused",now);
            // Both endings begin with an open book. From chin-rest, take the
            // book up first instead of jumping straight to the reading pose.
            else if(Stage=="paused") Play(ending || wantsRunning ? "resume" : "paused",now);
            else if(Stage=="stretch") Play("end",now);
        }
    }
}
