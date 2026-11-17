using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;
using Forms = System.Windows.Forms;

internal sealed class PomodoroPreferences : IPreferencesStore
{
    public string FilePath { get { return "memory-only-pomodoro.json"; } }
    public string LoadStatus { get { return "loaded"; } }
    public PetPreferences Load() { return new PetPreferences { SleepEnabled=false, Left=100, Top=100 }; }
    public bool TrySave(PetPreferences value,out string error) { error=null;return true; }
}
internal sealed class PomodoroPlaybackClock : IPlaybackClock
{
    public long ElapsedMilliseconds { get; set; }
    public void Restart() { ElapsedMilliseconds=0; }
    public void Stop() { }
}
internal static class PomodoroRegression
{
    private static PomodoroPlaybackClock clock;
    private static PetBehaviorController pet;
    private static Window window; private static Type type; private static int checks;
    private static readonly List<string> log = new List<string>();
    private static object Field(string name) { return type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window); }
    private static object Call(string name,params object[] args) { return type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(window,args); }
    private static void Check(bool value,string name) { checks++;log.Add((value?"PASS: ":"FAIL: ")+name);if(!value)throw new Exception(name); }
    private static void Pump()
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(30) };
        timer.Tick+=delegate { timer.Stop();frame.Continue=false; };timer.Start();Dispatcher.PushFrame(frame);
    }
    private static void Click(MenuItem item) { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
    private static void Advance(long ms)
    {
        var field=type.GetField("diagnosticTimeOffset",BindingFlags.Instance|BindingFlags.NonPublic);
        field.SetValue(window,(long)field.GetValue(window)+ms);clock.ElapsedMilliseconds+=ms;Call("OnTick",null,EventArgs.Empty);
    }
    private static void FinishScene()
    {
        var scene=(PomodoroSceneController)Field("pomodoroScene");int count=0;
        while(scene.IsActive && count++<10)Advance(pet.CurrentClip.DurationMilliseconds+1);
        Check(!scene.IsActive && pet.Phase=="idle","complete ending sequence returns idle");
    }
    private static void VerifyFortune(MenuItem duration, string output)
    {
        Call("ToggleVisibility");
        var draw = (MenuItem)Field("fortuneMenu");
        var trayDraw = (Forms.ToolStripMenuItem)Field("trayFortune");
        var bubble = (Window)Field("mood");
        var result = bubble.GetType().GetProperty("FortuneText", BindingFlags.Instance|BindingFlags.NonPublic);
        Check(window.ContextMenu.Items.Contains(draw) && (string)draw.Header=="抽签" && draw.IsEnabled,
            "pet right-click menu has the draw action");
        Click(draw);
        string first = pet.FortuneResult;
        Check(pet.Phase=="fortune_shake" && (string)result.GetValue(bubble,null)==null && !draw.IsEnabled,
            "menu starts shaking without immediately revealing the result");
        Check(!duration.IsEnabled && !trayDraw.Enabled,"both menus block overlapping scenes and draws");
        Advance(pet.CurrentClip.DurationMilliseconds + 1);
        Check(pet.Phase=="fortune_draw" && (string)result.GetValue(bubble,null)==null,
            "result stays hidden while picking up the stick");
        Advance(pet.CurrentClip.DurationMilliseconds + 1);
        Advance(600);
        Pump();
        Check(pet.Phase=="fortune_reveal" && bubble.IsVisible && (string)result.GetValue(bubble,null)==first,
            "approved open-eye pose is paired with today's result bubble");
        var canvas = (Canvas)((Viewbox)bubble.Content).Child;
        var label = (TextBlock)canvas.Children[3];
        Check(label.Text==first && label.Opacity==1 && !((Viewbox)bubble.Content).IsHitTestVisible,
            "result text is fully painted in a click-through bubble");
        canvas.UpdateLayout();
        var sprite = (Image)window.Content;
        sprite.UpdateLayout();
        var petImage = new RenderTargetBitmap(320,320,96,96,PixelFormats.Pbgra32);
        var bubbleImage = new RenderTargetBitmap(161,114,96,96,PixelFormats.Pbgra32);
        petImage.Render(sprite);bubbleImage.Render(canvas);
        var preview = new DrawingVisual();
        using (var dc = preview.RenderOpen())
        {
            dc.DrawRectangle(Brushes.OldLace,null,new Rect(0,0,416,320));
            dc.DrawImage(petImage,new Rect(44,0,320,320));
            dc.DrawImage(bubbleImage,new Rect(249,13,161,114));
        }
        var rendered = new RenderTargetBitmap(416,320,96,96,PixelFormats.Pbgra32);
        rendered.Render(preview);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(rendered));
        using (var stream = File.Create(Path.Combine(output,"fortune-result.png"))) png.Save(stream);

        Advance(pet.CurrentClip.DurationMilliseconds + 1);
        Check(pet.Phase=="idle" && draw.IsEnabled && duration.IsEnabled &&
            (string)result.GetValue(bubble,null)==null && !bubble.IsVisible,
            "finishing closes the result bubble and re-enables both features");
        trayDraw.PerformClick();Pump();
        Check(pet.Phase=="fortune_shake" && pet.FortuneResult==first,
            "tray can replay the same daily result");
        Call("ToggleVisibility");
        for (int i=0;i<3;i++) Advance(pet.CurrentClip.DurationMilliseconds + 1);
        Check(pet.Phase=="idle" && !window.IsVisible && !bubble.IsVisible,
            "hidden draw completes without forcing either window visible");
        Call("ToggleVisibility");
        Click(duration);
        Check(!draw.IsEnabled && !trayDraw.Enabled,"active focus timer also disables drawing");
        Click((MenuItem)Field("pomodoroEnd"));
        FinishScene();
    }
    private static void Screenshot(string output)
    {
        var overlay=(Window)Field("rocketArtwork");var mood=(Window)Field("mood");
        ((FrameworkElement)overlay.Content).UpdateLayout();((FrameworkElement)mood.Content).UpdateLayout();
        var actor=new RenderTargetBitmap(384,384,96,96,PixelFormats.Pbgra32);actor.Render((Visual)overlay.Content);
        var core=new RenderTargetBitmap(320,320,96,96,PixelFormats.Pbgra32);core.Render((Visual)window.Content);
        var artwork=(FrameworkElement)mood.GetType().GetProperty("Artwork",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mood,null);
        var bubble=new RenderTargetBitmap(320,164,96,96,PixelFormats.Pbgra32);bubble.Render(artwork);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        { dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(244,238,228)),null,new Rect(0,0,384,448));dc.DrawImage(actor,new Rect(0,64,384,384));dc.DrawImage(core,new Rect(32,128,320,320));dc.DrawImage(bubble,new Rect(32,24,320,164)); }
        var combined=new RenderTargetBitmap(384,448,96,96,PixelFormats.Pbgra32);combined.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(combined));
        using(var stream=File.Create(Path.Combine(output,"pomodoro-hover.png")))encoder.Save(stream);
    }
    [STAThread] public static int Main(string[] args)
    {
        Directory.CreateDirectory(args[1]);
        try
        {
            string exe=Path.GetFullPath(args[0]);type=Assembly.LoadFrom(exe).GetType("ArcPet.Desktop.PetWindow",true);
            var clips=new JsonAnimationCatalog(Path.Combine(Path.GetDirectoryName(exe),"assets")).LoadAll();
            var behavior=new PetBehaviorController(clips,new RandomSource());
            clock=new PomodoroPlaybackClock();var player=new AnimationPlayer(clock);pet=behavior;
            new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            window=(Window)Activator.CreateInstance(type,new object[] {player,behavior,clips,new PomodoroPreferences(),null,null});
            Call("Start"); ((DispatcherTimer)Field("timer")).Stop();
            var tray=(Forms.NotifyIcon)Field("tray");
            MenuItem movement=null,pomodoroRoot=null;
            foreach(object o in window.ContextMenu.Items)
            {
                var item=o as MenuItem;if(item==null)continue;
                if((string)item.Header=="移动")movement=item;
                if((string)item.Header=="番茄钟")pomodoroRoot=item;
                Check(!((string)item.Header).StartsWith("移动："),"no flat movement option at root");
            }
            Check(movement!=null && movement.Items.Count==3,"movement submenu contains three vertical native items");
            Check(pomodoroRoot!=null && pomodoroRoot.Items.Count==9,"pomodoro submenu includes status, five presets and controls");
            window.ContextMenu.IsOpen=true;movement.IsSubmenuOpen=true;Pump();
            Check(movement.IsSubmenuOpen,"native movement submenu opens");
            movement.IsSubmenuOpen=false;pomodoroRoot.IsSubmenuOpen=true;Pump();
            Check(pomodoroRoot.IsSubmenuOpen,"native pomodoro submenu opens");window.ContextMenu.IsOpen=false;
            var durations=(Dictionary<int,MenuItem>)Field("pomodoroDurations");
            var trayDurations=(Dictionary<int,Forms.ToolStripMenuItem>)Field("trayPomodoroDurations");
            var timer=(PomodoroTimer)Field("pomodoro");
            Check(durations.Count==5 && trayDurations.Count==5,"desktop and tray offer all five presets");
            int requests=0;behavior.PlaybackRequested+=delegate { requests++; };
            foreach(int minutes in new[] {20,30,40,50,60})
            {
                Click(durations[minutes]);
                Check(timer.State==PomodoroState.Running && timer.Minutes==minutes,"desktop preset starts exact duration");
                Check(!durations[20].IsEnabled && !trayDurations[60].Enabled,"active session disables replacement in both menus");
                Click((MenuItem)Field("pomodoroPause"));
                Check(timer.State==PomodoroState.Paused && (string)((MenuItem)Field("pomodoroPause")).Header=="继续计时","pause changes to resume in desktop");
                ((Forms.ToolStripMenuItem)Field("trayPomodoroPause")).PerformClick();Pump();
                Check(timer.State==PomodoroState.Running,"tray resumes same session");
                ((Forms.ToolStripMenuItem)Field("trayPomodoroEnd")).PerformClick();Pump();
                Check(timer.State==PomodoroState.Idle && !durations[20].IsEnabled,"tray end queues complete exit before re-enabling starts");
                FinishScene();Check(durations[20].IsEnabled,"duration selection restored after tomato fade");
            }
            trayDurations[30].PerformClick();Pump();Check(timer.Minutes==30 && timer.IsActive,"tray duration starts shared timer");
            Click((MenuItem)Field("pomodoroEnd"));
            Check(requests>0 && behavior.IsPomodoro,"timer controls drive the exclusive scene");FinishScene();
            Click(durations[20]);Advance(4001);Check(behavior.CurrentClip.Id=="pomodoro_read","start shows approved reading clip");
            var sprite=(Image)window.Content;
            Check(sprite.Opacity==1 && sprite.Source is CroppedBitmap,"input HWND paints an opaque character surface during reading");
            var painted=new RenderTargetBitmap(320,320,96,96,PixelFormats.Pbgra32);painted.Render(sprite);
            var pixels=new byte[320*320*4];painted.CopyPixels(pixels,1280,0);int opaque=0;
            for(int i=3;i<pixels.Length;i+=4)if(pixels[i]>=64)opaque++;
            Check(opaque>10000,"native owner has substantial painted alpha instead of an invisible input surface");
            Check(window.InputHitTest(new Point(140,120))!=null,"painted character has a WPF hit target");
            var overflow=(FrameworkElement)((Window)Field("rocketArtwork")).Content;
            var clip=overflow.Clip;
            Check(clip!=null && !clip.FillContains(new Point(140+32,120+64)),"passive artwork excludes the core instead of repainting its alpha");
            var right=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Right){RoutedEvent=Mouse.PreviewMouseDownEvent};sprite.RaiseEvent(right);
            Check(!right.Handled && behavior.IsPomodoro,"reading does not consume the routed right button event");
            Call("UpdateTimerBubble",true);Advance(600);Call("UpdateTimerBubble",true);Pump();
            var mood=Field("mood");var moodType=mood.GetType();
            Check((bool)moodType.GetProperty("TimerVisible",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mood,null),"hover displays time bubble");
            string runningText=(string)moodType.GetProperty("TimerText",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mood,null);
            long renderAt=(long)type.GetProperty("Now",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window,null)+600;
            moodType.GetMethod("Render",BindingFlags.Instance|BindingFlags.NonPublic,null,new[] { typeof(long) },null).Invoke(mood,new object[] { renderAt });
            Screenshot(args[1]);Advance(1100);Call("UpdateTimerBubble",true);
            Check(runningText!=(string)moodType.GetProperty("TimerText",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mood,null),"hover digits count down");
            Click((MenuItem)Field("pomodoroPause"));Call("UpdateTimerBubble",true);
            string pausedText=(string)moodType.GetProperty("TimerText",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mood,null);
            Advance(6000);Call("UpdateTimerBubble",true);
            Check(pausedText==(string)moodType.GetProperty("TimerText",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mood,null),"paused hover freezes remaining time");
            Check(behavior.CurrentClip.Id=="pomodoro_pause","pause waits for full reading loop then places book");
            Advance(1761);Check(behavior.CurrentClip.Id=="pomodoro_paused","chin-rest loop displayed");
            Call("UpdateTimerBubble",false);Check(!(bool)moodType.GetProperty("TimerVisible",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mood,null),"leaving pet hides time immediately");
            Click((MenuItem)Field("pomodoroEnd"));FinishScene();
            Click(durations[20]);Call("ToggleVisibility");Check(!window.IsVisible,"pet hidden during timer");
            int completed=0;timer.Completed+=delegate {completed++;};Advance(1200001L);Call("OnTick",null,EventArgs.Empty);Call("OnTick",null,EventArgs.Empty);
            Check(timer.State==PomodoroState.Completed && completed==1 && !window.IsVisible,"hidden deadline completes once without forcing window visible");
            Check(((Forms.ToolStripMenuItem)Field("trayPomodoroStatus")).Text.Contains("已完成") && tray.Text.Contains("已完成"),"completion remains visible in tray status");
            Check(!((MenuItem)Field("pomodoroPause")).IsEnabled && !((MenuItem)Field("pomodoroEnd")).IsEnabled,"completed controls disabled");
            Check(!window.ShowInTaskbar && tray.Visible,"tray-only app preserved");
            FinishScene();Check(timer.State==PomodoroState.Completed,"natural completion retains timer result after exit animation");
            VerifyFortune(durations[20],args[1]);
            window.Close();window=null;
            File.WriteAllLines(Path.Combine(args[1],"pomodoro-window-tests.txt"),log);
            Console.WriteLine("PASS: "+checks+" pomodoro and nested-menu window checks.");return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e);File.WriteAllLines(Path.Combine(args[1],"pomodoro-window-tests.txt"),log);return 1; }
        finally { if(window!=null)window.Close(); }
    }
}
