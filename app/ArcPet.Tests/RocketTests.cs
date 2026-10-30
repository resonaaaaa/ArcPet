using System;
using System.Collections.Generic;
using ArcPet.Animation;
using ArcPet.Behavior;

internal static class RocketTests
{
    private static PetBehaviorController Pet(string assets)
    {
        var clips=new JsonAnimationCatalog(assets).LoadAll();
        // Synthetic short clips isolate state boundaries from art availability.
        foreach(string id in new[]{"rocket_charge","rocket_hold","rocket_launch","rocket_flight","rocket_land"})
            clips[id]=new AnimationClip(id,id=="rocket_hold"||id=="rocket_flight",new[]{new AnimationFrame("pale",50),new AnimationFrame("red",50)});
        var p=new PetBehaviorController(clips,new FixedRandom());p.Start(0);p.SetLocked(true,0);return p;
    }
    public static void Run(string assets,Action<bool,string> check)
    {
        var p=Pet(assets);int launches=0;p.RocketRequested+=delegate{launches++;};
        p.PrimaryDown(0);p.Tick(500);p.Tick(1100);p.Tick(1499);
        check(p.Phase=="hold" && launches==0,"rocket does not arm before 1.5 seconds");
        p.Tick(1500);check(p.Phase=="rocket_prepare" && launches==0,"1.5 second hold enters separate crouch bridge");
        p.Tick(1700);check(p.Phase=="rocket_charge" && p.CurrentClip.Id=="rocket_charge","colour ramp is an independent finite crouching animation");
        p.Tick(1799);check(p.Phase=="rocket_charge","colour ramp is not replaced by a static flight frame");
        p.Tick(1800);check(p.Phase=="rocket_hold" && launches==0,"fully charged crouch loops until release");
        p.Tick(9000);check(p.Phase=="rocket_hold" && launches==0,"continued hold never launches automatically");
        p.PrimaryUp(9001);check(p.Phase=="rocket_ready_return" && launches==0,"release returns along adjacent fire-loop keys before launch");
        p.Tick(9200);check(p.Phase=="rocket_launch" && launches==1,"release launches exactly once after its anchor");
        p.Tick(9300);check(p.Phase=="rocket_flight","launch transition completes before flight loop");
        p.Tick(999999);check(p.Phase=="rocket_flight","sleep and random timers cannot interrupt flight");
        p.CompleteRocket(1000000);check(p.Phase=="rocket_settle","motion completion settles the current flight loop first");
        p.Tick(1000300);check(p.Phase=="rocket_land","flight settles to identical landing endpoint");
        p.Tick(1000400);check(p.Phase=="idle" && p.LastInteraction==1000400,"landing completes before idle; inactivity restarts after return");
        p=Pet(assets);p.PrimaryDown(0);p.Tick(500);p.Tick(1100);p.PrimaryUp(1499);
        check(p.Phase=="hold_return","ordinary shorter hold keeps original crouch/jump path");
        p=Pet(assets);launches=0;p.RocketRequested+=delegate{launches++;};p.PrimaryDown(0);p.Tick(500);p.Tick(1100);p.Tick(1500);p.Tick(1700);p.PrimaryUp(1730);
        check(p.Phase=="rocket_charge" && launches==0,"early release preserves full red-ramp transition instead of jumping art");
        p.Tick(1800);check(p.Phase=="rocket_launch" && launches==1,"queued release launches after colour-ramp endpoint");
        p=Pet(assets);p.RocketRequested+=delegate{};p.PrimaryDown(0);p.PrimaryUp(1500);
        check(p.Phase=="down","delayed timer still starts full ordinary descent first");p.Tick(2100);
        check(p.Phase=="rocket_charge","mouse-up timestamp still recognizes rocket despite missing timer ticks");
        p.CancelPress(2120);check(p.Phase=="idle" && !p.IsRocket,"capture cancellation never fires a queued rocket");
        p=Pet(assets);p.SetLocked(false,0);p.PrimaryDown(0);p.Tick(3000);p.PrimaryUp(3001);
        check(p.Phase=="idle","unlocked drag/hold cannot arm rocket");
        foreach(double origin in new[]{12.0,200.0,720.0})
        {
            var f=new VerticalFlight();check(f.Start(333,origin,0,0),"flight starts with finite display coordinates");
            double last=origin;bool bounded=true,horizontal=true,up=true,down=true;FlightStep s=null;
            for(int t=0;t<=1820;t+=20)
            {
                s=f.Update(t,0);bounded&=s.Top>=Math.Min(origin,12)-.0001 && s.Top<=origin+.0001;horizontal&=s.Left==333;
                if(t<900)up&=s.Top<=last+.0001;if(t>1020)down&=s.Top>=last-.0001;last=s.Top;
            }
            check(bounded && horizontal && up && down,"flight remains vertical and bounded across ascent/apex/return");
            check(s.Finished && s.Reason=="returned" && s.Top==origin,"flight returns exact starting coordinate even at screen top");
        }
        var flight=new VerticalFlight();flight.Start(-500,250,-300,0);var step=flight.Update(500,-300);
        check(step.Left==-500 && step.Top>=-288 && step.Top<=250,"negative-coordinate monitor supported");
        step=flight.Update(2000,-300);check(step.Finished && step.Top==250 && step.Reason=="clock-gap","long clock gap safely restores origin");
        flight.Start(10,500,0,0);step=flight.Update(300,50);check(step.Finished && step.Top==500 && step.Reason=="display-changed","display change settles at original position");
        check(!flight.Start(Double.NaN,50,0,0),"invalid coordinate rejected");
    }
}
