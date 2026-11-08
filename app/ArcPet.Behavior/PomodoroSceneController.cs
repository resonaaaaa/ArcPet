using System;
using System.Collections.Generic;
using ArcPet.Animation;

namespace ArcPet.Behavior
{
    // Owns only scene sequencing. Commands queue at whole-clip boundaries.
    public sealed class PomodoroSceneController
    {
        private readonly IDictionary<string,AnimationClip> clips;
        private long deadline;
        private bool wantsRunning, ending;
        public string Stage { get; private set; }
        public bool IsActive { get { return Stage != null; } }
        public bool IsEnding { get { return ending; } }
        public event EventHandler<PlaybackRequestedEventArgs> PlaybackRequested;
        public event EventHandler Finished;
        public PomodoroSceneController(IDictionary<string,AnimationClip> clips) { this.clips=clips; }
        private void Play(string stage,long now)
        {
            Stage=stage;var clip=clips["pomodoro_"+stage];deadline=checked(now+clip.DurationMilliseconds);
            var handler=PlaybackRequested;if(handler!=null)handler(this,new PlaybackRequestedEventArgs(clip));
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
            if(Stage=="start" || Stage=="resume") Play("read",now);
            else if(Stage=="read") Play(ending ? "stretch" : wantsRunning ? "read" : "pause",now);
            else if(Stage=="pause") Play("paused",now);
            // Both endings begin with an open book. From chin-rest, take the
            // book up first instead of jumping straight to the reading pose.
            else if(Stage=="paused") Play(ending || wantsRunning ? "resume" : "paused",now);
            else if(Stage=="stretch") Play("end",now);
        }
    }
}
