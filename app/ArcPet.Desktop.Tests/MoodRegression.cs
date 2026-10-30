using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;

internal sealed class MoodPreferences:IPreferencesStore
{
    public int Writes;
    public string FilePath {get{return "isolated-memory";}}
    public string LoadStatus {get{return "loaded";}}
    public PetPreferences Load(){return new PetPreferences{Left=250,Top=240,Locked=true,SleepEnabled=false};}
    public bool TrySave(PetPreferences p,out string error){Writes++;error=null;return true;}
}
internal sealed class MoodActionRandom:IRandomSource
{
    public int Choice;
    public int Next(int min,int max){return min==0 ? Choice : 60000;}
}
internal static class MoodRegression
{
    private static Type type;
    private static Window pet,bubble;
    private static object renderer;
    private static MoodBubbleController mood;
    private static PetBehaviorController behavior;
    private static readonly List<string> results=new List<string>();
    private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    private static object Call(string name,params object[] args){return type.GetMethod(name,Flags).Invoke(pet,args);}
    private static long Now {get{return (long)type.GetProperty("Now",Flags).GetValue(pet,null);}}
    private static void Check(bool value,string name){results.Add((value?"PASS: ":"FAIL: ")+name);if(!value)throw new Exception(name);}
    private static void Pump()
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};
        timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    private static void Advance(long delta)
    {
        var f=type.GetField("diagnosticTimeOffset",Flags);f.SetValue(pet,(long)f.GetValue(pet)+delta);Call("OnTick",null,EventArgs.Empty);Pump();
    }
    private static Canvas Canvas {get{return (Canvas)renderer.GetType().GetProperty("Artwork",Flags).GetValue(renderer,null);}}
    private static void Render(long time){renderer.GetType().GetMethod("Render",Flags|BindingFlags.DeclaredOnly).Invoke(renderer,new object[]{time});Pump();}
    private static void Save(string output,string name)
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            var overlayBitmap=new RenderTargetBitmap(320,164,96,96,PixelFormats.Pbgra32);overlayBitmap.Render(Canvas);
            dc.DrawRectangle(Brushes.FloralWhite,null,new Rect(0,0,320,424));
            dc.DrawRectangle(new VisualBrush(pet.Content as Visual),null,new Rect(0,104,320,320));
            dc.DrawImage(overlayBitmap,new Rect(0,0,320,164));
        }
        var bitmap=new RenderTargetBitmap(320,424,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var s=File.Create(Path.Combine(output,name)))encoder.Save(s);
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]private static extern int GetWindowLong(IntPtr h,int index);
    [STAThread]public static int Main(string[] args)
    {
        string output=args[1];Directory.CreateDirectory(output);
        try
        {
            var assembly=Assembly.LoadFrom(args[0]);type=assembly.GetType("ArcPet.Desktop.PetWindow",true);
            var clips=new JsonAnimationCatalog(Path.Combine(Path.GetDirectoryName(args[0]),"assets")).LoadAll();
            var random=new MoodActionRandom();behavior=new PetBehaviorController(clips,random);var player=new AnimationPlayer(new MonotonicPlaybackClock());
            var store=new MoodPreferences();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            pet=(Window)Activator.CreateInstance(type,new object[]{player,behavior,clips,store,null,null});Call("Start");Pump();
            ((DispatcherTimer)type.GetField("timer",Flags).GetValue(pet)).Stop();
            renderer=type.GetField("mood",Flags).GetValue(pet);bubble=(Window)renderer;
            mood=(MoodBubbleController)renderer.GetType().GetProperty("Controller",Flags).GetValue(renderer,null);
            double left=pet.Left,top=pet.Top,width=pet.Width;int requests=0;behavior.PlaybackRequested+=delegate{requests++;};
            typeof(MoodBubbleController).GetProperty("NextIdleAt").SetValue(mood,Now,null);Call("OnTick",null,EventArgs.Empty);Pump();
            Check(bubble.IsVisible && bubble.Owner==pet && !bubble.ShowInTaskbar && !bubble.ShowActivated,"independent owned overlay visible without taskbar or activation");
            int style=GetWindowLong(new WindowInteropHelper(bubble).Handle,-20);
            Check((style&0x08000020)==0x08000020 && !bubble.IsHitTestVisible,"native overlay click-through and no-activate flags");
            Check(pet.Left==left && pet.Top==top && pet.Width==width && pet.Content is Image,"bubble never expands or moves pet input bounds");
            long start=mood.StartedAt;Render(start+90);
            var small=Canvas.Children[0];var medium=Canvas.Children[1];var main=Canvas.Children[2] as Canvas;
            Check(Math.Abs(small.Opacity-.5)<.001 && medium.Opacity==0 && main.Opacity==0,"actual WPF early fade stage");
            Render(start+420);Check(small.Opacity==1 && medium.Opacity==1 && Math.Abs(main.Opacity-.5)<.001,"actual WPF main enters last");
            Check(main.Children.Count==2 && main.Children[0].Opacity==1 && main.Children[1].Opacity==1,"main and icon share one parent opacity");
            Check(((Image)main.Children[0]).Width==106 && Canvas.GetLeft(main.Children[0])==135 && Canvas.GetLeft(small)==69,"actual cloud geometry and relative offsets match v3");
            Render(start+1000);Save(output,"wpf-idle.png");
            Check(Math.Abs(bubble.Left-pet.Left)<.001 && Math.Abs(bubble.Top-(pet.Top-104))<.001,"cloud layout matches approved pet-relative position");
            behavior.PrimaryDown(Now);behavior.PrimaryUp(Now+10);behavior.SecondaryDown(Now+20);
            Check(mood.IsVisible && requests==0,"single and right click do not restart pet or idle bubble");
            Call("ToggleLock");object layout=type.GetField("layout",Flags).GetValue(pet);
            layout.GetType().GetMethod("MoveTo").Invoke(layout,new object[]{left+45,top+20});Call("ApplyLayout");Render(start+1000);
            Check(Math.Abs(bubble.Left-pet.Left)<.001 && Math.Abs(bubble.Top-(pet.Top-104))<.001,"overlay follows unlocked drag placement");
            layout.GetType().GetMethod("ResizeFrom").Invoke(layout,new object[]{1.0,0.0,-100.0,pet.Left,pet.Top});Call("ApplyLayout");Render(start+1000);
            Check(Math.Abs(bubble.Width-pet.Width)<.001 && Math.Abs(bubble.Top-(pet.Top-104*pet.Width/320))<.001,"overlay follows existing scale without changing pet scale rules");
            Call("ToggleVisibility");Check(!bubble.IsVisible && !mood.IsVisible,"hide pet synchronously removes bubble");
            Call("ToggleVisibility");Check(!bubble.IsVisible && mood.NextIdleAt>=Now+19900,"show pet starts fresh idle bubble wait");
            Advance(60001);Check(behavior.Phase=="automatic" && behavior.CurrentClip.Id=="icecream" && !bubble.IsVisible,"random ordinary action removes bubble before playback");
            Advance(14000);random.Choice=1;Advance(60001);
            Check(behavior.CurrentClip.Id=="think" && mood.Mode=="think" && bubble.IsVisible,"thinking immediately creates owned bubble");
            string first=mood.ContentId;int generation=mood.Generation;int before=requests;
            Advance(6000);Check(mood.Generation==generation+1 && mood.ContentId!=first && requests==before,"thinking loop boundary changes content without replay request");
            Render(mood.StartedAt+1100);Save(output,"wpf-think.png");
            Advance(6000);Check(mood.Generation==generation+2 && behavior.Phase=="automatic","third thinking loop starts fresh bubble");
            Advance(6000);Check(behavior.Phase=="idle" && !bubble.IsVisible,"thinking final loop ends bubble and returns to idle");
            Advance(60001);if(!behavior.IsLocked)Call("ToggleLock");
            behavior.PrimaryDown(Now);behavior.PrimaryUp(Now+20);behavior.PrimaryDown(Now+100);behavior.PrimaryUp(Now+120);
            Check(behavior.Phase=="automatic" && behavior.CurrentClip.Id.StartsWith("dance",StringComparison.Ordinal) && !bubble.IsVisible,"qualified double click clears thinking bubble synchronously");
            File.WriteAllText(Path.Combine(output,"mood-window.json"),"{\"passed\":true,\"checks\":"+results.Count+",\"results\":[\""+String.Join("\",\"",results)+"\"]}");
            Console.WriteLine("PASS: "+results.Count+" mood window checks.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"mood-window-failure.txt"),e+Environment.NewLine+String.Join(Environment.NewLine,results));return 1;}
        finally{if(pet!=null)pet.Close();}
    }
}
