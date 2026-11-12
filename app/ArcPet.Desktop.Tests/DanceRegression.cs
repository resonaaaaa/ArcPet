using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;

internal sealed class DancePreferences:IPreferencesStore
{
    public string FilePath{get{return "memory-only-dance-profile.json";}}
    public string LoadStatus{get{return "loaded";}}
    public PetPreferences Load(){return new PetPreferences{Locked=false,SleepEnabled=false,Visible=true,Left=100,Top=100};}
    public bool TrySave(PetPreferences value,out string error){error=null;return true;}
}
internal static class DanceRegression
{
    private static Window window;private static Type type;private static Image sprite;
    private static PetBehaviorController pet;private static int checks,requests,images;
    private static readonly List<string> log=new List<string>();
    private static object Call(string name,params object[] args){return type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(window,args);}
    private static long Now{get{return (long)type.GetProperty("Now",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window,null);}}
    private static void Pump(int ms){var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};t.Tick+=delegate{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
    private static void Check(bool ok,string name){checks++;log.Add((ok?"PASS:":"FAIL:")+name);if(!ok)throw new Exception(name);}
    private static void MouseEvent(bool down,MouseButton b)
    {
        var e=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,b){RoutedEvent=down?Mouse.PreviewMouseDownEvent:Mouse.PreviewMouseUpEvent};sprite.RaiseEvent(e);
        if(!e.Handled){e.RoutedEvent=down?Mouse.MouseDownEvent:Mouse.MouseUpEvent;sprite.RaiseEvent(e);}
    }
    // Synthetic routed presses cannot change the native button state. Capture
    // immediately produces a Released move for unlocked drag gestures, so those
    // cases drive the same production adapters without native capture.
    private static void Click()
    {
        if(pet.IsLocked){MouseEvent(true,MouseButton.Left);Pump(25);MouseEvent(false,MouseButton.Left);}
        else{Call("BeginPrimaryGesture",new Point(100,100),false);Pump(25);Call("EndGesture",false);}
    }
    private static void Double(){Click();Pump(60);Click();}
    private static void Reset(bool locked)
    {
        window.ContextMenu.IsOpen=false;Call("EndGesture",true);pet.Start(Now);
        if(pet.IsLocked!=locked)Call("ToggleLock");pet.SetSleepEnabled(false,Now);Pump(25);
    }
    private static void Advance(long target)
    {
        var f=type.GetField("diagnosticTimeOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        f.SetValue(window,(long)f.GetValue(window)+Math.Max(0,target-Now));Call("OnTick",null,EventArgs.Empty);
    }
    [STAThread]public static int Main(string[] args)
    {
        string output=args[1];Directory.CreateDirectory(output);
        try
        {
            string exe=Path.GetFullPath(args[0]);var asm=Assembly.LoadFrom(exe);type=asm.GetType("ArcPet.Desktop.PetWindow",true);
            var clips=new JsonAnimationCatalog(Path.Combine(Path.GetDirectoryName(exe),"assets")).LoadAll();
            pet=new PetBehaviorController(clips,new RandomSource());var testClock=new DanceWindowClock();var player=new AnimationPlayer(testClock);
            pet.PlaybackRequested+=delegate{requests++;};player.FrameChanged+=delegate{images++;};
            new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            window=(Window)Activator.CreateInstance(type,new object[]{player,pet,clips,new DancePreferences(),null,null});sprite=(Image)window.Content;Call("Start");Pump(100);
            foreach(bool locked in new[]{false,true})
            {
                Reset(locked);double x=window.Left,y=window.Top,w=window.Width;int before=requests;
                Click();Pump(80);Check(pet.Phase=="idle" && requests==before,"single routed click remains inert in either lock state");Click();
                Check(pet.Phase=="automatic" && pet.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && requests==before+1,"routed double click plays dance in either lock state");
                Check(pet.CurrentClip.DurationMilliseconds==12000 && pet.CurrentClip.Frames.Count==360,"actual window receives twelve complete one-second loops");
                Check(window.Left==x && window.Top==y && window.Width==w,"double click does not move or resize the pet");
                Check((string)type.GetField("renderedAction",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window)==pet.CurrentClip.Id,"sprite immediately renders the chosen dance facing instead of crouching");
                var batch=pet.CurrentClip;before=requests;int initial=images;Pump(380);
                Check(images>initial+2 && Object.ReferenceEquals(batch,pet.CurrentClip),"real sprite frames advance through the accelerated transition");
                Click();MouseEvent(true,MouseButton.Right);MouseEvent(false,MouseButton.Right);window.ContextMenu.IsOpen=false;
                Check(Object.ReferenceEquals(batch,pet.CurrentClip) && requests==before,"single and right clicks preserve active dance");
                Double();Check(pet.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && !Object.ReferenceEquals(batch,pet.CurrentClip),"qualified double click replaces dance with a fresh batch");
                batch=pet.CurrentClip;
                if(locked)
                {
                    MouseEvent(true,MouseButton.Left);Pump(530);Check(pet.Phase=="down","locked long press interrupts dance with the existing descent");MouseEvent(false,MouseButton.Left);Pump(1650);
                    Check(pet.Phase=="idle","long-press interruption finishes naturally and does not resume dance");
                }
                else
                {
                    Call("BeginPrimaryGesture",new Point(100,100),false);Pump(650);Call("EndGesture",false);
                    Check(pet.Phase=="drag_settle" || pet.Phase=="drag_land","unlocked stationary long press interrupts dance and releases into landing");
                    Reset(false);Double();batch=pet.CurrentClip;
                    Call("BeginPrimaryGesture",new Point(100,100),false);Call("UpdatePointerPosition",new Point(125,115));Call("EndGesture",false);
                    Check(Object.ReferenceEquals(batch,pet.CurrentClip),"pointer motion before the hold threshold preserves dance and cancels tap recognition");
                    Click();Check(Object.ReferenceEquals(batch,pet.CurrentClip),"click after drag cannot reuse the drag as a tap");
                    Call("BeginPrimaryGesture",new Point(100,100),true);Call("UpdatePointerPosition",new Point(100,90));Call("EndGesture",false);
                    Check(Object.ReferenceEquals(batch,pet.CurrentClip),"Ctrl resize preserves dance and clears double-click history");
                }
            }
            Reset(false);Double();var timer=(DispatcherTimer)type.GetField("timer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);timer.Stop();
            long start=(long)typeof(PetBehaviorController).GetField("phaseStarted",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
            int beforeSwitches=requests;
            var cache=type.GetField("cache",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            foreach(int elapsed in new[]{2999,3000,5999,6000,8999,9000})
            {
                testClock.Seek(elapsed);Call("OnTick",null,EventArgs.Empty);
                var clip=pet.CurrentClip;int index=clip.GetFrameIndex(elapsed);
                var expected=cache.GetType().GetMethod("Get").Invoke(cache,new object[]{clip.Frames[index].AssetPath});
                Check(player.CurrentFrameIndex==index && Object.ReferenceEquals(sprite.Source,expected),"actual sprite renders the correct frame before/at each three-loop mirror boundary");
            }
            Check(requests==beforeSwitches,"mirror changes stay within one batch and never restart or queue playback");
            var observed=(System.Diagnostics.Stopwatch)type.GetField("observed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            // Freeze real time so the one-millisecond boundary is set by Advance alone.
            observed.Stop();
            Advance(start+11999);Check(pet.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal),"window batch does not end one millisecond early");
            Advance(start+12000);Check(pet.Phase=="idle","window ends twelve loops at the exact boundary");
            observed.Start();
            var bmp=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);
            Reset(false);Double();sprite.UpdateLayout();bmp.Render(sprite);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));
            using(var s=File.Create(Path.Combine(output,"wpf-dance.png")))png.Save(s);
            File.WriteAllText(Path.Combine(output,"dance-window.json"),"{\"passed\":true,\"checks\":"+checks+",\"loop_ms\":1000,\"loops\":12,\"results\":[\""+String.Join("\",\"",log)+"\"]}");
            Console.WriteLine("PASS: "+checks+" dance window checks.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"dance-window-failure.txt"),e+Environment.NewLine+String.Join(Environment.NewLine,log));return 1;}
        finally{if(window!=null)window.Close();}
    }
}
internal sealed class DanceWindowClock:IPlaybackClock
{
    private readonly System.Diagnostics.Stopwatch watch=new System.Diagnostics.Stopwatch();
    private long? forced;
    public long ElapsedMilliseconds{get{return forced??watch.ElapsedMilliseconds;}}
    public void Restart(){forced=null;watch.Restart();}
    public void Stop(){watch.Stop();}
    public void Seek(long elapsed){forced=elapsed;}
}
