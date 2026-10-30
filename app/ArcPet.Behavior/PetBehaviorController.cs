using System;
using System.Collections.Generic;
using ArcPet.Animation;

namespace ArcPet.Behavior
{
    public interface IRandomSource { int Next(int minimumInclusive, int maximumExclusive); }
    public sealed class RandomSource : IRandomSource
    {
        private readonly Random random = new Random();
        public int Next(int minimumInclusive, int maximumExclusive) { return random.Next(minimumInclusive, maximumExclusive); }
    }
    public sealed class PlaybackRequestedEventArgs : EventArgs
    {
        public AnimationClip Clip { get; private set; }
        public PlaybackRequestedEventArgs(AnimationClip clip) { Clip = clip; }
    }
    // Input/time in, playback requests out. No window, timer or image dependencies.
    public sealed class PetBehaviorController
    {
        public const int LongPressMilliseconds = 500;
        public const int RocketPressMilliseconds = 1500;
        public const int MinimumIdleMilliseconds = 60000;
        public const int MaximumIdleMilliseconds = 90000;
        public const int InactivityMilliseconds = 300000;
        public const int SleepMilliseconds = 120000;
        public const int DefaultAutomaticLoops = 5;
        public const int DanceLoops = 12;
        public const int DanceLoopsPerFacing = 3;
        public static int AutomaticLoopCount(string action)
        { return action == "exercise" ? 2 : action == "think" ? 3 : DefaultAutomaticLoops; }
        public const int DoubleClickMilliseconds = 350;
        public const double DoubleClickDistance = 8;
        private readonly IDictionary<string, AnimationClip> clips;
        private readonly IRandomSource random;
        private long phaseStarted, phaseDuration, pressStarted;
        private bool pressed, pendingPress, busy, secondTapCandidate;
        private bool rocketAfterDown;
        private long lastShortRelease = -1;
        private double pressX, pressY, lastTapX, lastTapY;
        public event EventHandler<PlaybackRequestedEventArgs> PlaybackRequested;
        public event EventHandler SkateRequested;
        public event EventHandler RocketRequested;
        public bool IsRocket {get{return Phase!=null && Phase.StartsWith("rocket",StringComparison.Ordinal);}}
        private bool RocketAvailable {get{return clips.ContainsKey("rocket_charge") && clips.ContainsKey("rocket_hold") && clips.ContainsKey("rocket_launch") && clips.ContainsKey("rocket_flight") && clips.ContainsKey("rocket_land");}}
        public MovementMode TravelMode {get;private set;}
        public bool TravelAvailable {get;set;}
        public string Phase { get; private set; }
        public bool IsLocked { get; private set; }
        public bool SleepEnabled { get; private set; }
        public bool IsSleeping { get { return Phase == "sleep"; } }
        public long LastInteraction { get; private set; }
        public long NextAutomaticAt { get; private set; }
        public AnimationClip CurrentClip { get; private set; }
        public PetBehaviorController(IDictionary<string, AnimationClip> clips, IRandomSource random)
        {
            if (clips == null || random == null) throw new ArgumentNullException();
            foreach (string id in new[] { "idle", "sleep", "think", "icecream", "click_down", "click_hold", "click_release", "dance", "dance_mirrored" })
                if (!clips.ContainsKey(id)) throw new ArgumentException("Required animation missing: " + id);
            this.clips = clips; this.random = random; SleepEnabled = true;
        }
        public void Start(long now)
        { pressed = pendingPress = false; ClearTapHistory(); LastInteraction = now; EnterIdle(now); }
        private void ScheduleAutomatic(long now)
        { NextAutomaticAt = checked(now + random.Next(MinimumIdleMilliseconds, MaximumIdleMilliseconds + 1)); }
        private void Play(string phase, AnimationClip clip, long now)
        {
            Phase = phase; phaseStarted = now; phaseDuration = clip.DurationMilliseconds; CurrentClip = clip;
            var handler = PlaybackRequested;
            if (handler != null) handler(this, new PlaybackRequestedEventArgs(clip));
        }
        private static AnimationClip Finite(AnimationClip clip, int repetitions)
        {
            var frames = new List<AnimationFrame>();
            for (int i = 0; i < repetitions; i++) frames.AddRange(clip.Frames);
            return new AnimationClip(clip.Id, false, frames);
        }
        private void EnterIdle(long now)
        {
            ScheduleAutomatic(now); Play("idle", clips["idle"], now);
        }
        private bool SleepDue(long now)
        { return SleepEnabled && now - LastInteraction >= InactivityMilliseconds; }
        private void CompleteRandomAction(long now)
        {
            // Evaluate eligibility at completion, so activity or disabling sleep
            // cancels an outdated request. Never insert an idle/random clip first.
            if (SleepDue(now) && !busy && !pendingPress)
                Play("sleep", clips["sleep"], now);
            else EnterIdle(now);
        }
        public bool Touch(long now)
        {
            LastInteraction = now;
            // Travel has its own any-input interruption rule. Ordinary animation does not.
            if(Phase=="skate") {StopSkating(now);return true;}
            // Ordinary activity updates inactivity only. Qualified gestures own preemption.
            if (!IsSleeping) return false;
            pressed = false; pendingPress = false; ClearTapHistory();
            EnterIdle(now); return true;
        }
        public void SetMovementMode(MovementMode value,long now)
        {Touch(now);TravelMode=Enum.IsDefined(typeof(MovementMode),value) ? value : MovementMode.Off;}
        public void BeginSkating(int direction,long now)
        {
            if(IsLocked || TravelMode==MovementMode.Off || !TravelAvailable || busy || Phase!="idle")return;
            pressed=pendingPress=false;ClearTapHistory();ChangeSkateDirection(direction,now);
        }
        public void ChangeSkateDirection(int direction,long now)
        {
            if(IsLocked || TravelMode==MovementMode.Off || busy || (Phase!="idle" && Phase!="skate"))return;
            string id=direction>0 ? "skateboard_right" : "skateboard_left";
            if(clips.ContainsKey(id))Play("skate",clips[id],now);
        }
        public void StopSkating(long now)
        {
            if(Phase!="skate")return;
            pressed=pendingPress=false;ClearTapHistory();CompleteRandomAction(now);
        }
        public void SetLocked(bool value, long now)
        {
            Touch(now); CancelPress(now); IsLocked = value;
        }
        public void SetSleepEnabled(bool value, long now)
        {
            Touch(now); SleepEnabled = value;
        }
        public void SetBusy(bool value, long now)
        {
            Touch(now); busy = value;
            if (!value && Phase == "idle") ScheduleAutomatic(now);
        }
        private void ClearTapHistory()
        { lastShortRelease = -1; secondTapCandidate = false; }
        public void SecondaryDown(long now)
        { CancelPress(now); Touch(now); }
        public void PrimaryDown(long now) { PrimaryDown(now, 0, 0); }
        public void PrimaryDown(long now, double screenX, double screenY)
        {
            if (Touch(now)) { ClearTapHistory(); return; }
            if (Phase != "idle" && Phase != "automatic") return;
            double dx = screenX - lastTapX, dy = screenY - lastTapY;
            secondTapCandidate = lastShortRelease >= 0 && now >= lastShortRelease &&
                now - lastShortRelease <= DoubleClickMilliseconds &&
                dx * dx + dy * dy <= DoubleClickDistance * DoubleClickDistance;
            lastShortRelease = -1;
            pressed = true; pendingPress = true; pressStarted = now;
            rocketAfterDown=false;
            pressX = screenX; pressY = screenY;
        }
        private void RecognizeJumpGesture(long now)
        {
            pendingPress = false; ClearTapHistory();
            if (Phase == "idle" || Phase == "automatic") BeginDown(now);
        }
        private void RecognizeDanceGesture(long now)
        {
            pressed = pendingPress = false; rocketAfterDown = false; ClearTapHistory();
            // Same completion/preemption policy as ordinary automatic actions,
            // but triggered only by a qualified double click, never by the random pool.
            string facing = random.Next(0, 2) == 0 ? "dance" : "dance_mirrored";
            var frames = new List<AnimationFrame>();
            string opposite = facing == "dance" ? "dance_mirrored" : "dance";
            for (int loop = 0; loop < DanceLoops; loop++)
                frames.AddRange(clips[(loop / DanceLoopsPerFacing) % 2 == 0 ? facing : opposite].Frames);
            Play("automatic", new AnimationClip(facing, false, frames), now);
        }
        public void PrimaryUp(long now)
        {
            // Mouse-up may arrive between timer ticks; classify by the actual
            // held duration before clearing pressed, including the 1.5s boundary.
            if(IsLocked && pressed && RocketAvailable && now-pressStarted>=RocketPressMilliseconds)
            {
                if(Phase=="hold")BeginRocketPrepare(now);
                else if(Phase=="down" || pendingPress)rocketAfterDown=true;
            }
            Touch(now); pressed = false;
            if (pendingPress && (Phase == "idle" || Phase == "automatic"))
            {
                if (now - pressStarted >= LongPressMilliseconds)
                {
                    if (IsLocked) RecognizeJumpGesture(now);
                    else { pendingPress = false; ClearTapHistory(); }
                }
                else if (secondTapCandidate) RecognizeDanceGesture(now);
                else
                {
                    // A short single click only registers activity, never a jump.
                    pendingPress = false; secondTapCandidate = false;
                    lastShortRelease = now; lastTapX = pressX; lastTapY = pressY;
                }
            }
            else if (Phase == "hold") BeginHoldReturn(now);
            else if(Phase=="rocket_hold")Play("rocket_ready_return",TailToAnchor(clips["rocket_hold"],now-phaseStarted,160),now);
        }
        public void CancelPress(long now)
        {
            pressed = false; pendingPress = false; ClearTapHistory();
            rocketAfterDown=false;
            if(IsRocket){CancelRocket(now);return;}
            if (Phase == "hold") BeginHoldReturn(now);
            // A descent that already started always finishes before the release.
        }
        private void BeginDown(long now)
        { pendingPress = false; ClearTapHistory(); Play("down", Finite(clips["click_down"], 1), now); }
        private void BeginRelease(long now)
        { Play("release", Finite(clips["click_release"], 1), now); }
        private void BeginHoldReturn(long now)
        {
            AnimationClip hold = clips["click_hold"];
            int index = hold.GetFrameIndex(now - phaseStarted);
            var frames = new List<AnimationFrame>();
            bool backwards = index + 1 <= hold.Frames.Count - index;
            int count = backwards ? index + 1 : hold.Frames.Count - index;
            int duration = Math.Max(8, (int)Math.Ceiling(200.0 / count));
            // Walk adjacent approved poses to the shared crouch, never snap there.
            if (backwards)
                for (int i = index; i >= 0; i--) frames.Add(new AnimationFrame(hold.Frames[i].AssetPath, duration));
            else
                for (int i = index; i < hold.Frames.Count; i++) frames.Add(new AnimationFrame(hold.Frames[i].AssetPath, duration));
            Play("hold_return", new AnimationClip("click_hold_return", false, frames), now);
        }
        private void BeginRocketPrepare(long now)
        {
            // Return through adjacent approved crouch poses before the distinct
            // colour ramp. Its first key is the exact ordinary crouch endpoint.
            if(Phase=="hold")
            {
                AnimationClip hold=clips["click_hold"];int index=hold.GetFrameIndex(now-phaseStarted);
                var frames=new List<AnimationFrame>();bool backwards=index+1<=hold.Frames.Count-index;
                int count=backwards?index+1:hold.Frames.Count-index;int duration=Math.Max(8,(int)Math.Ceiling(160.0/count));
                if(backwards)for(int i=index;i>=0;i--)frames.Add(new AnimationFrame(hold.Frames[i].AssetPath,duration));
                else for(int i=index;i<hold.Frames.Count;i++)frames.Add(new AnimationFrame(hold.Frames[i].AssetPath,duration));
                Play("rocket_prepare",new AnimationClip("click_rocket_prepare",false,frames),now);
            }
            else Play("rocket_prepare",Finite(clips["click_down"],1),now);
        }
        private void BeginRocketLaunch(long now)
        {
            Play("rocket_launch",Finite(clips["rocket_launch"],1),now);
            var handler=RocketRequested;if(handler!=null)handler(this,EventArgs.Empty);else CompleteRocket(now);
        }
        public void CompleteRocket(long now)
        {
            if(Phase!="rocket_launch" && Phase!="rocket_flight")return;
            AnimationClip source=clips[Phase=="rocket_launch"?"rocket_launch":"rocket_flight"];
            Play("rocket_settle",TailToAnchor(source,now-phaseStarted,240),now);
        }
        private static AnimationClip TailToAnchor(AnimationClip clip,long elapsed,int maximumDuration)
        {
            int index=clip.GetFrameIndex(elapsed),count=clip.Frames.Count-index;
            int duration=Math.Max(8,Math.Min(40,maximumDuration/count));var frames=new List<AnimationFrame>();
            for(int i=index;i<clip.Frames.Count;i++)frames.Add(new AnimationFrame(clip.Frames[i].AssetPath,duration));
            return new AnimationClip(clip.Id+"_anchor",false,frames);
        }
        public void CancelRocket(long now)
        {if(!IsRocket)return;pressed=pendingPress=false;ClearTapHistory();LastInteraction=now;EnterIdle(now);}
        public void Tick(long now)
        {
            if (!pressed && lastShortRelease >= 0 && now - lastShortRelease > DoubleClickMilliseconds)
                ClearTapHistory();
            if (IsLocked && pendingPress && pressed && now - pressStarted >= LongPressMilliseconds &&
                (Phase == "idle" || Phase == "automatic"))
                RecognizeJumpGesture(now);
            if(pressed && RocketAvailable && now-pressStarted>=RocketPressMilliseconds && Phase=="hold")BeginRocketPrepare(now);
            if(IsRocket)
            {
                if(Phase=="rocket_prepare" && now-phaseStarted>=phaseDuration)Play("rocket_charge",Finite(clips["rocket_charge"],1),now);
                else if(Phase=="rocket_charge" && now-phaseStarted>=phaseDuration)
                {if(pressed)Play("rocket_hold",clips["rocket_hold"],now);else BeginRocketLaunch(now);}
                else if(Phase=="rocket_ready_return" && now-phaseStarted>=phaseDuration)BeginRocketLaunch(now);
                else if(Phase=="rocket_launch" && now-phaseStarted>=phaseDuration)Play("rocket_flight",clips["rocket_flight"],now);
                else if(Phase=="rocket_settle" && now-phaseStarted>=phaseDuration)Play("rocket_land",Finite(clips["rocket_land"],1),now);
                else if(Phase=="rocket_land" && now-phaseStarted>=phaseDuration){LastInteraction=now;EnterIdle(now);}
                return;
            }
            if (Phase == "sleep")
            {
                if (now - phaseStarted >= SleepMilliseconds) { LastInteraction = now; EnterIdle(now); }
                return;
            }
            // The movement controller owns the entire trip, including its return
            // leg. Bedtime cannot preempt it; StopSkating settles deferred sleep.
            if(Phase=="skate")return;
            if (Phase == "down")
            {
                if (now - phaseStarted >= phaseDuration)
                {
                    if(RocketAvailable && (rocketAfterDown || pressed && now-pressStarted>=RocketPressMilliseconds))
                    {rocketAfterDown=false;Play("rocket_charge",Finite(clips["rocket_charge"],1),now);}
                    else if (pressed) Play("hold", clips["click_hold"], now); else BeginRelease(now);
                }
                return;
            }
            if (Phase == "hold") return;
            if (Phase == "hold_return")
            { if (now - phaseStarted >= phaseDuration) BeginRelease(now); return; }
            if (Phase == "release")
            { if (now - phaseStarted >= phaseDuration) EnterIdle(now); return; }
            if (Phase == "automatic")
            {
                if (now - phaseStarted >= phaseDuration)CompleteRandomAction(now);
                return;
            }
            if (Phase != "idle" || busy) return;
            if (pendingPress)
            {
                return;
            }
            if (SleepDue(now))
            { Play("sleep", clips["sleep"], now); return; }
            if (now >= NextAutomaticAt)
            {
                var choices=new List<string>();
                foreach(string action in new[]{"icecream","think","music","exercise"})
                    if(clips.ContainsKey(action))choices.Add(action);
                if(!IsLocked && TravelMode!=MovementMode.Off && TravelAvailable && clips.ContainsKey("skateboard_left") && clips.ContainsKey("skateboard_right"))choices.Add("skate");
                if(clips.ContainsKey("calling"))choices.Add("calling");
                if(choices.Count==0){NextAutomaticAt=LastInteraction+InactivityMilliseconds;return;}
                string id=choices[random.Next(0,choices.Count)];
                if(id=="skate")
                {
                    var handler=SkateRequested;if(handler!=null)handler(this,EventArgs.Empty);
                    if(Phase=="idle")ScheduleAutomatic(now);
                    return;
                }
                AnimationClip automatic = Finite(clips[id], AutomaticLoopCount(id));
                Play("automatic", automatic, now);
            }
        }
    }
}
