using System;
using System.Collections.Generic;
using ArcPet.Animation;
using ArcPet.Behavior;

internal static class PomodoroSceneTests
{
    public static void Run(string assets,Action<bool,string> check)
    {
        var clips=new JsonAnimationCatalog(assets).LoadAll();
        foreach(bool pauseEarly in new[] {false,true})foreach(bool endPaused in new[] {false,true})
        {
            var scene=new PomodoroSceneController(clips);long now=0;AnimationClip current=null;
            var stages=new List<string>();int finished=0;
            scene.PlaybackRequested+=delegate(object sender,PlaybackRequestedEventArgs e) { current=e.Clip;stages.Add(scene.Stage); };
            scene.Finished+=delegate { finished++; };
            scene.Start(now);check(scene.Stage=="start","begin with sitting and taking book");
            if(pauseEarly)scene.SetPaused(true);
            scene.Tick(current.DurationMilliseconds-1);check(scene.Stage=="start","finite start cannot be cut early");
            now+=current.DurationMilliseconds;scene.Tick(now);check(scene.Stage=="read","start joins approved reading");
            scene.SetPaused(true);now+=current.DurationMilliseconds;scene.Tick(now);check(scene.Stage=="pause","pause at reading loop boundary");
            scene.SetPaused(false);scene.SetPaused(true);check(scene.Stage=="pause","rapid controls cannot cut book placement");
            now+=current.DurationMilliseconds;scene.Tick(now);check(scene.Stage=="paused","pause joins animated chin-rest loop");
            if(endPaused)scene.End();else scene.SetPaused(false);
            now+=current.DurationMilliseconds;scene.Tick(now);check(scene.Stage=="resume","paused scene takes book up before read or ending");
            scene.End();now+=current.DurationMilliseconds;scene.Tick(now);check(scene.Stage=="read","resume finishes and reconnects reading");
            now+=current.DurationMilliseconds;scene.Tick(now);check(scene.Stage=="stretch","every end first places book and stretches");
            scene.SetPaused(false);scene.Start(now);check(scene.Stage=="stretch","ending cannot restart or be interrupted");
            now+=current.DurationMilliseconds;scene.Tick(now);check(scene.Stage=="end","stretch joins tomato and sauce scene");
            now+=current.DurationMilliseconds;scene.Tick(now);scene.Tick(now+9999);
            check(!scene.IsActive && finished==1,"finish exactly once after full tomato segment");
        }
        var random=new RandomSource();var pet=new PetBehaviorController(clips,random);pet.Start(0);
        pet.PlayPomodoro(clips["pomodoro_read"],0);pet.PrimaryDown(1);pet.PrimaryUp(2);pet.PrimaryDown(3);pet.PrimaryUp(4);
        pet.SetLocked(true,5);pet.PrimaryDown(6);pet.Tick(400000);pet.PrimaryUp(400001);
        check(pet.IsPomodoro && pet.CurrentClip.Id=="pomodoro_read","reading owns animation over gestures, random and sleep deadlines");
        pet.FinishPomodoro(400002);check(pet.Phase=="idle" && pet.LastInteraction==400002,"completion returns idle with fresh inactivity deadline");
    }
}
