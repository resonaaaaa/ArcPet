using System;
using System.Collections.Generic;
using ArcPet.Animation;
using ArcPet.Behavior;

internal sealed class TravelChoiceRandom:IRandomSource
{
    public int Style,StyleDraws;
    public int Next(int min,int max)
    {
        if(min!=0)return 60000;
        if(max==6)return 4; // The existing movement slot in the ordinary pool.
        if(max==2){StyleDraws++;return Style;}
        return max==3 ? 1 : 0;
    }
}
internal static class WalkTravelTests
{
    public static void Run(string assets,Action<bool,string> check)
    {
        var clips=new JsonAnimationCatalog(assets).LoadAll();
        foreach(string id in new[]{"walk_left","walk_right"})
            check(clips[id].Loop && clips[id].Frames.Count==30 && clips[id].DurationMilliseconds==1200 &&
                clips[id].HandoffFrames.Count==2,"walking retains approved thirty-frame timing and double-support turn markers");
        foreach(int style in new[]{0,1})foreach(MovementMode mode in new[]{MovementMode.Long,MovementMode.Short})
        foreach(int direction in new[]{-1,1})
        {
            string action=style==0?"skateboard":"walk";
            var random=new TravelChoiceRandom{Style=style};var pet=new PetBehaviorController(clips,random);
            pet.Start(0);pet.SetMovementMode(mode,0);pet.TravelAvailable=true;
            int requested=0;
            pet.TravelRequested+=delegate(object sender,TravelRequestedEventArgs e)
            {requested++;check(e.Action==action,"both equally sized random outcomes select the corresponding movement artwork");pet.BeginTravel(e.Action,direction,60000);};
            pet.Tick(59999);check(pet.Phase=="idle" && requested==0,"movement waits for the unchanged idle deadline");
            pet.Tick(60000);
            check(pet.IsTraveling && pet.TravelAction==action && pet.CurrentClip.Id==action+(direction>0?"_right":"_left") && requested==1 && random.StyleDraws==1,
                "movement draws its style exactly once after selecting the existing pool slot");
            pet.ChangeTravelDirection(-direction,60050);
            check(pet.TravelAction==action && pet.CurrentClip.Id==action+(direction>0?"_left":"_right") && random.StyleDraws==1,
                "short-trip reversal changes direction without rerolling style");
            var original=pet.CurrentClip;pet.Tick(350000);
            check(pet.IsTraveling && Object.ReferenceEquals(original,pet.CurrentClip),"neither movement style is preempted by the sleep deadline");
            pet.StopTravel(350100);
            check(pet.IsSleeping && pet.CurrentClip.Id=="sleep" && pet.TravelAction==null,"completed movement settles queued sleep directly");

            var motion=new HorizontalTravel(clips[action+"_left"],clips[action+"_right"]);
            var area=new TravelArea(0,3000,100);double origin=direction>0?400:2400;
            check(motion.Start(mode,area,origin,73.5,180,0) && motion.Direction==direction,"both styles choose the farther edge with the same travel controller");
            long time=0;int turns=0;double farthest=0;
            while(motion.IsActive && time<60000)
            {
                time+=20;var step=motion.Update(time,area);farthest=Math.Max(farthest,Math.Abs(step.Left-origin));
                if(step.Top!=73.5 || step.Left<area.MinimumLeft || step.Left>area.MaximumLeft)throw new Exception("walking horizontal/boundary invariant failed");
                if(step.DirectionChanged){turns++;check(clips[action+(direction>0?"_right":"_left")].HandoffFrames.Contains(clips[action+(direction>0?"_right":"_left")].GetFrameIndex(time)),"walking/skating turn only at their own handoff markers");}
            }
            check(!motion.IsActive,"both movement styles finish their complete trip");
            check(mode==MovementMode.Short ? turns==1 && Math.Abs(motion.Left-origin)<.00001 && Math.Abs(farthest-900)<.00001 :
                turns==0 && Math.Abs(motion.Left-(direction>0?area.SafeMaximum:area.SafeMinimum))<.00001,
                "short travel returns from5w, long travel finishes at the safe boundary for either artwork");
        }
        foreach(int style in new[]{0,1})foreach(string input in new[]{"single","double","hold","right","wheel","lock","mode","hide"})
        {
            var random=new TravelChoiceRandom{Style=style};var pet=new PetBehaviorController(clips,random);
            pet.Start(0);pet.SetMovementMode(MovementMode.Short,0);pet.TravelAvailable=true;
            pet.TravelRequested+=delegate(object sender,TravelRequestedEventArgs e){pet.BeginTravel(e.Action,1,60000);};pet.Tick(60000);
            if(input=="single" || input=="double" || input=="hold")pet.PrimaryDown(60100);
            else if(input=="right")pet.SecondaryDown(60100);
            else if(input=="lock")pet.SetLocked(true,60100);
            else if(input=="mode")pet.SetMovementMode(MovementMode.Off,60100);
            else pet.Touch(60100);
            check(pet.Phase=="idle" && pet.TravelAction==null,"any intentional input immediately stops walking and skating");
            pet.PrimaryUp(60120);
            if(input=="double"){pet.PrimaryDown(60200);pet.PrimaryUp(60220);}
            pet.Tick(60700);check(pet.Phase=="idle","movement stop click cannot leak into dance or a queued jump");
        }
        foreach(bool locked in new[]{false,true})foreach(bool available in new[]{false,true})
        foreach(MovementMode mode in new[]{MovementMode.Off,MovementMode.Long,MovementMode.Short})
        {
            var random=new TravelChoiceRandom{Style=1};var pet=new PetBehaviorController(clips,random);int count=0;
            pet.Start(0);pet.SetLocked(locked,0);pet.SetMovementMode(mode,0);pet.TravelAvailable=available;
            pet.TravelRequested+=delegate{count++;};pet.Tick(60000);
            bool eligible=!locked && available && mode!=MovementMode.Off;
            check(count==(eligible?1:0) && random.StyleDraws==(eligible?1:0),"style draw is gated by unlocked state, movement enabled and safe visible space");
        }
    }
}
