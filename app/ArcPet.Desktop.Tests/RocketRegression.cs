using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;

internal sealed class RocketPreferences:IPreferencesStore
{
    public PetPreferences Last;
    public string FilePath {get{return "memory-only-rocket";}}
    public string LoadStatus {get{return "loaded";}}
    public PetPreferences Load(){return new PetPreferences{Left=450,Top=420,Locked=true,SleepEnabled=true};}
    public bool TrySave(PetPreferences value,out string error){Last=value;error=null;return true;}
}
internal static class RocketRegression
{
    private static Window pet,art;private static Type type;
    private static PetBehaviorController behavior;private static VerticalFlight motion;
    private static readonly List<string> results=new List<string>();
    private static readonly HashSet<int> chargeSeen=new HashSet<int>();
    private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    private static object Call(string method,params object[] values){return type.GetMethod(method,Flags).Invoke(pet,values);}
    private static void Check(bool b,string name){results.Add((b?"PASS: ":"FAIL: ")+name);if(!b)throw new Exception(name);}
    private static void Pump(int ms)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    private static void Until(Func<bool> condition,int timeout)
    {var watch=Stopwatch.StartNew();while(!condition() && watch.ElapsedMilliseconds<timeout)Pump(20);if(!condition())throw new Exception("Phase timeout: "+behavior.Phase);}
    private static void Save(string file)
    {
        var bitmap=new RenderTargetBitmap(320,440,96,96,PixelFormats.Pbgra32);bitmap.Render((Visual)art.Content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var s=File.Create(file))encoder.Save(s);
    }
    [STAThread]public static int Main(string[] args)
    {
        string output=args[1];Directory.CreateDirectory(output);
        try
        {
            var assembly=Assembly.LoadFrom(args[0]);type=assembly.GetType("ArcPet.Desktop.PetWindow",true);
            var clips=new JsonAnimationCatalog(Path.Combine(Path.GetDirectoryName(args[0]),"assets")).LoadAll();
            behavior=new PetBehaviorController(clips,new RandomSource());var player=new AnimationPlayer(new MonotonicPlaybackClock());var store=new RocketPreferences();
            player.FrameChanged+=delegate(object sender,AnimationFrameChangedEventArgs e){if(e.Clip.Id=="rocket_charge")chargeSeen.Add(e.FrameIndex);};
            new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};pet=(Window)Activator.CreateInstance(type,new object[]{player,behavior,clips,store,null,null});Call("Start");Pump(100);
            art=(Window)type.GetField("rocketArtwork",Flags).GetValue(pet);motion=(VerticalFlight)type.GetField("flight",Flags).GetValue(pet);
            double x=pet.Left,y=pet.Top,w=pet.Width;var sprite=(Image)pet.Content;
            Call("BeginPrimaryGesture",new Point(200,200),false);Pump(1250);
            Check(behavior.Phase=="hold" && !motion.IsActive,"ordinary hold remains separate below rocket threshold");
            Until(()=>behavior.Phase=="rocket_hold",2500);Pump(500);
            Check(chargeSeen.Count==9,"all nine independent gradual-face frames actually rendered");
            Check(!motion.IsActive && behavior.Phase=="rocket_hold","held charged loop never launches by itself");
            Check(art.IsVisible && art.Owner==pet && !art.ShowInTaskbar && !art.IsHitTestVisible && !art.ShowActivated,"extended flame artwork uses passive owned window");
            Check(pet.Width==w && pet.Height==w && art.Height==440*w/320 && sprite.Opacity==0,"fire does not shrink body or resize original input window");
            Save(Path.Combine(output,"rocket-ready.png"));Call("EndGesture",false);
            Until(()=>motion.IsActive,1000);Check(behavior.Phase=="rocket_launch","release starts motion through distinct upright transition");
            Pump(500);Check(pet.Top<y-20 && pet.Left==x && behavior.IsLocked,"rocket moves vertically despite manual position lock");
            var snapshot=(PetPreferences)Call("CapturePreferences");
            Check(snapshot.Top==y && snapshot.Left==x && snapshot.Locked,"transient flight coordinates never enter persisted preferences");
            Check(behavior.Phase=="rocket_flight" && art.IsVisible,"launch completes into separate wind-and-flame loop");
            Save(Path.Combine(output,"rocket-flight.png"));
            bool fixedX=true,bounded=true;double minimum=y;var wait=Stopwatch.StartNew();
            while(motion.IsActive && wait.ElapsedMilliseconds<3000){Pump(20);fixedX&=pet.Left==x;bounded&=pet.Top>=motion.TargetTop-.01;minimum=Math.Min(minimum,pet.Top);}
            Check(!motion.IsActive && fixedX && bounded && minimum<=motion.TargetTop+3,"flight reaches monitor-top limit without lateral movement or overshoot");
            Check(pet.Left==x && pet.Top==y,"flight returns exact original window position");
            Until(()=>behavior.Phase=="idle",3500);Check(!art.IsVisible && sprite.Opacity==1,"loop settles, cools and restores original idle rendering");
            Check(pet.Width==w && behavior.IsLocked && behavior.SleepEnabled,"flight preserves size lock and sleep settings");
            // Hide during a second actual trip; do not leave either overlay or
            // persist the transient location if the user hides/closes the pet.
            Call("BeginPrimaryGesture",new Point(200,200),false);Until(()=>behavior.Phase=="rocket_hold",3500);Call("EndGesture",false);Until(()=>motion.IsActive,1000);Pump(200);
            Call("ToggleVisibility");
            Check(!motion.IsActive && !pet.IsVisible && !art.IsVisible && pet.Left==x && pet.Top==y && behavior.Phase=="idle","hide mid-flight atomically cancels, restores origin and hides extended art");
            snapshot=(PetPreferences)Call("CapturePreferences");Check(snapshot.Top==y && !snapshot.Visible,"hidden profile still remembers original ground position");
            Call("ToggleVisibility");Check(pet.IsVisible && !art.IsVisible,"show after cancelled flight restores normal pet only");
            File.WriteAllText(Path.Combine(output,"rocket-window.json"),"{\"passed\":true,\"checks\":"+results.Count+",\"charge_frames_seen\":"+chargeSeen.Count+",\"results\":[\""+String.Join("\",\"",results)+"\"]}");
            Console.WriteLine("PASS: "+results.Count+" rocket window checks.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);File.WriteAllText(Path.Combine(output,"rocket-window-failure.txt"),e+Environment.NewLine+String.Join(Environment.NewLine,results));return 1;}
        finally{if(pet!=null)pet.Close();}
    }
}
