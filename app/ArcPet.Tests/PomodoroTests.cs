using System;
using ArcPet.Behavior;

internal static class PomodoroTests
{
    public static void Run(Action<bool,string> check)
    {
        var timer = new PomodoroTimer(); int completed = 0;
        timer.Completed += delegate { completed++; };
        foreach (int minutes in new[] {20,30,40,50,60})
        {
            timer.End();
            check(timer.Start(minutes,100) && timer.RemainingMilliseconds(100)==minutes*60000L,"preset duration");
            check(!timer.Start(20,101) && timer.Minutes==minutes,"active session cannot be overwritten");
            timer.Tick(100+minutes*60000L-1);
            check(timer.State==PomodoroState.Running,"not complete early");
            int before=completed;
            timer.Tick(100+minutes*60000L+5000);timer.Tick(100+minutes*60000L+9000);
            check(timer.State==PomodoroState.Completed && completed==before+1,"delayed update completes exactly once");
        }
        timer.Start(20,0);timer.Pause(12345);long remaining=timer.RemainingMilliseconds(9999999);
        timer.Pause(9999999);timer.Tick(9999999);
        check(remaining==1200000-12345 && timer.State==PomodoroState.Paused,"pause freezes exact remaining time");
        timer.Resume(10000000);timer.Resume(10000001);
        check(timer.RemainingMilliseconds(10000001)==remaining-1,"resume preserves elapsed progress");
        timer.End();int count=completed;timer.Tick(99999999);timer.Resume(99999999);
        check(!timer.IsActive && timer.State==PomodoroState.Idle && completed==count,"manual end cancels deadline without completion");
        timer.Start(20,0);timer.Pause(1200000);
        check(timer.State==PomodoroState.Completed && completed==count+1,"pause at deadline completes instead of pausing zero");
        bool invalid=false;try{timer.End();timer.Start(25,0);}catch(ArgumentOutOfRangeException){invalid=true;}
        check(invalid,"unsupported preset rejected");
    }
}
