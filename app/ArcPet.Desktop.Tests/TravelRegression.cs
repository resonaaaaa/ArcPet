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

internal sealed class TravelPreferences:IPreferencesStore
{
    public string FilePath{get{return "memory-only-travel-profile.json";}}
    public string LoadStatus{get{return "loaded";}}
    public PetPreferences Load(){return new PetPreferences{Locked=false,Visible=true,SleepEnabled=false,Left=300,Top=100};}
    public bool TrySave(PetPreferences p,out string error){error=null;return true;}
}
internal sealed class TravelWindowRandom:IRandomSource
{
    public int Style;
    public int Next(int min,int max){return min!=0?60000:max==6?4:max==2?Style:max==3?1:0;}
}
internal sealed class TravelClock:IPlaybackClock
{
    public long ElapsedMilliseconds{get;set;}
    public void Restart(){ElapsedMilliseconds=0;}
    public void Stop(){}
}
internal static class TravelRegression
{
    private static Window window;private static Type type;private static Image sprite;
    private static PetBehaviorController pet;private static AnimationPlayer player;private static TravelClock clock;
    private static TravelWindowRandom random;private static int checks;
    private static readonly List<string> log=new List<string>();
    private static object Call(string name,params object[] args){return type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(window,args);}
    private static long Now{get{return (long)type.GetProperty("Now",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window,null);}}
    private static object Field(string name){return type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);}
    private static void Check(bool ok,string name){checks++;log.Add((ok?"PASS:":"FAIL:")+name);if(!ok)throw new Exception(name);}
    private static void Pump(int ms){var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};t.Tick+=delegate{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
    private static void Advance(long ms)
    {
        var f=type.GetField("diagnosticTimeOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        f.SetValue(window,(long)f.GetValue(window)+ms);clock.ElapsedMilliseconds+=ms;Call("OnTick",null,EventArgs.Empty);
    }
    private static void MouseEvent(bool down,MouseButton button)
    {
        var e=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,button){RoutedEvent=down?Mouse.PreviewMouseDownEvent:Mouse.PreviewMouseUpEvent};sprite.RaiseEvent(e);
        if(!e.Handled){e.RoutedEvent=down?Mouse.MouseDownEvent:Mouse.MouseUpEvent;sprite.RaiseEvent(e);}
    }
    private static void Start(int style,MovementMode mode,int direction)
    {
        window.ContextMenu.IsOpen=false;Call("EndGesture",true);pet.Touch(Now);pet.CancelPress(Now);
        if(!window.IsVisible)Call("ToggleVisibility");if(pet.IsLocked)Call("ToggleLock");
        Call("SetMovementMode",mode);pet.SetSleepEnabled(false,Now);pet.Start(Now);random.Style=style;
        var area=(TravelArea)Call("GetTravelArea",null,null,style==0?"skateboard":"walk");
        double x=direction>0?area.SafeMinimum+10:area.SafeMaximum-10;
        object layout=Field("layout");layout.GetType().GetMethod("MoveTo").Invoke(layout,new object[]{x,100.0});Call("ApplyLayout");
        Call("RefreshTravelAvailability");Advance(pet.NextAutomaticAt-Now);
        Check(pet.IsTraveling && pet.TravelAction==(style==0?"skateboard":"walk"),"window starts the selected movement style through the random slot");
        Check(pet.CurrentClip.Id==pet.TravelAction+(direction>0?"_right":"_left"),"actual directional animation matches farther-edge selection");
    }
    private static void CheckRenderedFrame()
    {
        var cache=Field("cache");var expected=cache.GetType().GetMethod("Get").Invoke(cache,new object[]{pet.CurrentClip.Frames[player.CurrentFrameIndex].AssetPath});
        Check(Object.ReferenceEquals(sprite.Source,expected) && (string)Field("renderedAction")==pet.CurrentClip.Id,"WPF image renders the movement clip's current frame");
    }
    private static void Screenshot(string output,string name)
    {
        sprite.UpdateLayout();var bmp=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);bmp.Render(sprite);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var s=File.Create(Path.Combine(output,name)))encoder.Save(s);
    }
    [STAThread]public static int Main(string[] args)
    {
        string output=args[1];Directory.CreateDirectory(output);
        try
        {
            string exe=Path.GetFullPath(args[0]);var asm=Assembly.LoadFrom(exe);type=asm.GetType("ArcPet.Desktop.PetWindow",true);
            var clips=new JsonAnimationCatalog(Path.Combine(Path.GetDirectoryName(exe),"assets")).LoadAll();random=new TravelWindowRandom();
            pet=new PetBehaviorController(clips,random);clock=new TravelClock();player=new AnimationPlayer(clock);
            new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            window=(Window)Activator.CreateInstance(type,new object[]{player,pet,clips,new TravelPreferences(),null,null});sprite=(Image)window.Content;Call("Start");Pump(80);
            foreach(int style in new[]{0,1})foreach(MovementMode mode in new[]{MovementMode.Long,MovementMode.Short})foreach(int direction in new[]{-1,1})
            {
                Start(style,mode,direction);double x=window.Left,y=window.Top,size=window.Width;string first=pet.CurrentClip.Id;
                CheckRenderedFrame();Advance(200);CheckRenderedFrame();
                Check(direction*(window.Left-x)>0 && window.Top==y && window.Width==size,"real window moves horizontally while retaining its height and scale");
                if(style==1 && mode==MovementMode.Long)Screenshot(output,direction>0?"wpf-walk-right.png":"wpf-walk-left.png");
                typeof(PetBehaviorController).GetProperty("SleepEnabled").SetValue(pet,true,null);
                typeof(PetBehaviorController).GetProperty("LastInteraction").SetValue(pet,Now-PetBehaviorController.InactivityMilliseconds+1,null);
                Advance(40);Check(pet.IsTraveling,"sleep deadline queues behind either moving artwork");
                bool turned=false,horizontal=true,sameStyle=true;int steps=0;
                while(pet.IsTraveling && steps++<4000)
                {
                    Advance(40);horizontal &= window.Top==y;
                    if(pet.IsTraveling){sameStyle &= pet.TravelAction==(style==0?"skateboard":"walk");if(pet.CurrentClip.Id!=first)turned=true;}
                }
                Check(pet.IsSleeping && horizontal && sameStyle,"completed actual movement enters queued sleep without changing style or vertical height");
                Check(mode==MovementMode.Short ? turned && Math.Abs(window.Left-x)<.001 : !turned && Math.Abs(window.Left-x)>10,
                    "short window trip returns to origin and long trip stops at edge for both styles");
            }
            foreach(int style in new[]{0,1})foreach(string input in new[]{"single","double","hold","right","wheel","middle","lock","hide","mode"})
            {
                Start(style,MovementMode.Short,1);Advance(160);
                if(input=="right"){MouseEvent(true,MouseButton.Right);MouseEvent(false,MouseButton.Right);}
                else if(input=="wheel")sprite.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=Mouse.PreviewMouseWheelEvent});
                else if(input=="middle")MouseEvent(true,MouseButton.Middle);
                else if(input=="lock")Call("ToggleLock");
                else if(input=="hide")Call("ToggleVisibility");
                else if(input=="mode")Call("SetMovementMode",MovementMode.Off);
                else MouseEvent(true,MouseButton.Left);
                double stopped=window.Left,y=window.Top;
                Check(pet.Phase=="idle" && !(bool)Field("travel").GetType().GetProperty("IsActive").GetValue(Field("travel"),null),"routed mouse or menu input stops either movement controller immediately");
                MouseEvent(false,MouseButton.Left);
                if(input=="double"){Advance(60);MouseEvent(true,MouseButton.Left);MouseEvent(false,MouseButton.Left);}
                Advance(600);
                Check(pet.Phase=="idle" && window.Left==stopped && window.Top==y,"interrupted travel stays in place without queued dance or jump");
            }
            File.WriteAllText(Path.Combine(output,"travel-window.json"),"{\"passed\":true,\"checks\":"+checks+",\"virtual_clock\":true,\"results\":[\""+String.Join("\",\"",log)+"\"]}");
            Console.WriteLine("PASS: "+checks+" walking/skating window checks.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"travel-window-failure.txt"),e+Environment.NewLine+String.Join(Environment.NewLine,log));return 1;}
        finally{if(window!=null)window.Close();}
    }
}
