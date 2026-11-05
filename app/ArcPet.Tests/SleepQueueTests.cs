using System;
using System.Collections.Generic;
using ArcPet.Animation;
using ArcPet.Behavior;

internal static class SleepQueueTests
{
    private static IDictionary<string,AnimationClip> clips;
    private static PetBehaviorController Create(int choice,MovementMode mode)
    {
        var pet=new PetBehaviorController(clips,new PoolRandom{Choice=choice});pet.Start(0);
        pet.SetMovementMode(mode,0);pet.TravelAvailable=true;return pet;
    }
    public static void Run(string assets,Action<bool,string> check)
    {
        clips=new JsonAnimationCatalog(assets).LoadAll();
        foreach(MovementMode mode in new[]{MovementMode.Long,MovementMode.Short})
        foreach(int direction in new[]{-1,1})
        {
            var pet=Create(4,mode);pet.TravelRequested+=delegate{pet.BeginTravel("skateboard",direction,290000);};
            pet.Tick(290000);var original=pet.CurrentClip;int requests=0;pet.PlaybackRequested+=delegate{requests++;};
            pet.Tick(300000);pet.Tick(310000);
            check(pet.Phase=="travel" && Object.ReferenceEquals(original,pet.CurrentClip) && requests==0,"sleep waits for active long/short skating in either direction");
            pet.StopTravel(310100);
            check(pet.IsSleeping && requests==1,"completed skating enters queued sleep directly without idle or another random action");
            pet.Tick(430099);check(pet.IsSleeping,"queued sleep still lasts its complete two minutes");
            pet.Tick(430100);check(pet.Phase=="idle" && pet.LastInteraction==430100,"queued sleep naturally wakes with a fresh inactivity period");
        }
        foreach(int choice in new[]{0,1,2,3,4})
        {
            string id=new[]{"icecream","think","music","exercise","calling"}[choice];
            var pet=Create(choice,MovementMode.Off);pet.Tick(299000);var original=pet.CurrentClip;
            int loops=new[]{5,3,5,2,5}[choice];
            long end=299000+clips[id].DurationMilliseconds*loops;
            check(pet.Phase=="automatic" && original.Id==id && original.Frames.Count==clips[id].Frames.Count*loops,"near bedtime every ordinary action starts its configured complete loops");
            var transitions=new List<string>();pet.PlaybackRequested+=delegate{transitions.Add(pet.Phase);};
            pet.Tick(300000);pet.Tick(end-1);
            check(pet.Phase=="automatic" && Object.ReferenceEquals(original,pet.CurrentClip) && transitions.Count==0,"sleep deadline cannot interrupt or restart any ordinary action");
            pet.Tick(end);
            check(pet.IsSleeping && transitions.Count==1 && transitions[0]=="sleep","configured final loop completion goes straight into queued sleep");
            pet.Tick(end+1);check(pet.IsSleeping && transitions.Count==1,"queued sleep is entered only once");
            pet.Tick(end+120000);check(pet.Phase=="idle" && pet.LastInteraction==end+120000,"ordinary-action queued sleep uses completion time as its own start");

            pet=Create(choice,MovementMode.Off);pet.Tick(299000);pet.Tick(300000);original=pet.CurrentClip;
            pet.SecondaryDown(300100);pet.Tick(end);
            check(pet.Phase=="idle" && pet.LastInteraction==300100 && !pet.IsSleeping,"right click cancels outdated sleep eligibility without interrupting ordinary action");
            pet.Tick(600100);check(pet.IsSleeping,"after activity sleep uses the new inactivity deadline");

            pet=Create(choice,MovementMode.Off);pet.Tick(299000);pet.SetSleepEnabled(false,300100);pet.Tick(end);
            check(pet.Phase=="idle" && !pet.SleepEnabled,"disabling sleep removes deferred sleep at ordinary completion");

            pet=Create(choice,MovementMode.Off);pet.SetLocked(true,0);pet.Tick(299000);pet.Tick(300000);
            pet.PrimaryDown(300100);pet.PrimaryUp(300120);pet.PrimaryDown(300200);pet.PrimaryUp(300220);
            check(pet.Phase=="automatic" && pet.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"qualified double click still preempts ordinary action after the sleep deadline");
            pet.Tick(312219);check(pet.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"dance retains twelve loops after cancelling stale sleep");pet.Tick(312220);
            check(pet.Phase=="idle" && !pet.IsSleeping,"dance clears outdated queued sleep rather than resuming old random work");
        }
        foreach(string input in new[]{"left","right","wheel","mode","sleep-off"})
        {
            var pet=Create(4,MovementMode.Long);pet.TravelRequested+=delegate{pet.BeginTravel("skateboard",1,290000);};pet.Tick(290000);pet.Tick(300000);
            if(input=="left")pet.PrimaryDown(300100);
            else if(input=="right")pet.SecondaryDown(300100);
            else if(input=="mode")pet.SetMovementMode(MovementMode.Off,300100);
            else if(input=="sleep-off")pet.SetSleepEnabled(false,300100);
            else pet.Touch(300100);
            check(pet.Phase=="idle" && !pet.IsSleeping && pet.LastInteraction==300100,"user interruption of overdue travel stops in idle and cancels stale sleep");
        }
        var idle=Create(0,MovementMode.Off);idle.Tick(300000);
        check(idle.IsSleeping,"idle sleep still takes priority over starting another random action");
        var disabled=Create(3,MovementMode.Off);disabled.SetSleepEnabled(false,0);disabled.Tick(299000);disabled.Tick(400000);
        check(disabled.Phase=="idle" && !disabled.IsSleeping,"completed random action never queues sleep when disabled");

        var busy=Create(3,MovementMode.Off);busy.Tick(299000);busy.SetBusy(true,300100);busy.Tick(700000);
        check(busy.Phase=="idle" && !busy.IsSleeping,"active drag still prevents queued sleep on action completion");
        busy.SetBusy(false,700000);busy.Tick(700001);check(!busy.IsSleeping,"ending drag resets eligibility rather than entering stale queued sleep");
    }
}
