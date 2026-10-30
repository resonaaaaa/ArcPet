using System;
using System.Collections.Generic;
using ArcPet.Animation;
using ArcPet.Behavior;

internal sealed class FixedRandom : IRandomSource
{
    public int Wait = 60000;
    public int Choice;
    public int Next(int min, int max) { return min == 0 ? Choice % max : Wait; }
}
internal sealed class BehaviorFixture
{
    public Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
    public FixedRandom Random = new FixedRandom();
    public PetBehaviorController Pet;
    public BehaviorFixture(string assets, int choice, int wait)
    {
        var catalog=new JsonAnimationCatalog(assets);
        foreach(string id in new[]{"idle","sleep","think","icecream","click_down","click_hold","click_release","dance","dance_mirrored"}) Clips.Add(id,catalog.Load(id));
        Random.Choice=choice;Random.Wait=wait;
        Pet=new PetBehaviorController(Clips,Random);Pet.Start(0);
    }
}
internal static class BehaviorTests
{
    private static PetBehaviorController Locked(string assets)
    { var p=new BehaviorFixture(assets,0,60000).Pet; p.SetLocked(true,0); return p; }
    public static void Run(string assets, Action<bool,string> check)
    {
        var f=new BehaviorFixture(assets,0,60000);var p=f.Pet;
        check(p.NextAutomaticAt==60000 && p.Phase=="idle","minimum random idle wait is 60s");
        p.Tick(59999);check(p.Phase=="idle","no early automatic action");
        p.Tick(60000);check(p.Phase=="automatic" && p.CurrentClip.Id=="icecream","random icecream selection");
        check(!p.CurrentClip.Loop && p.CurrentClip.Frames.Count==150 && p.CurrentClip.DurationMilliseconds==13500,"exactly five full icecream cycles");
        p.Tick(73499);check(p.Phase=="automatic","automatic cannot finish before fifth loop");
        p.Tick(73500);check(p.Phase=="idle" && p.NextAutomaticAt==133500,"idle gets fresh wait after five cycles");
        f=new BehaviorFixture(assets,1,90000);p=f.Pet;p.Tick(89999);check(p.Phase=="idle","maximum wait not early");p.Tick(90000);
        check(p.CurrentClip.Id=="think" && p.CurrentClip.DurationMilliseconds==18000 && p.CurrentClip.Frames.Count==180,"three complete thinking cycles and 90s upper bound");
        var actualRandom=new RandomSource();var values=new HashSet<int>();bool bounded=true;
        for(int i=0;i<1000;i++){int value=actualRandom.Next(60000,90001);values.Add(value);bounded &= value>=60000 && value<=90000;}
        check(bounded && values.Count>1,"production randomness varies within 60-90 seconds");

        f=new BehaviorFixture(assets,0,60000);p=f.Pet;p.PrimaryDown(0);p.Tick(600);p.PrimaryUp(650);
        check(p.Phase=="idle","unlocked input never starts interaction animation");
        p=Locked(assets);p.PrimaryDown(0);p.Tick(499);check(p.Phase=="idle","hold threshold waits full 500ms");
        p.Tick(500);check(p.Phase=="down","hold starts descent at 500ms");
        p.PrimaryUp(550);p.Tick(1099);check(p.Phase=="down","early release still finishes entire descent");
        p.Tick(1100);check(p.Phase=="release","descent completes before release");p.Tick(2000);check(p.Phase=="idle","release settles into idle");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(499);p.Tick(1000);
        check(p.Phase=="idle","499ms click never jumps, even after double-click wait");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(500);
        check(p.Phase=="down","500ms mouse-up classified as hold even if timer did not tick");
        p.Tick(1099);check(p.Phase=="down","late-detected hold preserves full descent");p.Tick(1100);check(p.Phase=="release","late-detected hold jumps after descent");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(40);p.Tick(100);
        check(p.Phase=="idle","first short click has no action");p.PrimaryDown(200);p.PrimaryUp(240);
        check(p.Phase=="automatic" && p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"double short click starts dance without descent");
        p.Tick(12239);check(p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"double click preserves all twelve dance loops");
        p.Tick(12240);check(p.Phase=="idle","double click returns to idle after twelve loops");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(20);p.PrimaryDown(370);p.PrimaryUp(400);
        check(p.Phase=="automatic" && p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"350ms inter-click gap accepted");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(20);p.PrimaryDown(371);p.PrimaryUp(400);
        check(p.Phase=="idle","351ms inter-click gap rejected");
        p=Locked(assets);p.PrimaryDown(0,100,100);p.PrimaryUp(20);p.PrimaryDown(200,109,100);p.PrimaryUp(220);
        check(p.Phase=="idle","distant clicks do not count as a double click");
        p=Locked(assets);p.PrimaryDown(0,100,100);p.PrimaryUp(20);p.PrimaryDown(200,108,100);p.PrimaryUp(220);
        check(p.Phase=="automatic" && p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"8 DIP spatial boundary accepted");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(20);p.SecondaryDown(100);p.PrimaryDown(200);p.PrimaryUp(220);
        check(p.Phase=="idle","right click breaks double-click sequence");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(20);p.SetLocked(false,100);p.SetLocked(true,150);p.PrimaryDown(200);p.PrimaryUp(220);
        check(p.Phase=="idle","mode changes break double-click sequence");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(20);p.PrimaryDown(100);p.Tick(599);
        check(p.Phase=="idle","second click that is still held waits for hold threshold");p.Tick(600);p.PrimaryUp(650);p.Tick(1200);
        check(p.Phase=="release","long second press produces one hold gesture");p.Tick(2100);check(p.Phase=="idle","no delayed extra double-click jump");
        p=Locked(assets);p.PrimaryDown(0);p.PrimaryUp(20);p.PrimaryDown(100);p.PrimaryUp(130);p.PrimaryDown(200);p.PrimaryUp(230);p.Tick(730);
        check(p.Phase=="automatic" && p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"third single click preserves the active dance");

        f=new BehaviorFixture(assets,0,60000);p=f.Pet;p.SetLocked(true,0);p.PrimaryDown(0);p.Tick(500);p.Tick(1100);
        check(p.Phase=="hold" && p.CurrentClip.Loop,"held pointer enters crouch loop");
        p.PrimaryUp(1425);check(p.Phase=="hold_return","release from arbitrary hold phase returns naturally");
        long returnedAt=1425+p.CurrentClip.DurationMilliseconds;
        check(p.CurrentClip.DurationMilliseconds<=215 && p.CurrentClip.Frames[p.CurrentClip.Frames.Count-1].AssetPath==f.Clips["click_hold"].Frames[0].AssetPath,"hold return is short and ends at shared anchor");
        p.Tick(returnedAt);check(p.Phase=="release","hold return then jump");
        p=Locked(assets);p.PrimaryDown(0);p.Tick(500);p.CancelPress(550);p.Tick(1099);
        check(p.Phase=="down","capture cancellation preserves descent");p.Tick(1100);check(p.Phase=="release","capture cancellation cannot stick hold");
        p=Locked(assets);p.PrimaryDown(0);p.Tick(500);p.SetLocked(false,550);p.Tick(1100);
        check(p.Phase=="release" && !p.IsLocked,"unlock during descent still ends naturally");

        p=Locked(assets);p.Tick(60000);var original=p.CurrentClip;p.PrimaryDown(61000);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"first press does not interrupt or restart automatic playback");
        p.PrimaryUp(61050);p.Tick(73499);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"single click preserves automatic clip and duration");
        check(p.LastInteraction==61050,"non-preempting single click still resets inactivity");p.Tick(73500);
        check(p.Phase=="idle" && p.NextAutomaticAt==133500,"single click does not postpone natural five-loop completion");
        p=Locked(assets);p.Tick(60000);original=p.CurrentClip;p.PrimaryDown(61000);p.PrimaryUp(61050);p.PrimaryDown(61200);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"second press alone does not preempt before qualification");p.PrimaryUp(61250);
        check(p.Phase=="automatic" && p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"qualified double click preempts immediately with dance");
        p.Tick(73249);check(p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"preempting dance completes all twelve loops");p.Tick(73250);p.Tick(73500);
        check(p.Phase=="idle" && p.NextAutomaticAt==133250,"cancelled batch never resumes at its old deadline");
        p=Locked(assets);p.Tick(60000);original=p.CurrentClip;p.PrimaryDown(61000);p.Tick(61499);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"499ms hold preserves random action");p.Tick(61500);
        check(p.Phase=="down","500ms hold preempts immediately without waiting for automatic completion");
        p.PrimaryUp(61550);p.Tick(62099);check(p.Phase=="down","preemption and early release cannot truncate descent");p.Tick(62100);
        check(p.Phase=="release","preempting long press releases after full descent");
        p=Locked(assets);p.Tick(60000);p.PrimaryDown(73400);p.Tick(73500);p.Tick(73899);
        check(p.Phase=="idle","unfinished hold survives natural automatic completion");p.Tick(73900);
        check(p.Phase=="down","hold across natural batch boundary is recognized exactly once");
        f=new BehaviorFixture(assets,1,60000);p=f.Pet;p.Tick(60000);original=p.CurrentClip;p.SecondaryDown(60100);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original) && p.LastInteraction==60100,"right click records activity without cancelling thinking");p.Tick(77998);
        check(p.Phase=="automatic","right click does not shorten thinking batch");p.Tick(77999);check(p.Phase=="automatic","thinking does not end before its third complete loop");p.Tick(78000);check(p.Phase=="idle","thinking completes exactly three cycles after right click");
        f=new BehaviorFixture(assets,0,60000);p=f.Pet;p.Tick(60000);original=p.CurrentClip;p.SetBusy(true,60100);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"unlocked drag or resize cannot preempt random action");p.SetBusy(false,60200);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"ending layout manipulation does not restart automatic action");p.Tick(73500);
        check(p.Phase=="idle","automatic action finishes normally after manipulation");
        f=new BehaviorFixture(assets,1,60000);p=f.Pet;p.Tick(60000);original=p.CurrentClip;p.Touch(60100);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"menu or tray activity cannot preempt random action");
        p=Locked(assets);p.Tick(60000);original=p.CurrentClip;p.PrimaryDown(61000);p.PrimaryUp(61020);p.SecondaryDown(61100);p.PrimaryDown(61200);p.PrimaryUp(61220);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"right click breaks double-click history without interrupting automatic action");
        p=Locked(assets);p.Tick(60000);p.PrimaryDown(61000);p.PrimaryUp(61500);
        check(p.Phase=="down","500ms release preempts even when timer was delayed");
        f=new BehaviorFixture(assets,0,60000);p=f.Pet;p.Tick(60000);original=p.CurrentClip;p.PrimaryDown(61000);p.PrimaryUp(61020);p.PrimaryDown(61200);p.PrimaryUp(61220);
        check(p.Phase=="automatic" && p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"unlocked double click preempts with dance");original=p.CurrentClip;p.PrimaryDown(62000);p.Tick(62500);p.PrimaryUp(62600);
        check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,original),"unlocked long press cannot preempt dance");
        var clock=new FakeClock();var livePlayer=new AnimationPlayer(clock);string rendered=null;int automaticRequests=0,requests=0;
        p=Locked(assets);p.PlaybackRequested+=delegate(object sender,PlaybackRequestedEventArgs e)
        {requests++;if(e.Clip.Id=="icecream")automaticRequests++;livePlayer.Play(e.Clip);};
        livePlayer.FrameChanged+=delegate(object sender,AnimationFrameChangedEventArgs e){rendered=e.Clip.Id;};
        p.Tick(60000);check(rendered=="icecream","player receives automatic clip");p.PrimaryDown(61000);p.PrimaryUp(61050);p.SecondaryDown(61100);
        check(rendered=="icecream" && requests==1,"single/right click sends no replacement clip to player");p.PrimaryDown(62000);p.Tick(62499);
        check(rendered=="icecream" && requests==1,"player preserves random image before long-press threshold");p.Tick(62500);
        check(rendered=="click_down" && requests==2,"qualified gesture synchronously replaces random clip with interaction");p.PrimaryUp(62510);p.Tick(63100);p.Tick(64000);p.Tick(73500);
        check(rendered=="idle" && automaticRequests==1,"player and behavior cannot replay qualified-gesture-cancelled work");
        f=new BehaviorFixture(assets,1,60000);p=f.Pet;p.Tick(290000);
        check(p.Phase=="automatic" && p.CurrentClip.Id=="think","random batch can cross bedtime after delayed tick");p.Tick(300000);check(p.Phase=="automatic","five-minute sleep waits for the active batch");
        p.Tick(307999);check(p.Phase=="automatic","queued sleep waits until the shorter thinking batch is complete");
        p.Tick(308000);check(p.IsSleeping,"sleep starts when the three-loop batch finishes");
        p.Tick(427999);check(p.IsSleeping,"sleep lasts full two minutes");p.Tick(428000);
        check(p.Phase=="idle" && p.LastInteraction==428000,"natural wake resets inactivity period");
        p=Locked(assets);p.Tick(300000);p.PrimaryDown(300010);p.PrimaryUp(300050);p.PrimaryDown(300100);p.PrimaryUp(300150);
        check(p.Phase=="idle" && !p.IsSleeping,"wake-up click cannot become first half of a double click");
        f=new BehaviorFixture(assets,0,60000);p=f.Pet;p.Tick(300000);p.SecondaryDown(300010);
        check(p.Phase=="idle" && p.LastInteraction==300010,"right click wakes and resets timer");
        f=new BehaviorFixture(assets,0,60000);p=f.Pet;p.Tick(300000);p.SetSleepEnabled(false,300010);
        check(!p.IsSleeping && !p.SleepEnabled,"disabling sleep wakes immediately");p.Tick(600010);check(!p.IsSleeping,"disabled sleep cannot activate");
        f=new BehaviorFixture(assets,0,60000);p=f.Pet;p.Touch(290000);p.Tick(300000);check(!p.IsSleeping,"any mouse interaction resets inactivity");
        f=new BehaviorFixture(assets,0,60000);p=f.Pet;p.SetBusy(true,0);p.Tick(600000);check(p.Phase=="idle","active drag/resize blocks automatic actions and sleep");p.SetBusy(false,600000);
        check(p.NextAutomaticAt==660000 && p.LastInteraction==600000,"ending manipulation restarts 60-90s wait");

        var layout=new PetLayout(100,200);double footX=235,footY=492;
        layout.ResizeFrom(1,0,-120,footX,footY);check(layout.Scale==1.5 && layout.Size==480,"upward drag enlarges");
        check(Math.Abs(layout.Left+layout.Size*135/320-footX)<0.0001 && Math.Abs(layout.Top+layout.Size*292/320-footY)<0.0001,"resizing preserves foot anchor");
        layout.ResizeFrom(1,0,120,footX,footY);check(layout.Scale==0.5,"downward drag shrinks");
        layout.ResizeFrom(1,0,9000,footX,footY);check(layout.Scale==0.4 && layout.Size==128,"lower scale clamp");
        layout.ResizeFrom(1,0,-9000,footX,footY);check(layout.Scale==2 && layout.Size==640,"upper scale clamp");
        layout.IsLocked=true;double x=layout.Left,y=layout.Top;layout.ResizeFrom(1,0,9000,footX,footY);layout.MoveTo(0,0);
        check(layout.Scale==2 && layout.Left==x && layout.Top==y,"lock freezes scale and position");
        layout.IsLocked=false;layout.MoveTo(33,44);check(layout.Left==33 && layout.Top==44,"unlock restores movement");
        bool uiFree=true;
        foreach(var name in typeof(PetBehaviorController).Assembly.GetReferencedAssemblies())
            uiFree &= name.Name!="PresentationCore" && name.Name!="PresentationFramework" && name.Name!="WindowsBase" && name.Name!="System.Windows.Forms";
        check(uiFree,"behavior assembly independent from desktop UI");
    }
}
