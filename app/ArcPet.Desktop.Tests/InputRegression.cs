using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;

internal sealed class SlowPreferences : IPreferencesStore
{
    public int Writes,UiWrites;
    private readonly int uiThread=Thread.CurrentThread.ManagedThreadId;
    public string FilePath {get{return "memory-only-test-profile.json";}}
    public string LoadStatus {get{return "loaded";}}
    public PetPreferences Load(){return new PetPreferences{Locked=true,SleepEnabled=false,Left=80,Top=80};}
    public bool TrySave(PetPreferences value,out string error)
    {
        Interlocked.Increment(ref Writes);
        if(Thread.CurrentThread.ManagedThreadId==uiThread)Interlocked.Increment(ref UiWrites);
        Thread.Sleep(450);error=null;return true;
    }
}
internal sealed class FixedRandom : IRandomSource
{
    public int Choice;
    public int Next(int min,int max){return min==0 ? Choice : 60000;}
}
internal static class InputRegression
{
    private static Window window;
    private static Type type;
    private static Image sprite;
    private static PetBehaviorController behavior;
    private static AnimationPlayer player;
    private static SlowPreferences store;
    private static int frameEvents,requests,checks;
    private static readonly List<string> log=new List<string>();
    private static object Call(string name,params object[] args){return type.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(window,args);}
    private static long Now {get{return (long)type.GetProperty("Now",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window,null);}}
    private static void Pump(int milliseconds)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};
        timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    private static void Check(bool value,string name){checks++;log.Add((value?"PASS:":"FAIL:")+name);if(!value)throw new Exception(name);}
    private static void MouseEvent(bool down,MouseButton button)
    {
        var e=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,button);
        e.RoutedEvent=down ? Mouse.PreviewMouseDownEvent : Mouse.PreviewMouseUpEvent;sprite.RaiseEvent(e);
        if(!e.Handled){e.RoutedEvent=down ? Mouse.MouseDownEvent : Mouse.MouseUpEvent;sprite.RaiseEvent(e);}
    }
    private static long Click()
    {
        if(behavior.IsLocked)MouseEvent(true,MouseButton.Left);else Call("BeginPrimaryGesture",new Point(100,100),false);
        Pump(30);var watch=Stopwatch.StartNew();if(behavior.IsLocked)MouseEvent(false,MouseButton.Left);else Call("EndGesture",false);return watch.ElapsedMilliseconds;
    }
    private static void FinishDance()
    {
        Check(behavior.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && behavior.CurrentClip.DurationMilliseconds==12000,"routed dance uses a complete twelve-second batch");
        long end=(long)typeof(PetBehaviorController).GetField("phaseStarted",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(behavior)+behavior.CurrentClip.DurationMilliseconds;
        var offset=type.GetField("diagnosticTimeOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        offset.SetValue(window,(long)offset.GetValue(window)+Math.Max(0,end-Now));Call("OnTick",null,EventArgs.Empty);
    }
    private static void Automatic(int choice,bool locked)
    {
        window.ContextMenu.IsOpen=false;Pump(20);
        behavior.CancelPress(Now);
        if(behavior.IsLocked!=locked)Call("ToggleLock");else behavior.SetLocked(locked,Now);
        Call("SyncMenus");
        behavior.Start(Now);var f=type.GetField("diagnosticTimeOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        f.SetValue(window,(long)f.GetValue(window)+60001);behavior.Tick(Now);Pump(120);
        Check(behavior.Phase=="automatic","random clip starts for routed-input case");
        Check(behavior.CurrentClip.Id==new[]{"icecream","think","music","exercise","calling"}[choice],"expected random clip selected");
    }
    private static void StartTravel(FixedRandom random,MovementMode mode)
    {
        window.ContextMenu.IsOpen=false;Pump(20);
        behavior.Touch(Now);behavior.CancelPress(Now);
        if(behavior.IsLocked)Call("ToggleLock");
        Call("SetMovementMode",mode);behavior.SetSleepEnabled(false,Now);behavior.Start(Now);random.Choice=4;
        Call("RefreshTravelAvailability");
        var offset=type.GetField("diagnosticTimeOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        offset.SetValue(window,(long)offset.GetValue(window)+60001);behavior.Tick(Now);Pump(80);
        Check(behavior.Phase=="skate","eligible random selection starts actual sliding window");
    }
    [STAThread]
    public static int Main(string[] args)
    {
        bool baseline=args.Length>2 && args[2]=="--baseline";string output=args[1];Directory.CreateDirectory(output);
        try
        {
            string executable=Path.GetFullPath(args[0]);var assembly=Assembly.LoadFrom(executable);type=assembly.GetType("ArcPet.Desktop.PetWindow",true);
            var catalog=new JsonAnimationCatalog(Path.Combine(Path.GetDirectoryName(executable),"assets"));var clips=catalog.LoadAll();
            var random=new FixedRandom();behavior=new PetBehaviorController(clips,random);player=new AnimationPlayer(new MonotonicPlaybackClock());store=new SlowPreferences();
            player.FrameChanged+=delegate{frameEvents++;};behavior.PlaybackRequested+=delegate{requests++;};
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            window=(Window)Activator.CreateInstance(type,new object[]{player,behavior,clips,store,null,null});sprite=(Image)window.Content;Call("Start");Pump(200);
            Automatic(0,true);var original=behavior.CurrentClip;int before=requests;long elapsed=Click();Pump(80);long second=Click();
            if(baseline)
            {
                File.WriteAllText(Path.Combine(output,"routed-input-baseline.json"),String.Format("{{\"single_release_ms\":{0},\"second_release_ms\":{1},\"settings_writes\":{2},\"ui_thread_writes\":{3},\"double_click_phase\":\"{4}\",\"expected_phase\":\"down\"}}",elapsed,second,store.Writes,store.UiWrites,behavior.Phase));
                Console.WriteLine("Baseline: release="+elapsed+"ms; second="+second+"ms; phase="+behavior.Phase+"; UI writes="+store.UiWrites);return 0;
            }
            Check(elapsed<150 && second<150,"mouse releases do not block on slow settings storage");
            Check(behavior.Phase=="automatic" && behavior.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && requests==before+1,"routed double click directly preempts random clip with dance");
            Check(store.Writes==0,"locked clicks do not save unchanged preferences");
            Pump(650);Check(behavior.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"double click keeps dancing without entering crouch or release");FinishDance();
            Check(behavior.Phase=="idle","double click completes interaction without replaying random action");
            for(int choice=0;choice<2;choice++)
            {
                random.Choice=choice;
                foreach(bool locked in new[]{true,false})
                {
                    Automatic(choice,locked);original=behavior.CurrentClip;before=requests;int images=frameEvents;
                    Click();Pump(200);
                    Check(behavior.Phase=="automatic" && Object.ReferenceEquals(original,behavior.CurrentClip) && requests==before,"single click preserves random clip in both lock states");
                    Check(frameEvents>images+1 && player.IsPlaying,"random frames continue during single/double-click recognition window");
                    MouseEvent(true,MouseButton.Right);MouseEvent(false,MouseButton.Right);Pump(120);
                    Check(behavior.Phase=="automatic" && requests==before,"routed right click does not replace random playback");
                    window.ContextMenu.IsOpen=false;Pump(20);
                    if(!locked)
                    {
                        // A routed event cannot change the physical Mouse.LeftButton state.
                        // Drive the production movement adapter without native capture here.
                        Call("BeginPrimaryGesture",new Point(100,100),false);
                        Check((string)type.GetField("gesture",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window)=="drag","unlocked pointer-down starts the production drag adapter");
                        var start=(Point)type.GetField("initialPointer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                        double oldLeft=window.Left,oldTop=window.Top;images=frameEvents;
                        Call("UpdatePointerPosition",new Point(start.X+18,start.Y+12));Pump(650);
                        Check(window.Left!=oldLeft && window.Top!=oldTop,"unlocked drag updates actual window layout");
                        Check(frameEvents>images && behavior.Phase=="automatic","random frames advance while unlocked window moves");
                        MouseEvent(false,MouseButton.Left);
                        Check(behavior.Phase=="automatic" && requests==before,"unlocked hold does not pause or preempt random action");
                        Click();Pump(70);Click();Check(behavior.Phase=="automatic" && behavior.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && requests==before+1,"unlocked double click preempts directly with dance");FinishDance();
                    }
                    else
                    {
                        MouseEvent(true,MouseButton.Left);Pump(200);Check(behavior.Phase=="automatic","locked hold shorter than500ms preserves playback");Pump(330);
                        Check(behavior.Phase=="down","routed500ms hold immediately preempts random action");MouseEvent(false,MouseButton.Left);Pump(650);Pump(950);
                    }
                }
            }
            random.Choice=1;Automatic(1,true);before=requests;int framesBefore=frameEvents;
            Call("ToggleSleep");Pump(100);Click();Pump(70);Click();
            Check(behavior.Phase=="automatic" && behavior.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && requests==before+1,"double click still qualifies while slow settings write runs");
            Check(store.UiWrites==0 && store.Writes>0,"actual changed settings save outside animation thread");
            Check(frameEvents>framesBefore,"animation continues during background persistence");
            FinishDance();
            if(clips.ContainsKey("skateboard_left"))
            {
                foreach(MouseButton button in new[]{MouseButton.Left,MouseButton.Right,MouseButton.Middle,MouseButton.XButton1})
                {
                    StartTravel(random,MovementMode.Long);double y=window.Top,x=window.Left;int movingImages=frameEvents;Pump(120);
                    Check(window.Left!=x && window.Top==y && frameEvents>movingImages,"live sliding changes only horizontal position while frames advance");
                    MouseEvent(true,button);x=window.Left;
                    Check(behavior.Phase=="idle","any routed mouse button immediately stops sliding");Pump(120);MouseEvent(false,button);
                    Check(Math.Abs(window.Left-x)<.001 && window.Top==y,"stopped travel remains at current location");
                }
                StartTravel(random,MovementMode.Long);
                sprite.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=Mouse.PreviewMouseWheelEvent});
                Check(behavior.Phase=="idle","mouse wheel also stops sliding without affecting ordinary-action policy");
                StartTravel(random,MovementMode.Short);MouseEvent(true,MouseButton.Left);double stoppedX=window.Left;Pump(600);MouseEvent(false,MouseButton.Left);
                Check(behavior.Phase=="idle" && window.Left==stoppedX,"hold interrupts travel on initial press rather than starting a queued jump");
                StartTravel(random,MovementMode.Short);Click();Pump(70);Click();
                Check(behavior.Phase=="idle","double click during unlocked sliding only stops travel");
                StartTravel(random,MovementMode.Long);Call("ToggleLock");stoppedX=window.Left;Pump(80);
                Check(behavior.IsLocked && behavior.Phase=="idle" && window.Left==stoppedX,"locking a moving pet stops travel in place");
                foreach(MovementMode mode in new[]{MovementMode.Long,MovementMode.Short,MovementMode.Off})
                {
                    Call("SetMovementMode",mode);
                    var menus=(Dictionary<MovementMode,MenuItem>)type.GetField("movementMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                    var tray=(System.Collections.IDictionary)type.GetField("trayMovement",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);bool good=true;
                    foreach(MovementMode candidate in new[]{MovementMode.Long,MovementMode.Short,MovementMode.Off})
                    {
                        object item=tray[candidate];bool marked=(bool)item.GetType().GetProperty("Checked").GetValue(item,null);
                        good &= menus[candidate].IsChecked==(candidate==mode) && marked==(candidate==mode);
                    }
                    Check(good,"desktop/tray movement options are mutually exclusive and synchronized");
                }
                StartTravel(random,MovementMode.Short);
                object travel=type.GetField("travel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                double origin=(double)travel.GetType().GetProperty("OriginLeft").GetValue(travel,null),top=window.Top;
                string firstDirection=behavior.CurrentClip.Id;bool turned=false,horizontal=true;var trip=Stopwatch.StartNew();
                while(behavior.Phase=="skate" && trip.ElapsedMilliseconds<22000)
                {Pump(40);horizontal &= Math.Abs(window.Top-top)<.001;if(behavior.Phase=="skate" && behavior.CurrentClip.Id!=firstDirection)turned=true;}
                Check(behavior.Phase=="idle" && turned && horizontal && Math.Abs(window.Left-origin)<1,"actual short sliding window reverses and returns to its original horizontal position");
                foreach(int choice in new[]{2,3,4})
                {
                    random.Choice=choice;Call("SetMovementMode",MovementMode.Off);Automatic(choice,true);original=behavior.CurrentClip;before=requests;int actionImages=frameEvents;
                    Click();Pump(180);MouseEvent(true,MouseButton.Right);MouseEvent(false,MouseButton.Right);Call("SetMovementMode",MovementMode.Long);
                    Check(behavior.Phase=="automatic" && Object.ReferenceEquals(original,behavior.CurrentClip) && requests==before && frameEvents>actionImages,"new music/exercise playback ignores single/right/movement-menu inputs and keeps animating");
                    Click();Pump(70);Click();Check(behavior.Phase=="automatic" && behavior.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"new music/exercise yields directly to a qualified locked double click");FinishDance();
                }
            }
            if(type.GetField("startupMenu",BindingFlags.Instance|BindingFlags.NonPublic)!=null)
            {
                var startupMenu=(MenuItem)type.GetField("startupMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                object trayStartup=type.GetField("trayStartup",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                var startup=(StartupRegistration)type.GetField("startup",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                Check(window.Icon!=null && !window.ShowInTaskbar,"custom window icon is loaded and taskbar remains hidden");
                Check(!startupMenu.IsChecked && (string)startupMenu.Header=="开机自启动：关闭","startup menu initially shows disabled");
                bool enabled;string error;
                Check(startup.TryRead(out enabled,out error) && !enabled,"diagnostic window uses isolated startup state");
                random.Choice=2;Call("SetMovementMode",MovementMode.Off);Automatic(2,true);before=requests;int images=frameEvents;
                startupMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(100);
                Check(startupMenu.IsChecked && (string)startupMenu.Header=="开机自启动：开启","desktop menu enables login startup");
                Check((bool)trayStartup.GetType().GetProperty("Checked").GetValue(trayStartup,null) && (string)trayStartup.GetType().GetProperty("Text").GetValue(trayStartup,null)=="开机自启动：开启","tray reflects enabled state from desktop menu");
                Check(startup.TryRead(out enabled,out error) && enabled,"enabled menu writes isolated startup registration");
                Check(behavior.Phase=="automatic" && requests==before && frameEvents>images,"startup toggle does not pause or interrupt random animation");
                trayStartup.GetType().GetMethod("PerformClick").Invoke(trayStartup,null);Pump(100);
                Check(!startupMenu.IsChecked && (string)startupMenu.Header=="开机自启动：关闭" && !(bool)trayStartup.GetType().GetProperty("Checked").GetValue(trayStartup,null),"tray click disables startup and updates both menus");
                Check(startup.TryRead(out enabled,out error) && !enabled,"disabled menu removes isolated startup registration");
            }
            foreach(int choice in new[]{0,1,2,3,4})
            {
                random.Choice=choice;behavior.SetSleepEnabled(true,Now);Call("SetMovementMode",MovementMode.Off);Automatic(choice,true);
                original=behavior.CurrentClip;before=requests;int images=frameEvents;
                int loops=new[]{5,3,5,2,5}[choice];
                Check(original.Frames.Count==clips[original.Id].Frames.Count*loops && original.DurationMilliseconds==clips[original.Id].DurationMilliseconds*loops,"actual random window uses five/three/five/two complete loops respectively");
                typeof(PetBehaviorController).GetProperty("LastInteraction").SetValue(behavior,Now-PetBehaviorController.InactivityMilliseconds+100,null);
                Pump(180);
                Check(behavior.Phase=="automatic" && Object.ReferenceEquals(original,behavior.CurrentClip) && requests==before && frameEvents>images,"every ordinary window animation continues across bedtime without replacement");
                long end=(long)typeof(PetBehaviorController).GetField("phaseStarted",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(behavior)+original.DurationMilliseconds;
                var offset=type.GetField("diagnosticTimeOffset",BindingFlags.Instance|BindingFlags.NonPublic);
                offset.SetValue(window,(long)offset.GetValue(window)+end-Now+1);Call("OnTick",null,EventArgs.Empty);
                Check(behavior.IsSleeping && (string)type.GetField("renderedAction",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window)=="sleep" && requests==before+1,"ordinary window switches directly to queued sleep after its configured complete loops");
            }
            foreach(MovementMode mode in new[]{MovementMode.Long,MovementMode.Short})
            {
                StartTravel(random,mode);double top=window.Top;
                object motion=type.GetField("travel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                double origin=(double)motion.GetType().GetProperty("OriginLeft").GetValue(motion,null);
                typeof(PetBehaviorController).GetProperty("SleepEnabled").SetValue(behavior,true,null);
                typeof(PetBehaviorController).GetProperty("LastInteraction").SetValue(behavior,Now-PetBehaviorController.InactivityMilliseconds+100,null);
                string first=behavior.CurrentClip.Id;bool turned=false,horizontal=true;before=requests;
                Pump(180);Check(behavior.Phase=="skate" && requests==before,"window travel is not stopped at the sleep deadline");
                var trip=Stopwatch.StartNew();
                while(behavior.Phase=="skate" && trip.ElapsedMilliseconds<24000)
                {Pump(40);horizontal &= Math.Abs(window.Top-top)<.001;if(behavior.Phase=="skate" && behavior.CurrentClip.Id!=first)turned=true;}
                Check(behavior.IsSleeping && (string)type.GetField("renderedAction",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window)=="sleep" && horizontal,"completed actual travel enters sleep with constant horizontal height");
                if(mode==MovementMode.Short)Check(turned && Math.Abs(window.Left-origin)<1,"queued sleep waits for the entire short return trip, not just its outward leg");
                else Check(!turned && Math.Abs(window.Left-origin)>10,"queued sleep waits until long travel reaches its natural edge");
            }
            File.WriteAllText(Path.Combine(output,"routed-input.json"),"{\"passed\":true,\"checks\": "+checks+",\"slow_store_delay_ms\":450,\"ui_thread_writes\":"+store.UiWrites+",\"first_release_ms\":"+elapsed+",\"second_release_ms\":"+second+",\"results\":[\""+String.Join("\",\"",log)+"\"]}");
            Console.WriteLine("PASS: "+checks+" routed window input checks.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"routed-input-failure.txt"),e.ToString()+Environment.NewLine+String.Join(Environment.NewLine,log));return 1;}
        finally{if(window!=null)window.Close();}
    }
}
