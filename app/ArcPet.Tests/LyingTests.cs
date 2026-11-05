using System;
using System.Collections.Generic;
using ArcPet.Animation;
using ArcPet.Behavior;

internal sealed class SleepVariantRandom : IRandomSource
{
    public int Variant,Choice,Draws;
    public int Next(int min,int max)
    {
        if(min!=0)return 60000;
        if(max==3){Draws++;return Variant;}
        return Choice%max;
    }
}
internal static class LyingTests
{
    private static IDictionary<string,AnimationClip> clips;
    private static PetBehaviorController Create(SleepVariantRandom random,bool locked)
    {var p=new PetBehaviorController(clips,random);p.Start(0);p.SetLocked(locked,0);return p;}
    public static void Run(string assets,Action<bool,string> check)
    {
        clips=new JsonAnimationCatalog(assets).LoadAll();var clip=clips["lying"];
        check(clip.Loop && clip.Frames.Count==36 && clip.DurationMilliseconds==1600,"lying retains accepted36-frame1600ms loop");
        foreach(bool locked in new[]{false,true})foreach(int variant in new[]{0,1,2})
        {
            var r=new SleepVariantRandom{Variant=variant};var p=Create(r,locked);
            p.Tick(300000);string expected=variant==0?"lying":"sleep";
            check(p.IsSleeping && p.Phase=="sleep" && p.CurrentClip.Id==expected,"exactly one of three equally sized choices selects lying in either lock state");
            var original=p.CurrentClip;p.Tick(300500);p.Tick(419999);
            check(Object.ReferenceEquals(original,p.CurrentClip) && p.IsSleeping && r.Draws==1,"sleep drawing remains selected and loops without rerolling for full two minutes");
            p.Tick(420000);check(p.Phase=="idle" && p.LastInteraction==420000 && r.Draws==1,"both sleep drawings wake at the same exact deadline");
            foreach(bool right in new[]{false,true})
            {
                r=new SleepVariantRandom{Variant=variant};p=Create(r,locked);p.Tick(300000);
                if(right)p.SecondaryDown(300100);else{p.PrimaryDown(300100);p.PrimaryUp(300120);}
                check(p.Phase=="idle" && !p.IsSleeping,"first left/right click wakes either drawing in either lock state");
                p.PrimaryDown(300200);p.PrimaryUp(300220);
                check(p.Phase=="idle","wake click is consumed and cannot become the first half of a dance double click");
            }
            r=new SleepVariantRandom{Variant=variant};p=Create(r,locked);p.SetSleepEnabled(false,0);p.Tick(300000);
            check(!p.IsSleeping && r.Draws==0,"disabled sleep never selects lying or original sleep");
            r=new SleepVariantRandom{Variant=variant};p=Create(r,locked);p.Tick(300000);p.SetSleepEnabled(false,300100);
            check(p.Phase=="idle" && !p.SleepEnabled,"disabling sleep wakes either drawing immediately");
        }
        foreach(int choice in new[]{0,1,2,3,4})
        {
            var r=new SleepVariantRandom{Choice=choice};var p=Create(r,true);p.Tick(299000);var original=p.CurrentClip;
            check(p.Phase=="automatic" && original.Id!="lying" && r.Draws==0,"lying is absent from ordinary random-action pool");
            long end=299000+original.DurationMilliseconds;p.Tick(300000);p.Tick(end-1);
            check(Object.ReferenceEquals(original,p.CurrentClip) && r.Draws==0,"sleep variant selection waits for complete ordinary action");
            p.Tick(end);check(p.IsSleeping && p.CurrentClip.Id=="lying" && r.Draws==1,"queued sleep selects lying only on actual entry");
        }
        foreach(MovementMode mode in new[]{MovementMode.Long,MovementMode.Short})
        {
            var r=new SleepVariantRandom{Choice=4};var p=Create(r,false);p.SetMovementMode(mode,0);p.TravelAvailable=true;
            p.TravelRequested+=delegate{p.BeginTravel("skateboard",1,299000);};p.Tick(299000);p.Tick(350000);
            check(p.Phase=="travel" && r.Draws==0,"lying cannot preempt long or short skating at bedtime");
            p.StopTravel(350100);check(p.IsSleeping && p.CurrentClip.Id=="lying" && r.Draws==1,"completed skating enters selected lying sleep directly");
        }
        var mood=new MoodBubbleController(new[]{new MoodContent("fish","C"),new MoodContent("bird","C")},new MoodRandom());
        mood.Enter("idle","idle",2100,0);mood.Tick(20000);check(mood.IsVisible,"idle bubble visible before sleep entry");
        mood.Enter("sleep","lying",1600,20001);mood.Tick(60000);check(!mood.IsVisible,"lying sleep suppresses idle/thinking bubbles");
        var legacy=new Dictionary<string,AnimationClip>(clips);legacy.Remove("lying");var fallback=new PetBehaviorController(legacy,new SleepVariantRandom());fallback.Start(0);fallback.Tick(300000);
        check(fallback.IsSleeping && fallback.CurrentClip.Id=="sleep","catalogues without optional lying asset retain original sleep");
    }
}
