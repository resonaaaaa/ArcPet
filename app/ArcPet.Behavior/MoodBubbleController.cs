using System;
using System.Collections.Generic;

namespace ArcPet.Behavior
{
    public sealed class MoodContent
    {
        public string Id { get; private set; }
        public string Category { get; private set; }
        public MoodContent(string id,string category)
        {
            if(String.IsNullOrWhiteSpace(id) || (category!="A" && category!="B" && category!="C"))throw new ArgumentException("Invalid mood content.");
            Id=id;Category=category;
        }
    }
    // Independent clock-driven overlay state. It cannot request pet playback,
    // change input recognition, extend an action or write preferences.
    public sealed class MoodBubbleController
    {
        public const int MinimumIdleMilliseconds=20000, MaximumIdleMilliseconds=30000, IdleLifetimeMilliseconds=4000;
        private readonly IRandomSource random;
        private readonly List<MoodContent> contents;
        private string mode,previousContent;
        private long contextStarted,loopDuration,loopIndex;
        private bool available=true;
        public string ContentId {get;private set;}
        public bool IsVisible {get{return ContentId!=null;}}
        public long StartedAt {get;private set;}
        public long NextIdleAt {get;private set;}
        public int Generation {get;private set;}
        public string Mode {get{return mode;}}
        public MoodBubbleController(IEnumerable<MoodContent> content,IRandomSource random)
        {
            if(content==null || random==null)throw new ArgumentNullException();
            contents=new List<MoodContent>(content);this.random=random;
            var ids=new HashSet<string>();int idle=0,think=0;
            foreach(var item in contents)
            {
                if(item==null || !ids.Add(item.Id))throw new ArgumentException("Duplicate mood content.");
                if(item.Category!="A")idle++;if(item.Category!="B")think++;
            }
            if(idle<2 || think<2)throw new ArgumentException("Each context needs at least two different contents.");
            NextIdleAt=Int64.MaxValue;
        }
        private void Schedule(long now){NextIdleAt=checked(now+random.Next(MinimumIdleMilliseconds,MaximumIdleMilliseconds+1));}
        private void Show(long now)
        {
            var choices=new List<string>();
            foreach(var item in contents)
                if((mode=="think" ? item.Category!="B" : item.Category!="A") && item.Id!=previousContent)choices.Add(item.Id);
            ContentId=choices[random.Next(0,choices.Count)];previousContent=ContentId;StartedAt=now;Generation++;
        }
        public void Enter(string phase,string action,long singleLoopMilliseconds,long now)
        {
            ContentId=null;NextIdleAt=Int64.MaxValue;contextStarted=now;loopIndex=0;
            mode=phase=="idle" ? "idle" : phase=="automatic" && action=="think" ? "think" : null;
            if(mode=="think")
            {
                if(singleLoopMilliseconds<=0)throw new ArgumentOutOfRangeException("singleLoopMilliseconds");
                loopDuration=singleLoopMilliseconds;if(available)Show(now);
            }
            else if(mode=="idle" && available)Schedule(now);
        }
        public void SetAvailable(bool value,long now)
        {
            if(value==available)return;available=value;ContentId=null;NextIdleAt=Int64.MaxValue;
            if(!value)return;
            if(mode=="idle")Schedule(now);
            else if(mode=="think") {loopIndex=Math.Max(0,now-contextStarted)/loopDuration;Show(now);}
        }
        public void Tick(long now)
        {
            if(!available || mode==null)return;
            if(mode=="think")
            {
                long current=Math.Max(0,now-contextStarted)/loopDuration;
                if(current!=loopIndex){loopIndex=current;Show(contextStarted+current*loopDuration);}
                return;
            }
            if(IsVisible && now-StartedAt>=IdleLifetimeMilliseconds)ContentId=null;
            // The interval is measured between appearances, not after the 4s display.
            // A delayed UI tick starts one fresh bubble and never replays a backlog.
            if(now>=NextIdleAt){Show(now);Schedule(now);}
        }
        public double Opacity(long now,int delay,int fadeMilliseconds)
        {
            if(!IsVisible)return 0;
            double u=Math.Max(0,Math.Min(1,(now-StartedAt-delay)/(double)fadeMilliseconds));
            return u*u*(3-2*u);
        }
    }
}
