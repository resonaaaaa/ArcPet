using System;
using ArcPet.Animation;
using ArcPet.Behavior;

internal static class DanceTests
{
    private static void Double(PetBehaviorController p,long t)
    {p.PrimaryDown(t,100,100);p.PrimaryUp(t+20);p.PrimaryDown(t+150,100,100);p.PrimaryUp(t+180);}
    public static void Run(string assets,Action<bool,string> check)
    {
        var clips=new JsonAnimationCatalog(assets).LoadAll();var dance=clips["dance"];
        check(dance.Frames.Count==30 && dance.DurationMilliseconds==1000 && dance.Loop,"dance is exactly1.2x the1200ms source");
        check(dance.Frames[0].DurationMilliseconds==250 && dance.Frames[15].DurationMilliseconds==250,"speed-up preserves endpoint rhythm at250ms");
        var mirror=clips["dance_mirrored"];
        check(mirror.Frames.Count==30 && mirror.DurationMilliseconds==1000 && mirror.Loop,"mirror preserves frame count and loop duration");
        for(int i=0;i<30;i++)check(mirror.Frames[i].DurationMilliseconds==dance.Frames[i].DurationMilliseconds,"mirror preserves each frame boundary");
        foreach(bool locked in new[]{false,true})foreach(int facing in new[]{0,1})
        {
            var q=new PetBehaviorController(clips,new PoolRandom{Choice=facing});q.Start(0);q.SetLocked(locked,0);Double(q,100);
            check(q.CurrentClip.Id==(facing==0?"dance":"dance_mirrored"),"both random facings reachable in both lock states");
            check(q.CurrentClip.DurationMilliseconds==12000 && q.CurrentClip.Frames.Count==360,"both facings play twelve complete loops");
            for(int loop=0;loop<12;loop++)
            {
                var expected=clips[((facing+loop/3)%2)==0?"dance":"dance_mirrored"];
                bool same=true;
                for(int i=0;i<30;i++)same &= q.CurrentClip.Frames[loop*30+i].AssetPath==expected.Frames[i].AssetPath && q.CurrentClip.Frames[loop*30+i].DurationMilliseconds==expected.Frames[i].DurationMilliseconds;
                check(same,"three complete loops per facing, exact frame order and rhythm in either lock state");
            }
            foreach(int boundary in new[]{3000,6000,9000})
            {
                var before=q.CurrentClip.Frames[q.CurrentClip.GetFrameIndex(boundary-1)];
                var after=q.CurrentClip.Frames[q.CurrentClip.GetFrameIndex(boundary)];
                string next=((facing+boundary/3000)%2)==0?"dance":"dance_mirrored";
                string previous=next=="dance"?"dance_mirrored":"dance";
                check(before.AssetPath==clips[previous].Frames[29].AssetPath,"facing never switches before completing the third loop");
                check(after.AssetPath==clips[next].Frames[0].AssetPath,"facing switches at exact three-loop boundary without skipping a frame");
            }
        }
        var alternating=new AlternatingDanceRandom();var repeat=new PetBehaviorController(clips,alternating);repeat.Start(0);
        Double(repeat,100);string firstFacing=repeat.CurrentClip.Id;Double(repeat,1000);
        check(repeat.CurrentClip.Id!=firstFacing && alternating.FacingDraws==2,"each qualified double click draws a fresh facing");
        foreach(bool locked in new[]{false,true})
        foreach(int choice in new[]{-1,0,1,2,3,4})
        {
            var p=new PetBehaviorController(clips,new PoolRandom{Choice=Math.Max(0,choice)});p.Start(0);p.SetLocked(locked,0);
            long t=choice<0?100:60100;if(choice>=0)p.Tick(60000);
            var original=p.CurrentClip;int requests=0;p.PlaybackRequested+=delegate{requests++;};
            p.PrimaryDown(t);p.PrimaryUp(t+20);p.Tick(t+100);
            check(Object.ReferenceEquals(original,p.CurrentClip) && requests==0,"first click never pauses or replaces current animation in either lock state");
            p.PrimaryDown(t+150);check(Object.ReferenceEquals(original,p.CurrentClip),"second down waits for a completed short click");p.PrimaryUp(t+180);
            check(p.Phase=="automatic" && p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && requests==1,"double click directly dances from idle/every random action in either lock state");
            check(!p.CurrentClip.Loop && p.CurrentClip.Frames.Count==360 && p.CurrentClip.DurationMilliseconds==12000,"dance batch is finite and contains exactly twelve complete loops");
            var batch=p.CurrentClip;p.PrimaryDown(t+600);p.PrimaryUp(t+620);p.SecondaryDown(t+700);p.SetBusy(true,t+800);p.SetBusy(false,t+900);
            check(Object.ReferenceEquals(batch,p.CurrentClip) && requests==1,"single/right/layout activity does not interrupt or restart dance");
            p.Tick(t+12179);check(Object.ReferenceEquals(batch,p.CurrentClip),"dance cannot finish one millisecond before twelve loops");
            p.Tick(t+12180);check(p.Phase=="idle" && requests==2,"dance completes exactly once without resuming cancelled random work");
        }
        foreach(bool locked in new[]{false,true})
        {
            var p=new PetBehaviorController(clips,new PoolRandom());p.Start(0);p.SetLocked(locked,0);Double(p,100);var first=p.CurrentClip;
            Double(p,1000);check(p.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && !Object.ReferenceEquals(first,p.CurrentClip),"double click restarts a fresh twelve-loop dance instead of queueing");
            var current=p.CurrentClip;p.PrimaryDown(2000);p.Tick(2499);check(Object.ReferenceEquals(current,p.CurrentClip),"dance continues before long-press qualification");
            p.Tick(2500);check(locked?p.Phase=="down":Object.ReferenceEquals(current,p.CurrentClip),"only locked500ms hold interrupts dance for original crouch interaction");
            p.PrimaryUp(2550);
            if(locked){p.Tick(3100);p.Tick(4000);p.Tick(14000);check(p.Phase=="idle","long-press interruption does not resume old dance");}
            else {p.Tick(13180);check(p.Phase=="idle","unlocked long hold does not extend the dance deadline");}
        }
        var sleep=new PetBehaviorController(clips,new PoolRandom());sleep.Start(0);Double(sleep,100);
        typeof(PetBehaviorController).GetProperty("LastInteraction").SetValue(sleep,-299000L,null);
        sleep.Tick(1000);check(sleep.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && !sleep.IsSleeping,"overdue sleep waits for current dance");
        sleep.Tick(12280);check(sleep.IsSleeping,"sleep is evaluated only after the twelfth dance loop");
        var cancelled=new PetBehaviorController(clips,new PoolRandom());cancelled.Start(0);
        cancelled.PrimaryDown(100);cancelled.PrimaryUp(120);cancelled.PrimaryDown(200);cancelled.CancelPress(220);cancelled.PrimaryUp(240);
        check(cancelled.Phase=="idle","drag/capture cancellation removes double-click candidate");
        cancelled.PrimaryDown(300);cancelled.PrimaryUp(320);check(cancelled.Phase=="idle","click after cancelled drag cannot reuse previous tap history");
    }
}
internal sealed class AlternatingDanceRandom:IRandomSource
{
    public int FacingDraws;
    public int Next(int min,int max){return min==0?FacingDraws++%max:60000;}
}
