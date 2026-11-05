using System;
using System.Collections.Generic;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;

internal sealed class PoolRandom : IRandomSource
{
    public int Choice;
    public int Next(int min,int max){return min==0 ? Choice%max : 60000;}
}
internal static class TravelTests
{
    private static IDictionary<string,AnimationClip> all;
    private static HorizontalTravel Motion(){return new HorizontalTravel(all["skateboard_left"],all["skateboard_right"]);}
    private static PetBehaviorController Pet(int choice,bool locked,MovementMode mode)
    {
        var p=new PetBehaviorController(all,new PoolRandom{Choice=choice});p.Start(0);p.SetLocked(locked,0);p.SetMovementMode(mode,0);p.TravelAvailable=true;return p;
    }
    private static void Trip(Action<bool,string> check,MovementMode mode,TravelArea area,double origin,double width,int expectedDirection)
    {
        var t=Motion();check(t.Start(mode,area,origin,123.5,width,0),"travel starts with valid bounds");
        check(t.Direction==expectedDirection,"direction chooses farther horizontal edge");double maxDistance=0,previous=origin;int turns=0;long now=0;
        while(t.IsActive && now<100000)
        {
            now+=20;var step=t.Update(now,area);maxDistance=Math.Max(maxDistance,Math.Abs(step.Left-origin));
            if(step.Top!=123.5 || step.Left<area.MinimumLeft-.0001 || step.Left>area.MaximumLeft+.0001)throw new Exception("Horizontal/boundary invariant failed");
            if(Math.Abs(step.Left-previous)>Math.Max(12,width*.8)*.02+.0001)throw new Exception("Unexpected travel teleport");
            previous=step.Left;
            if(step.DirectionChanged)
            {
                turns++;var clip=expectedDirection>0 ? all["skateboard_right"] : all["skateboard_left"];
                check(clip.HandoffFrames.Contains(clip.GetFrameIndex(now)),"turn occurs in a shared neutral animation pose");
            }
        }
        check(!t.IsActive,"travel terminates by distance or screen edge");
        if(mode==MovementMode.Short)
        {
            check(Math.Abs(t.Left-origin)<.000001 && turns==1,"short trip reverses once and returns exactly to origin");
            double room=expectedDirection>0 ? area.SafeMaximum-origin : origin-area.SafeMinimum;
            check(Math.Abs(maxDistance-Math.Min(5*width,room))<.00001,"outbound distance is5w or available safe room");
        }
        else check(turns==0 && Math.Abs(t.Left-(expectedDirection>0?area.SafeMaximum:area.SafeMinimum))<.00001,"long trip stops at100px safety boundary without reversing");
    }
    public static void Run(string assets,string scratch,Action<bool,string> check)
    {
        all=new JsonAnimationCatalog(assets).LoadAll();check(all.Count==25,"all twenty-five registered clips load");
        var padded=new TravelArea(0,1600,40,50);
        check(padded.SafeMinimum==40 && padded.SafeMaximum==1550,"asymmetric visible padding retains100px margins while preserving the full restorable window bounds");
        check(all["music"].Frames.Count==32 && all["exercise"].Frames.Count==224,"approved music/exercise frame counts retained");
        check(all["skateboard_left"].HandoffFrames.Count==4 && all["skateboard_right"].HandoffFrames.Count==4,"direction clips expose neutral handoffs independently of desktop");
        foreach(int choice in new[]{2,3})
        {
            string id=choice==2 ? "music" : "exercise";var p=Pet(choice,true,MovementMode.Off);p.Tick(60000);var clip=p.CurrentClip;
            int loops=choice==2 ? 5 : 2;
            check(p.Phase=="automatic" && clip.Id==id && clip.Frames.Count==all[id].Frames.Count*loops && clip.DurationMilliseconds==all[id].DurationMilliseconds*loops,"music plays five complete loops and exercise plays two");
            int requests=0;p.PlaybackRequested+=delegate{requests++;};p.PrimaryDown(60100);p.PrimaryUp(60120);p.SecondaryDown(60200);p.SetBusy(true,60300);p.SetBusy(false,60400);
            check(p.Phase=="automatic" && Object.ReferenceEquals(p.CurrentClip,clip) && requests==0,"new ordinary action ignores single/right/layout inputs without restart");
            p.PrimaryDown(61000);p.PrimaryUp(61030);p.PrimaryDown(61100);p.PrimaryUp(61130);
            check(p.Phase=="automatic" && p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && requests==1,"locked double click directly preempts new ordinary action with dance");
            p=Pet(choice,true,MovementMode.Off);p.Tick(60000);p.PrimaryDown(60100);p.Tick(60599);check(p.Phase=="automatic","new ordinary action continues before hold threshold");p.Tick(60600);
            check(p.Phase=="down","locked500ms hold preempts new ordinary action");
            p=Pet(choice,false,MovementMode.Long);p.TravelAvailable=false;p.Tick(60000);clip=p.CurrentClip;p.PrimaryDown(60100);p.Tick(60700);p.PrimaryUp(60710);
            check(p.Phase=="automatic" && Object.ReferenceEquals(clip,p.CurrentClip),"unlocked hold cannot preempt new ordinary action");
            p=Pet(choice,false,MovementMode.Off);p.Tick(60000);long end=60000+p.CurrentClip.DurationMilliseconds;p.Tick(end-1);check(p.Phase=="automatic","new action never ends before its configured complete loops");p.Tick(end);
            check(p.Phase=="idle" && p.NextAutomaticAt==end+60000,"new ordinary action returns to normal randomized idle schedule");
        }
        foreach(bool locked in new[]{false,true})foreach(MovementMode mode in new[]{MovementMode.Off,MovementMode.Long,MovementMode.Short})
        {
            var p=Pet(4,locked,mode);int requests=0;p.TravelRequested+=delegate{requests++;p.BeginTravel("skateboard",1,60000);};p.Tick(60000);
            bool eligible=!locked && mode!=MovementMode.Off;
            check((p.Phase=="travel")==eligible && requests==(eligible?1:0),"skate eligibility requires enabled movement AND unlocked state");
        }
        foreach(string input in new[]{"single","double","hold","right","wheel","drag","lock","hide","mode"})
        {
            var p=Pet(4,false,MovementMode.Short);p.TravelRequested+=delegate{p.BeginTravel("skateboard",-1,60000);};p.Tick(60000);
            check(p.Phase=="travel" && p.CurrentClip.Id=="skateboard_left","skate request uses chosen directional clip");
            if(input=="single" || input=="double" || input=="hold")p.PrimaryDown(60100);
            else if(input=="right")p.SecondaryDown(60100);
            else if(input=="drag")p.SetBusy(true,60100);
            else if(input=="lock")p.SetLocked(true,60100);
            else if(input=="mode")p.SetMovementMode(MovementMode.Off,60100);
            else p.Touch(60100);
            check(p.Phase=="idle","any intentional mouse/menu input stops only skate immediately");p.PrimaryUp(60110);p.Tick(60610);
            check(p.Phase=="idle","stopping skate does not leak into a queued crouch/jump");
        }
        var nearBed=Pet(3,true,MovementMode.Off);nearBed.Tick(290000);
        check(nearBed.Phase=="automatic" && nearBed.CurrentClip.Id=="exercise","sleep deadline no longer excludes long random batches");
        var asleep=Pet(4,false,MovementMode.Long);asleep.TravelRequested+=delegate{asleep.BeginTravel("skateboard",1,60000);};asleep.Tick(60000);asleep.Tick(300000);
        check(asleep.Phase=="travel","five minute inactivity queues sleep behind prolonged travel");
        Trip(check,MovementMode.Short,new TravelArea(0,4000,100),800,200,1);
        Trip(check,MovementMode.Short,new TravelArea(-4000,0,100),-800,200,-1);
        Trip(check,MovementMode.Short,new TravelArea(0,800,100),400,200,1);
        Trip(check,MovementMode.Short,new TravelArea(0,1600,100),20,120,1);
        Trip(check,MovementMode.Long,new TravelArea(0,1920,100),1700,200,-1);
        Trip(check,MovementMode.Long,new TravelArea(-1920,-320,100),-1800,200,1);
        Trip(check,MovementMode.Short,new TravelArea(0,1400,100),1100,400,-1);
        check(!HorizontalTravel.CanStart(new TravelArea(2,2,100),2,200) && !HorizontalTravel.CanStart(new TravelArea(0,100,100),Double.NaN,200),"impossible bounds/nonfinite positions cannot start travel");
        var t=Motion();check(!t.Start(MovementMode.Off,new TravelArea(0,1000,100),100,0,200,0),"off mode never starts horizontal movement");
        t.Start(MovementMode.Long,new TravelArea(0,2000,100),100,42,200,0);t.Update(100,new TravelArea(0,2000,100));double last=t.Left;
        var step=t.Update(1200,new TravelArea(0,2000,100));check(step.Finished && step.Left==last && step.Top==42,"suspended clock stops in place instead of teleporting");
        t.Start(MovementMode.Short,new TravelArea(0,2000,100),300,42,200,0);step=t.Update(100,new TravelArea(0,350,100));
        check(step.Left<=350 && step.Top==42,"dynamic short-trip boundary is checked during movement");
        t.Start(MovementMode.Long,new TravelArea(0,2000,100),100,42,200,0);step=t.Update(100,null);check(step.Finished && step.Left==100,"removed display stops travel safely");
        t.Start(MovementMode.Short,new TravelArea(0,4000,100),800,42,200,0);long time=0;
        while(t.IsActive && t.Stage!="return" && time<12000){time+=20;t.Update(time,new TravelArea(0,4000,100));}
        check(t.Stage=="return","short trip reaches its returning stage");
        while(t.IsActive && time<20000){time+=20;step=t.Update(time,new TravelArea(1000,4000,100));}
        check(!t.IsActive && t.Left==1000 && t.Top==42,"changed return-side boundary stops safely instead of crossing toward an unreachable origin");
        var pref=new PetPreferences{Movement="short"};string error;var store=new JsonPreferencesStore(System.IO.Path.Combine(scratch,"movement-test/settings.json"));
        check(store.TrySave(pref,out error) && new JsonPreferencesStore(store.FilePath).Load().Movement=="short","movement mode survives persisted restart");
        check(new PetPreferences().Movement=="off" && new PetPreferences{Movement="unknown"}.Normalize().Movement=="off","old and invalid preferences default movement to off");
    }
}
