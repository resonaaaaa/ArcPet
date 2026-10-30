using System;
using System.Collections.Generic;
using ArcPet.Behavior;
using ArcPet.Animation;

internal sealed class MoodRandom:IRandomSource
{
    public int Wait=20000,Choice;
    public int Next(int min,int max){return min==0 ? Choice%(max-min) : Wait;}
}
internal static class MoodTests
{
    private static MoodBubbleController New(MoodRandom random)
    {
        var list=new List<MoodContent>();
        foreach(string id in new[]{"question","book","chatgpt","forest","python"})list.Add(new MoodContent(id,"A"));
        foreach(string id in new[]{"heart","headphones","skateboard"})list.Add(new MoodContent(id,"B"));
        foreach(string id in new[]{"fish","bird","rice","plane","gamepad","sun","icecream"})list.Add(new MoodContent(id,"C"));
        return new MoodBubbleController(list,random);
    }
    public static void Run(string assets,Action<bool,string> check)
    {
        var r=new MoodRandom();var m=New(r);m.Enter("idle","idle",0,0);
        check(!m.IsVisible && m.NextIdleAt==20000,"idle schedules independent minimum 20 second wait");
        m.Tick(19999);check(!m.IsVisible,"no early idle bubble");m.Tick(20000);
        check(m.IsVisible && m.ContentId=="heart" && m.StartedAt==20000,"idle first bubble appears on deadline");
        check(m.Opacity(20000,0,180)==0 && m.Opacity(20090,0,180)==0.5 && m.Opacity(20180,0,180)==1,"smoothstep small cloud fade");
        check(m.Opacity(20100,140,200)==0 && m.Opacity(20240,140,200)==0.5,"medium follows small");
        check(m.Opacity(20299,300,240)==0 && m.Opacity(20540,300,240)==1,"main and content appear last");
        m.Tick(23999);check(m.IsVisible,"idle bubble lasts full 4 seconds including entrance");m.Tick(24000);
        check(!m.IsVisible && m.Opacity(24000,0,180)==0 && m.Opacity(24000,300,240)==0,"all layers hide together without exit");
        m.Tick(40000);check(m.IsVisible && m.ContentId!="heart" && m.StartedAt==40000,"start-to-start interval and different content");
        r.Wait=30000;m.Enter("idle","idle",0,50000);check(m.NextIdleAt==80000,"maximum idle wait is 30 seconds");
        m.Tick(80000);m.Enter("automatic","music",0,80010);
        check(!m.IsVisible && m.NextIdleAt==Int64.MaxValue,"ordinary random action immediately preempts bubble");
        m.Tick(999999);check(!m.IsVisible,"no backlog bubbles in disallowed context");
        m.Enter("automatic","think",6000,1000000);string a=m.ContentId;int gen=m.Generation;
        check(m.IsVisible && a!=null,"every thinking entry creates first bubble immediately");
        m.Tick(1005999);check(m.Generation==gen && m.ContentId==a,"thinking keeps content for one exact cycle");
        m.Tick(1006000);string b=m.ContentId;
        check(m.Generation==gen+1 && b!=a && m.StartedAt==1006000 && m.Opacity(1006000,300,240)==0,"thinking boundary replaces and restarts entrance with different content");
        m.Tick(1012000);check(m.Generation==gen+2 && m.ContentId!=b,"third cycle gets a third bubble");
        m.Enter("idle","idle",0,1018000);check(!m.IsVisible && m.NextIdleAt==1048000,"final thinking cycle exits without extra bubble");
        foreach(string phase in new[]{"sleep","down","hold","hold_return","release","skate"})
        {
            m.Enter("automatic","think",6000,0);m.Enter(phase,"other",0,10);m.Tick(900000);
            check(!m.IsVisible,"bubble excluded from "+phase);
        }
        foreach(string mode in new[]{"idle","think"})
        {
            var seen=new HashSet<string>();
            for(int choice=0;choice<12;choice++)
            {
                r=new MoodRandom{Choice=choice};m=New(r);m.Enter(mode=="idle"?"idle":"automatic",mode,6000,0);
                if(mode=="idle")m.Tick(20000);seen.Add(m.ContentId);
            }
            string[] required=mode=="idle"?new[]{"heart","headphones","skateboard","fish","bird","rice","plane","gamepad","sun","icecream"}:new[]{"question","book","chatgpt","forest","python","fish","bird","rice","plane","gamepad","sun","icecream"};
            check(seen.SetEquals(required),mode+" exact category pool including every shared icon");
        }
        r=new MoodRandom();m=New(r);m.Enter("automatic","think",6000,0);m.SetAvailable(false,400);m.Tick(9000);
        check(!m.IsVisible,"hidden pet hides all cloud layers");m.SetAvailable(true,9010);a=m.ContentId;m.Tick(9020);
        check(m.IsVisible && m.ContentId==a && m.StartedAt==9010,"restore hidden thinking respects current cycle without reset storm");
        m.Enter("idle","idle",0,10000);m.SetAvailable(false,11000);m.Tick(90000);m.SetAvailable(true,90000);
        check(!m.IsVisible && m.NextIdleAt==110000,"restore hidden idle starts fresh wait");
        m.Tick(500000);check(m.IsVisible && m.Generation>0 && m.StartedAt==500000 && m.NextIdleAt==520000,"delayed idle tick produces only one fresh display");
        m.Enter("automatic","think",6000,600000);gen=m.Generation;m.Tick(613000);
        check(m.Generation==gen+1 && m.StartedAt==612000,"delayed thinking tick selects actual current cycle without replaying old ones");
        // Wire exactly as the desktop host, with an independent random source.
        var clips=new JsonAnimationCatalog(assets).LoadAll();var br=new FixedRandom{Choice=1,Wait=60000};
        var pet=new PetBehaviorController(clips,br);m=New(new MoodRandom());long now=0;
        pet.PlaybackRequested+=delegate(object sender,PlaybackRequestedEventArgs e){m.Enter(pet.Phase,e.Clip.Id,clips["think"].DurationMilliseconds,now);};
        pet.Start(now);now=20000;pet.Tick(now);m.Tick(now);check(m.IsVisible,"integrated idle bubble");
        now=60000;pet.Tick(now);m.Tick(now);check(pet.CurrentClip.Id=="think" && m.IsVisible && m.Mode=="think","random think replaces idle overlay with first thinking bubble");
        a=m.ContentId;now=66000;pet.Tick(now);m.Tick(now);check(m.ContentId!=a && pet.Phase=="automatic","content change does not restart thinking playback");
        pet.SetLocked(true,now);pet.PrimaryDown(now);pet.PrimaryUp(now+20);check(m.IsVisible && pet.Phase=="automatic","single click leaves thinking and bubble alone");
        now+=100;pet.PrimaryDown(now);now+=20;pet.PrimaryUp(now);check(pet.Phase=="automatic" && pet.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && !m.IsVisible,"qualified double click clears thinking bubble immediately");
    }
}
