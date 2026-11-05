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

internal sealed class SleepPreferences:IPreferencesStore
{
    public string FilePath{get{return "memory-only-sleep-profile.json";}}
    public string LoadStatus{get{return "loaded";}}
    public PetPreferences Load(){return new PetPreferences{Locked=false,SleepEnabled=true,Visible=true,Left=100,Top=100};}
    public bool TrySave(PetPreferences value,out string error){error=null;return true;}
}
internal sealed class SleepDrawingRandom:IRandomSource
{
    public int Variant,Draws;
    public int Next(int min,int max){if(min!=0)return 60000;if(max==3){Draws++;return Variant;}return 0;}
}
internal sealed class SleepDrawingClock:IPlaybackClock
{
    public long ElapsedMilliseconds{get;set;}
    public void Restart(){ElapsedMilliseconds=0;}
    public void Stop(){}
}
internal static class SleepVariantRegression
{
    private static Window window;private static Type type;private static Image sprite;
    private static PetBehaviorController pet;private static int checks;
    private static readonly List<string> log=new List<string>();
    private static object Call(string name,params object[] args){return type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(window,args);}
    private static long Now{get{return (long)type.GetProperty("Now",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window,null);}}
    private static void Check(bool ok,string name){checks++;log.Add((ok?"PASS:":"FAIL:")+name);if(!ok)throw new Exception(name);}
    private static void Advance(long target)
    {
        var f=type.GetField("diagnosticTimeOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        f.SetValue(window,(long)f.GetValue(window)+Math.Max(0,target-Now));Call("OnTick",null,EventArgs.Empty);
    }
    private static void MouseEvent(bool down,MouseButton b)
    {
        var e=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,b){RoutedEvent=down?Mouse.PreviewMouseDownEvent:Mouse.PreviewMouseUpEvent};sprite.RaiseEvent(e);
        if(!e.Handled){e.RoutedEvent=down?Mouse.MouseDownEvent:Mouse.MouseUpEvent;sprite.RaiseEvent(e);}
    }
    private static void Enter(bool locked)
    {
        window.ContextMenu.IsOpen=false;Call("EndGesture",true);
        if(pet.IsLocked!=locked)Call("ToggleLock");pet.SetSleepEnabled(true,Now);pet.Start(Now);
        typeof(PetBehaviorController).GetProperty("LastInteraction").SetValue(pet,Now-300001,null);
        Call("OnTick",null,EventArgs.Empty);
    }
    [STAThread]public static int Main(string[] args)
    {
        string output=args[1];Directory.CreateDirectory(output);
        try
        {
            string exe=Path.GetFullPath(args[0]);var asm=Assembly.LoadFrom(exe);type=asm.GetType("ArcPet.Desktop.PetWindow",true);
            var clips=new JsonAnimationCatalog(Path.Combine(Path.GetDirectoryName(exe),"assets")).LoadAll();
            var random=new SleepDrawingRandom();pet=new PetBehaviorController(clips,random);var clock=new SleepDrawingClock();var player=new AnimationPlayer(clock);
            new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            window=(Window)Activator.CreateInstance(type,new object[]{player,pet,clips,new SleepPreferences(),null,null});sprite=(Image)window.Content;Call("Start");
            ((DispatcherTimer)type.GetField("timer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window)).Stop();
            foreach(bool locked in new[]{false,true})foreach(int variant in new[]{0,1,2})
            {
                random.Variant=variant;random.Draws=0;double x=window.Left,y=window.Top,width=window.Width;
                Enter(locked);string expected=variant==0?"lying":"sleep";
                Check(pet.IsSleeping && pet.CurrentClip.Id==expected && random.Draws==1,"WPF enters selected sleep drawing in either lock state");
                Check((string)type.GetField("renderedAction",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window)==expected,"WPF immediately renders selected drawing");
                Check(window.Left==x && window.Top==y && window.Width==width,"sleep entry preserves desktop position and user scale");
                clock.ElapsedMilliseconds=450;Call("OnTick",null,EventArgs.Empty);
                var cache=type.GetField("cache",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
                var frame=cache.GetType().GetMethod("Get").Invoke(cache,new object[]{clips[expected].Frames[clips[expected].GetFrameIndex(450)].AssetPath});
                Check(Object.ReferenceEquals(sprite.Source,frame) && random.Draws==1,"sleep animation advances without rerolling its drawing");
                if(variant==0 && !locked)
                {
                    window.UpdateLayout();sprite.UpdateLayout();var bmp=new RenderTargetBitmap(320,320,96,96,PixelFormats.Pbgra32);bmp.Render(sprite);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine(output,"wpf-lying.png")))png.Save(stream);
                }
                foreach(MouseButton button in new[]{MouseButton.Left,MouseButton.Right})
                {
                    Enter(locked);MouseEvent(true,button);MouseEvent(false,button);window.ContextMenu.IsOpen=false;
                    Check(pet.Phase=="idle" && !pet.IsSleeping,"routed left/right input wakes either sleep drawing");
                }
                Enter(locked);long start=(long)typeof(PetBehaviorController).GetField("phaseStarted",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
                Advance(start+119990);Check(pet.IsSleeping && pet.CurrentClip.Id==expected,"WPF sleep does not end before two-minute deadline");
                Advance(start+120001);Check(pet.Phase=="idle","WPF wakes selected drawing after two minutes");
                Enter(locked);Call("ToggleSleep");Check(!pet.SleepEnabled && pet.Phase=="idle","sleep menu disables and wakes either drawing");
            }
            File.WriteAllText(Path.Combine(output,"sleep-variant-window.json"),"{\"passed\":true,\"checks\":"+checks+",\"results\":[\""+String.Join("\",\"",log)+"\"]}");
            Console.WriteLine("PASS: "+checks+" sleep variant window checks.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"sleep-variant-window-failure.txt"),e+Environment.NewLine+String.Join(Environment.NewLine,log));return 1;}
        finally{if(window!=null)window.Close();}
    }
}
