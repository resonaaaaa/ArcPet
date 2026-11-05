using System;
using System.Collections.Generic;
using System.IO;
using ArcPet.Animation;

internal sealed class FakeClock : IPlaybackClock
{
    public long ElapsedMilliseconds { get; set; }
    public void Restart() { ElapsedMilliseconds = 0; }
    public void Stop() { }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
    private static AnimationClip Clip(bool loop) { return new AnimationClip("test", loop, new[] { new AnimationFrame("a",70), new AnimationFrame("b",130), new AnimationFrame("c",50) }); }
    public static int Main(string[] args)
    {
        try
        {
            var clip = Clip(true);
            Check(clip.GetFrameIndex(0)==0 && clip.GetFrameIndex(69)==0, "first frame duration");
            Check(clip.GetFrameIndex(70)==1 && clip.GetFrameIndex(199)==1, "variable frame boundary");
            Check(clip.GetFrameIndex(200)==2 && clip.GetFrameIndex(249)==2, "last frame duration");
            Check(clip.GetFrameIndex(250)==0 && clip.GetFrameIndex(570)==1, "loop modulo and delayed update");
            Check(Clip(false).GetFrameIndex(999)==2, "non-loop clamps");
            var clock = new FakeClock(); var player = new AnimationPlayer(clock);
            int changed=0, complete=0; player.FrameChanged += delegate { changed++; }; player.Completed += delegate { complete++; };
            player.Play(clip); Check(changed==1 && player.CurrentFrameIndex==0, "immediate first frame");
            clock.ElapsedMilliseconds=10; player.Update(); Check(changed==1, "unchanged frame not republished");
            clock.ElapsedMilliseconds=210; player.Update(); Check(player.CurrentFrameIndex==2, "delayed tick selects correct frame");
            clock.ElapsedMilliseconds=251; player.Update(); Check(player.CurrentFrameIndex==0 && complete==0, "loop stays active");
            player.Stop(); clock.ElapsedMilliseconds=400; player.Update(); Check(changed==3 && !player.IsPlaying, "stop prevents updates");
            player.Play(Clip(false)); clock.ElapsedMilliseconds=1000; player.Update(); player.Update();
            Check(complete==1 && !player.IsPlaying && player.CurrentFrameIndex==2, "single completion");
            player.Play(clip); Check(player.CurrentFrameIndex==0 && clock.ElapsedMilliseconds==0, "switch restarts timeline");
            bool invalid=false; try { new AnimationFrame("x",0); } catch(ArgumentOutOfRangeException) { invalid=true; }
            Check(invalid, "reject zero duration");
            var loaded = new JsonAnimationCatalog(args[0]).Load("idle");
            Check(loaded.Frames.Count==30 && loaded.DurationMilliseconds==2100 && loaded.Loop, "load real idle metadata");
            invalid=false; try { new JsonAnimationCatalog(args[0]).Load("missing"); } catch(InvalidDataException) { invalid=true; }
            Check(invalid, "unknown clip rejected");
            BehaviorTests.Run(args[0], Check);
            SettingsTests.Run(args[0],args[1],Check);
            SaveQueueTests.Run(Check);
            TravelTests.Run(args[0],args[1],Check);
            WalkTravelTests.Run(args[0],Check);
            StartupTests.Run(Check);
            SleepQueueTests.Run(args[0],Check);
            MoodTests.Run(args[0],Check);
            RocketTests.Run(args[0],Check);
            DanceTests.Run(args[0],Check);
            LyingTests.Run(args[0],Check);
            Console.WriteLine("PASS: " + checks + " animation, behavior, layout and settings checks. Core uses no WPF assemblies."); return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
