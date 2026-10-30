using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;
using Forms = System.Windows.Forms;

namespace ArcPet.Desktop
{
    internal sealed class PetWindow : Window
    {
        private readonly IAnimationPlayer player;
        private readonly PetBehaviorController behavior;
        private readonly FrameCache cache;
        private readonly Image sprite;
        private readonly MoodBubbleWindow mood;
        private readonly RocketArtworkWindow rocketArtwork;
        private readonly VerticalFlight flight=new VerticalFlight();
        private BitmapSource rocketFrame;
        private string flightMonitor;
        private readonly string smokeOutput;
        private readonly string diagnosticMode;
        private readonly IPreferencesStore preferencesStore;
        private readonly StartupRegistration startup;
        private System.Drawing.Icon petIcon;
        private readonly PreferencesSaveQueue preferencesWriter;
        private readonly HorizontalTravel travel;
        private readonly IDictionary<string,AnimationClip> clips;
        private readonly Dictionary<MovementMode,MenuItem> movementMenu=new Dictionary<MovementMode,MenuItem>();
        private readonly Dictionary<MovementMode,Forms.ToolStripMenuItem> trayMovement=new Dictionary<MovementMode,Forms.ToolStripMenuItem>();
        private string travelMonitor;
        private PetPreferences loadedPreferences;
        private bool settingsReady, saveWarningShown, shownOnStartup;
        private long saveAfter=-1;
        private readonly Stopwatch observed = new Stopwatch();
        private readonly DispatcherTimer timer;
        private readonly HashSet<int> idleSeen = new HashSet<int>();
        private readonly List<string> phaseLog = new List<string>();
        private readonly List<string> uiChecks = new List<string>();
        private PetLayout layout;
        private Forms.NotifyIcon tray;
        private MenuItem lockMenu, visibilityMenu, sleepMenu, startupMenu;
        private Forms.ToolStripMenuItem trayLock, trayVisibility, traySleep, trayStartup;
        private string gesture;
        private bool gestureMoved;
        private Point initialPointer;
        private double initialLeft, initialTop, initialScale, anchorX, anchorY;
        private bool started, finished;
        private int smokeStage;
        private long diagnosticTimeOffset;
        private string renderedAction;
        private AnimationClip diagnosticAutomaticClip;
        private long Now { get { return observed.ElapsedMilliseconds+diagnosticTimeOffset; } }
        public PetWindow(IAnimationPlayer player, PetBehaviorController behavior, IDictionary<string, AnimationClip> clips, IPreferencesStore preferencesStore, string smokeOutput, string diagnosticMode)
            : this(player,behavior,clips,preferencesStore,smokeOutput,diagnosticMode,
                new StartupRegistration(new MemoryStartupValueStore(),System.Reflection.Assembly.GetExecutingAssembly().Location)) { }
        public PetWindow(IAnimationPlayer player, PetBehaviorController behavior, IDictionary<string, AnimationClip> clips, IPreferencesStore preferencesStore, string smokeOutput, string diagnosticMode, StartupRegistration startup)
        {
            this.player=player;this.behavior=behavior;this.smokeOutput=smokeOutput;
            this.preferencesStore=preferencesStore;this.diagnosticMode=diagnosticMode;
            this.startup=startup;
            this.clips=clips;
            if(clips.ContainsKey("skateboard_left") && clips.ContainsKey("skateboard_right"))travel=new HorizontalTravel(clips["skateboard_left"],clips["skateboard_right"]);
            preferencesWriter=new PreferencesSaveQueue(preferencesStore);
            cache = new FrameCache(clips.Values);
            Title = "ArcPet 0.14 · 桌宠"; Width = 320; Height = 320;
            using(var stream=typeof(PetWindow).Assembly.GetManifestResourceStream("ArcPet.Icon"))
            {
                if(stream==null)throw new InvalidDataException("缺少猫姬图标资源。");
                petIcon=new System.Drawing.Icon(stream);
                Icon=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(petIcon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            }
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            sprite = new Image { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
            Content = sprite;
            mood=new MoodBubbleWindow(this,Path.Combine(Path.GetDirectoryName(typeof(PetWindow).Assembly.Location),"assets/moods"),new RandomSource());
            rocketArtwork=new RocketArtworkWindow(this);
            IsVisibleChanged+=delegate {mood.Controller.SetAvailable(IsVisible,Now);mood.Render(Now);rocketArtwork.Render(rocketFrame);};
            CreateContextMenu();
            MouseLeftButtonDown += OnPrimaryDown;
            MouseLeftButtonUp += OnPrimaryUp;
            MouseMove += OnPointerMove;
            PreviewMouseRightButtonDown += delegate { behavior.SecondaryDown(Now); };
            PreviewMouseDown += delegate(object sender,MouseButtonEventArgs e)
            {if(e.ChangedButton!=MouseButton.Left && e.ChangedButton!=MouseButton.Right && behavior.Touch(Now))e.Handled=true;};
            PreviewMouseWheel += delegate(object sender,MouseWheelEventArgs e){if(behavior.Touch(Now))e.Handled=true;};
            LostMouseCapture += delegate { if (gesture != null) EndGesture(true); };
            Deactivated += delegate { if (gesture != null) EndGesture(true); };
            KeyUp += delegate(object sender, KeyEventArgs e)
            { if (gesture == "resize" && (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)) EndGesture(true); };
            player.FrameChanged += OnFrameChanged;
            behavior.PlaybackRequested += OnPlaybackRequested;
            behavior.SkateRequested += OnSkateRequested;
            behavior.RocketRequested += OnRocketRequested;
            timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += OnTick;
            LocationChanged += delegate { ScheduleSave();mood.Render(Now);rocketArtwork.Render(rocketFrame); };
            SizeChanged += delegate { ScheduleSave();mood.Render(Now);rocketArtwork.Render(rocketFrame); };
            Closing += delegate { RestoreFlightOrigin();SavePreferences(); preferencesWriter.Flush(); };
            Closed += OnClosed;
        }
        private void CreateContextMenu()
        {
            var menu = new ContextMenu();
            lockMenu = new MenuItem { Header = "锁定", IsCheckable = true };
            lockMenu.Click += delegate { ToggleLock(); };
            visibilityMenu = new MenuItem(); visibilityMenu.Click += delegate { ToggleVisibility(); };
            sleepMenu = new MenuItem { IsCheckable = true }; sleepMenu.Click += delegate { ToggleSleep(); };
            startupMenu = new MenuItem { IsCheckable = true }; startupMenu.Click += delegate { ToggleStartup(); };
            var exit = new MenuItem { Header = "退出桌宠" }; exit.Click += delegate { Close(); };
            menu.Items.Add(lockMenu); menu.Items.Add(visibilityMenu); menu.Items.Add(sleepMenu);
            menu.Items.Add(new Separator());
            foreach(MovementMode mode in new[]{MovementMode.Long,MovementMode.Short,MovementMode.Off})
            {
                MovementMode selected=mode;var item=new MenuItem{Header=MovementLabel(mode),IsCheckable=true};
                item.Click+=delegate{SetMovementMode(selected);};movementMenu.Add(mode,item);menu.Items.Add(item);
            }
            menu.Items.Add(new Separator()); menu.Items.Add(startupMenu);
            menu.Items.Add(new Separator()); menu.Items.Add(exit);
            menu.Opened += delegate { behavior.Touch(Now); SyncMenus(); };
            ContextMenu = menu;
        }
        private void CreateTray()
        {
            tray = new Forms.NotifyIcon { Icon = petIcon, Text = "ArcPet 0.14 · 双击显示或隐藏桌宠", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            trayLock = new Forms.ToolStripMenuItem("锁定"); trayLock.Click += delegate { Dispatch(ToggleLock); };
            trayVisibility = new Forms.ToolStripMenuItem(); trayVisibility.Click += delegate { Dispatch(ToggleVisibility); };
            traySleep = new Forms.ToolStripMenuItem(); traySleep.Click += delegate { Dispatch(ToggleSleep); };
            trayStartup = new Forms.ToolStripMenuItem(); trayStartup.Click += delegate { Dispatch(ToggleStartup); };
            var exit = new Forms.ToolStripMenuItem("退出桌宠"); exit.Click += delegate { Dispatch(Close); };
            menu.Items.Add(trayLock); menu.Items.Add(trayVisibility); menu.Items.Add(traySleep);
            menu.Items.Add(new Forms.ToolStripSeparator());
            foreach(MovementMode mode in new[]{MovementMode.Long,MovementMode.Short,MovementMode.Off})
            {
                MovementMode selected=mode;var item=new Forms.ToolStripMenuItem(MovementLabel(mode));
                item.Click+=delegate{Dispatch(delegate{SetMovementMode(selected);});};trayMovement.Add(mode,item);menu.Items.Add(item);
            }
            menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(trayStartup);
            menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(exit);
            menu.Opening += delegate { behavior.Touch(Now); SyncMenus(); };
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatch(ToggleVisibility); };
            tray.MouseDown += delegate { Dispatch(delegate { behavior.Touch(Now); }); };
        }
        private void Dispatch(Action action) { Dispatcher.BeginInvoke(action); }
        private void SyncMenus()
        {
            bool enabled;string error;bool readable=startup.TryRead(out enabled,out error);
            startupMenu.IsEnabled=readable;startupMenu.IsChecked=readable && enabled;
            startupMenu.Header=readable ? (enabled ? "开机自启动：开启" : "开机自启动：关闭") : "开机自启动：无法读取";
            lockMenu.IsChecked = behavior.IsLocked;
            visibilityMenu.Header = IsVisible ? "隐藏桌宠" : "显示桌宠";
            sleepMenu.IsChecked = behavior.SleepEnabled;
            sleepMenu.Header = behavior.SleepEnabled ? "睡眠：启用" : "睡眠：关闭";
            foreach(var item in movementMenu)item.Value.IsChecked=item.Key==behavior.TravelMode;
            if (tray == null) return;
            trayStartup.Enabled=readable;trayStartup.Checked=readable && enabled;trayStartup.Text=(string)startupMenu.Header;
            trayLock.Checked = behavior.IsLocked;
            trayVisibility.Text = IsVisible ? "隐藏桌宠" : "显示桌宠";
            traySleep.Checked = behavior.SleepEnabled;
            traySleep.Text = behavior.SleepEnabled ? "睡眠：启用" : "睡眠：关闭";
            foreach(var item in trayMovement)item.Value.Checked=item.Key==behavior.TravelMode;
        }
        private static string MovementLabel(MovementMode mode)
        {return mode==MovementMode.Long ? "移动：长" : mode==MovementMode.Short ? "移动：短" : "移动：关闭";}
        private void ToggleStartup()
        {
            bool enabled;string error;
            if(!startup.TryRead(out enabled,out error) || !startup.TrySet(!enabled,out error))
                MessageBox.Show(this,"无法修改开机自启动设置：\n"+error,"ArcPet",MessageBoxButton.OK,MessageBoxImage.Information);
            SyncMenus();
        }
        private void SetMovementMode(MovementMode mode)
        {EndGesture(true);behavior.SetMovementMode(mode,Now);SyncMenus();RefreshTravelAvailability();SavePreferences();}
        private void ToggleLock()
        {
            EndGesture(true);
            behavior.SetLocked(!behavior.IsLocked, Now); layout.IsLocked = behavior.IsLocked;
            SyncMenus(); SavePreferences();
        }
        private void ToggleSleep() { behavior.SetSleepEnabled(!behavior.SleepEnabled, Now); SyncMenus(); SavePreferences(); }
        private void ToggleVisibility()
        {
            behavior.CancelRocket(Now);RestoreFlightOrigin();
            EndGesture(true); behavior.Touch(Now);
            ContextMenu.IsOpen = false;
            if (IsVisible) Hide(); else Show();
            SyncMenus(); SavePreferences();
        }
        private Point PointerInScreenDips(MouseEventArgs e)
        {
            Point p = PointToScreen(e.GetPosition(this));
            var source = PresentationSource.FromVisual(this);
            return source == null || source.CompositionTarget == null ? p : source.CompositionTarget.TransformFromDevice.Transform(p);
        }
        private void OnPrimaryDown(object sender, MouseButtonEventArgs e)
        {
            if (gesture != null || behavior.Touch(Now)) { e.Handled = true; return; }
            BeginPrimaryGesture(PointerInScreenDips(e),(Keyboard.Modifiers & ModifierKeys.Control)!=0);
            CaptureMouse(); e.Handled = true;
        }
        private void BeginPrimaryGesture(Point pointer,bool control)
        {
            gestureMoved = false;
            initialPointer = pointer;
            initialLeft = Left; initialTop = Top; initialScale = layout.Scale;
            anchorX = Left + Width * 135 / 320; anchorY = Top + Height * 292 / 320;
            if (behavior.IsLocked) { gesture = "click"; behavior.PrimaryDown(Now, initialPointer.X, initialPointer.Y); }
            else
            {
                gesture = control ? "resize" : "drag";
                if (control) behavior.CancelPress(Now);
                else behavior.PrimaryDown(Now, initialPointer.X, initialPointer.Y);
                behavior.SetBusy(true, Now);
            }
        }
        private void OnPointerMove(object sender, MouseEventArgs e)
        {
            if (gesture == null || gesture == "click") return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndGesture(true); return; }
            if (gesture == "resize" && (Keyboard.Modifiers & ModifierKeys.Control) == 0) { EndGesture(true); return; }
            Point current = PointerInScreenDips(e);
            UpdatePointerPosition(current); e.Handled = true;
        }
        private void UpdatePointerPosition(Point current)
        {
            if(gesture!="resize" && gesture!="drag")return;
            behavior.Touch(Now);
            double dx=current.X-initialPointer.X,dy=current.Y-initialPointer.Y;
            if(!gestureMoved && dx*dx+dy*dy>PetBehaviorController.DoubleClickDistance*PetBehaviorController.DoubleClickDistance)
            {gestureMoved=true;behavior.CancelPress(Now);}
            if (gesture == "resize") layout.ResizeFrom(initialScale, initialPointer.Y, current.Y, anchorX, anchorY);
            else layout.MoveTo(initialLeft + current.X - initialPointer.X, initialTop + current.Y - initialPointer.Y);
            ApplyLayout();
        }
        private void OnPrimaryUp(object sender, MouseButtonEventArgs e)
        { EndGesture(false); behavior.Touch(Now); e.Handled = true; }
        private void EndGesture(bool cancelled)
        {
            string previous = gesture; gesture = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (previous == "click") { if (cancelled) behavior.CancelPress(Now); else behavior.PrimaryUp(Now); }
            else if (previous != null)
            {
                behavior.SetBusy(false, Now);
                if(previous=="drag" && !cancelled && !gestureMoved)behavior.PrimaryUp(Now);
                else behavior.CancelPress(Now);
            }
            // Clicks do not change preferences. Never force disk I/O in input dispatch.
            if((previous=="drag" || previous=="resize") &&
                (Left!=initialLeft || Top!=initialTop || layout.Scale!=initialScale))SavePreferences();
        }
        private void ApplyLayout()
        { if (layout.IsLocked) return; Width = layout.Size; Height = layout.Size; Left = layout.Left; Top = layout.Top; }
        private IList<DisplayArea> GetDisplayAreas()
        {
            // A hidden HWND may still expose an identity composition matrix.
            // Use stable system-DPI coordinates before and after the first Show().
            var primary=Forms.Screen.PrimaryScreen.Bounds;
            double factorX=SystemParameters.PrimaryScreenWidth/primary.Width;
            double factorY=SystemParameters.PrimaryScreenHeight/primary.Height;
            Matrix transform=new Matrix(factorX,0,0,factorY,0,0);
            var areas=new List<DisplayArea>();
            foreach(var screen in Forms.Screen.AllScreens)
            {
                var rect=screen.WorkingArea;
                Point start=transform.Transform(new Point(rect.Left,rect.Top));
                Point end=transform.Transform(new Point(rect.Right,rect.Bottom));
                areas.Add(new DisplayArea(screen.DeviceName,start.X,start.Y,end.X-start.X,end.Y-start.Y,screen.Primary));
            }
            return areas;
        }
        public void Start()
        {
            if(started)return;started=true;
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            loadedPreferences=PlacementPolicy.Restore(preferencesStore.Load(),GetDisplayAreas());
            layout=new PetLayout(loadedPreferences.Left.Value,loadedPreferences.Top.Value,loadedPreferences.Scale);
            Topmost=loadedPreferences.Topmost;ApplyLayout();observed.Start();CreateTray();
            behavior.Start(Now);behavior.SetSleepEnabled(loadedPreferences.SleepEnabled,Now);
            behavior.SetLocked(loadedPreferences.Locked,Now);layout.IsLocked=behavior.IsLocked;
            behavior.SetMovementMode(MovementOptions.Parse(loadedPreferences.Movement),Now);
            if(loadedPreferences.Visible){shownOnStartup=true;Show();}
            settingsReady=true;SyncMenus();timer.Start();
            if(preferencesStore.LoadStatus=="backup-recovered" || preferencesStore.LoadStatus=="defaults-invalid-file" || preferencesStore.LoadStatus=="future-version-preserved")
                NotifySettingsIssue("读取设置时已采用安全恢复。详情："+preferencesStore.LoadStatus);
        }
        private void ScheduleSave() { if(settingsReady && !flight.IsActive)saveAfter=Now+400; }
        private PetPreferences CapturePreferences()
        {
            double savedLeft=flight.IsActive?flight.OriginLeft:Left,savedTop=flight.IsActive?flight.OriginTop:Top;
            DisplayArea area=PlacementPolicy.Choose(GetDisplayAreas(),savedLeft,savedTop,layout.Size);
            return new PetPreferences {Scale=layout.Scale,Left=savedLeft,Top=savedTop,Locked=behavior.IsLocked,
                SleepEnabled=behavior.SleepEnabled,Visible=IsVisible,Topmost=Topmost,Monitor=area.Name,
                Movement=MovementOptions.Setting(behavior.TravelMode),
                WorkLeft=area.Left,WorkTop=area.Top,WorkWidth=area.Width,WorkHeight=area.Height};
        }
        private void NotifySettingsIssue(string message)
        {
            if(saveWarningShown || tray==null || smokeOutput!=null)return;saveWarningShown=true;
            tray.BalloonTipTitle="ArcPet 设置";tray.BalloonTipText=message;tray.ShowBalloonTip(5000);
        }
        private void SavePreferences()
        {
            if(!settingsReady || layout==null)return;saveAfter=-1;
            preferencesWriter.Enqueue(CapturePreferences());
        }
        private void OnPlaybackRequested(object sender, PlaybackRequestedEventArgs e)
        {
            if(flight.IsActive && behavior.Phase!="rocket_launch" && behavior.Phase!="rocket_flight")RestoreFlightOrigin();
            // Behavior settles priorities first. The overlay only observes the
            // chosen clip, so it can never pause or enqueue the pet's playback.
            mood.Controller.Enter(behavior.Phase,e.Clip.Id,e.Clip.Id=="think" ? clips["think"].DurationMilliseconds : 0,Now);
            mood.Render(Now);
            if(travel!=null && travel.IsActive && behavior.Phase!="skate")
            {travel.Stop();travelMonitor=null;SavePreferences();}
            if (smokeOutput != null) phaseLog.Add(Now.ToString(CultureInfo.InvariantCulture) + ":" + behavior.Phase);
            player.Play(e.Clip);
        }
        private void OnFrameChanged(object sender, AnimationFrameChangedEventArgs e)
        {
            BitmapSource frame=cache.Get(e.Frame.AssetPath);
            rocketFrame=frame.PixelHeight>320 ? frame : null;
            if(rocketFrame==null)sprite.Source=frame;
            sprite.Opacity=rocketFrame==null?1:0;rocketArtwork.Render(rocketFrame);renderedAction=e.Clip.Id;
            if (e.Clip.Id == "idle") idleSeen.Add(e.FrameIndex);
        }
        private void OnTick(object sender, EventArgs e)
        {
            RefreshTravelAvailability();
            behavior.Tick(Now);
            UpdateTravel();
            UpdateFlight();
            mood.Controller.Tick(Now);mood.Render(Now);
            if (IsVisible) player.Update();
            if(saveAfter>=0 && Now>=saveAfter)SavePreferences();
            string saveError=preferencesWriter.TakeError();
            if(saveError!=null)NotifySettingsIssue("设置未能保存，本次仍可继续使用。"+saveError);
            if(diagnosticMode=="--smoke-test")SmokeTick();
            else if(diagnosticMode=="--interrupt-test")InterruptTick();
            else if(diagnosticMode=="--settings-test-write" || diagnosticMode=="--settings-test-read")SettingsRestartTick();
        }
        private TravelArea GetTravelArea(string monitor,out string selected)
        {
            selected=null;if(travel==null || layout==null)return null;
            var areas=GetDisplayAreas();DisplayArea area=null;
            if(monitor==null)area=PlacementPolicy.Choose(areas,Left,Top,layout.Size);
            else foreach(var candidate in areas)if(candidate.Name==monitor){area=candidate;break;}
            if(area==null)return null;selected=area.Name;
            Rect footprint=cache.VisibleBounds(clips["skateboard_left"]);footprint.Union(cache.VisibleBounds(clips["skateboard_right"]));
            double pixelToDip=SystemParameters.PrimaryScreenWidth/Forms.Screen.PrimaryScreen.Bounds.Width;
            // Keep the full transparent window inside the area as well, so restoring
            // its persisted position never has to shift it back onto the display.
            double margin=100*pixelToDip;
            return new TravelArea(area.Left,area.Left+area.Width-layout.Size,
                Math.Max(0,margin-footprint.Left*layout.Scale),Math.Max(0,margin-(layout.Size-footprint.Right*layout.Scale)));
        }
        private double CharacterWidth {get{return cache.VisibleBounds(clips["idle"]).Width*layout.Scale;}}
        private void RefreshTravelAvailability()
        {
            string monitor;TravelArea area=GetTravelArea(travelMonitor,out monitor);
            behavior.TravelAvailable=IsVisible && layout!=null && travel!=null && HorizontalTravel.CanStart(area,Left,CharacterWidth);
        }
        private void OnSkateRequested(object sender,EventArgs e)
        {
            if(travel==null || behavior.IsLocked || !IsVisible)return;
            string monitor;TravelArea area=GetTravelArea(null,out monitor);
            if(!travel.Start(behavior.TravelMode,area,Left,Top,CharacterWidth,Now))return;
            travelMonitor=monitor;behavior.BeginSkating(travel.Direction,Now);
            if(behavior.Phase!="skate"){travel.Stop();travelMonitor=null;}
        }
        private void UpdateTravel()
        {
            if(travel==null || !travel.IsActive)return;
            if(behavior.IsLocked || !IsVisible){behavior.StopSkating(Now);return;}
            string monitor;TravelStep step=travel.Update(Now,GetTravelArea(travelMonitor,out monitor));
            layout.MoveTo(step.Left,step.Top);ApplyLayout();
            if(step.Finished){travelMonitor=null;behavior.StopSkating(Now);SavePreferences();}
            else if(step.DirectionChanged)behavior.ChangeSkateDirection(travel.Direction,Now);
        }
        private void OnRocketRequested(object sender,EventArgs e)
        {
            if(!IsVisible){behavior.CompleteRocket(Now);return;}
            DisplayArea area=PlacementPolicy.Choose(GetDisplayAreas(),Left,Top,Width);flightMonitor=area.Name;
            if(!flight.Start(Left,Top,area.Top,Now))behavior.CompleteRocket(Now);
        }
        private void RestoreFlightOrigin()
        {
            if(!flight.IsActive)return;
            // Keep active while restoring: location notifications must not save
            // the transient point between the old and restored coordinates.
            Left=flight.OriginLeft;Top=flight.OriginTop;flight.Stop();flightMonitor=null;
        }
        private void UpdateFlight()
        {
            if(!flight.IsActive)return;
            if(!IsVisible){RestoreFlightOrigin();behavior.CancelRocket(Now);return;}
            double screenTop=Double.NaN;
            foreach(var area in GetDisplayAreas())if(area.Name==flightMonitor){screenTop=area.Top;break;}
            FlightStep step=flight.Update(Now,screenTop);Left=step.Left;Top=step.Top;
            if(step.Finished){flightMonitor=null;behavior.CompleteRocket(Now);}
        }
        private void SettingsRestartTick()
        {
            if(Now<500 || finished)return;finished=true;
            Directory.CreateDirectory(smokeOutput);
            if(diagnosticMode=="--settings-test-write")
            {
                DisplayArea area=PlacementPolicy.Choose(GetDisplayAreas(),Left,Top,Width);
                layout=new PetLayout(area.Left+80,area.Top+90,1.6);ApplyLayout();
                behavior.SetSleepEnabled(false,Now);behavior.SetLocked(true,Now);layout.IsLocked=true;
                behavior.SetMovementMode(MovementMode.Short,Now);
                Topmost=false;Hide();SyncMenus();SavePreferences();
                preferencesWriter.Flush();
                File.Copy(preferencesStore.FilePath,Path.Combine(smokeOutput,"settings-expected.json"),true);
                File.WriteAllText(Path.Combine(smokeOutput,"settings-write.json"),"{\"saved\":true,\"scale\":1.6,\"hidden\":true}");
            }
            else
            {
                var snapshot=CapturePreferences();
                var expected=new JsonPreferencesStore(Path.Combine(smokeOutput,"settings-expected.json")).Load();
                bool passed=Math.Abs(snapshot.Scale-1.6)<0.0001 && Math.Abs(Width-512)<0.001 && snapshot.Locked && !snapshot.SleepEnabled &&
                    !snapshot.Visible && !snapshot.Topmost && !Topmost && !shownOnStartup && Math.Abs(Left-expected.Left.Value)<1 && Math.Abs(Top-expected.Top.Value)<1 &&
                    snapshot.WorkWidth==expected.WorkWidth && snapshot.WorkHeight==expected.WorkHeight &&
                    sleepMenu.IsChecked==false && (string)sleepMenu.Header=="睡眠：关闭" && lockMenu.IsChecked==true && tray.Visible &&
                    behavior.TravelMode==MovementMode.Short && snapshot.Movement=="short" && movementMenu[MovementMode.Short].IsChecked && trayMovement[MovementMode.Short].Checked;
                string report=String.Format(CultureInfo.InvariantCulture,
                    "{{\"passed\":{0},\"scale\":{1},\"width\":{2},\"left\":{3},\"top\":{4},\"locked\":{5},\"sleep_enabled\":{6},\"visible\":{7},\"shown_on_startup\":{8},\"load_status\":\"{9}\"}}",
                    passed.ToString().ToLowerInvariant(),snapshot.Scale,Width,Left,Top,snapshot.Locked.ToString().ToLowerInvariant(),snapshot.SleepEnabled.ToString().ToLowerInvariant(),snapshot.Visible.ToString().ToLowerInvariant(),shownOnStartup.ToString().ToLowerInvariant(),preferencesStore.LoadStatus);
                File.WriteAllText(Path.Combine(smokeOutput,"settings-read.json"),report);
            }
            Close();
        }
        private void InterruptTick()
        {
            // Advance only the isolated diagnostic clock; real settings/timers are untouched.
            long real=observed.ElapsedMilliseconds;
            if(smokeStage==0 && real>=300)
            {
                behavior.SetLocked(true,Now);diagnosticTimeOffset+=90001;behavior.Tick(Now);
                diagnosticAutomaticClip=behavior.CurrentClip;
                CheckUi(behavior.Phase=="automatic" && renderedAction==diagnosticAutomaticClip.Id,"automatic image started");smokeStage++;
            }
            else if(smokeStage==1 && real>=700)
            {
                behavior.PrimaryDown(Now);
                CheckUi(behavior.Phase=="automatic" && Object.ReferenceEquals(behavior.CurrentClip,diagnosticAutomaticClip) && renderedAction==diagnosticAutomaticClip.Id,"first press preserves random image");smokeStage++;
            }
            else if(smokeStage==2 && real>=800)
            {
                behavior.PrimaryUp(Now);
                CheckUi(behavior.Phase=="automatic" && Object.ReferenceEquals(behavior.CurrentClip,diagnosticAutomaticClip) && renderedAction==diagnosticAutomaticClip.Id,"single release does not cancel or jump");smokeStage++;
            }
            else if(smokeStage==3 && real>=1000)
            {
                behavior.SecondaryDown(Now);
                CheckUi(behavior.Phase=="automatic" && Object.ReferenceEquals(behavior.CurrentClip,diagnosticAutomaticClip) && renderedAction==diagnosticAutomaticClip.Id,"right click preserves random image");smokeStage++;
            }
            else if(smokeStage==4 && real>=1100)
            {
                behavior.SetBusy(true,Now);
                CheckUi(behavior.Phase=="automatic" && Object.ReferenceEquals(behavior.CurrentClip,diagnosticAutomaticClip),"layout activity does not cancel random action");smokeStage++;
            }
            else if(smokeStage==5 && real>=1200)
            {
                behavior.SetBusy(false,Now);
                CheckUi(behavior.Phase=="automatic" && Object.ReferenceEquals(behavior.CurrentClip,diagnosticAutomaticClip),"ending layout activity does not restart random action");smokeStage++;
            }
            else if(smokeStage==6 && real>=1600){behavior.PrimaryDown(Now);smokeStage++;}
            else if(smokeStage==7 && real>=1640)
            {behavior.PrimaryUp(Now);CheckUi(behavior.Phase=="automatic","first half of double click does not preempt");smokeStage++;}
            else if(smokeStage==8 && real>=1750)
            {behavior.PrimaryDown(Now);CheckUi(behavior.Phase=="automatic","second press waits for gesture qualification");smokeStage++;}
            else if(smokeStage==9 && real>=1790)
            {
                behavior.PrimaryUp(Now);
                CheckUi(behavior.Phase=="automatic" && renderedAction=="dance","qualified double click immediately replaces random image with dance");smokeStage++;
            }
            else if(smokeStage==10 && real>=3600)
            {
                diagnosticTimeOffset+=12001;behavior.Tick(Now);
                diagnosticTimeOffset+=90001;behavior.Tick(Now);diagnosticAutomaticClip=behavior.CurrentClip;
                CheckUi(behavior.Phase=="automatic","fresh random action started after completed interaction");smokeStage++;
            }
            else if(smokeStage==11 && real>=3900){behavior.PrimaryDown(Now);smokeStage++;}
            else if(smokeStage==12 && real>=4100)
            {
                CheckUi(behavior.Phase=="automatic" && Object.ReferenceEquals(behavior.CurrentClip,diagnosticAutomaticClip) && renderedAction==diagnosticAutomaticClip.Id,"shorter than 500ms hold keeps random playback");smokeStage++;
            }
            else if(smokeStage==13 && real>=4500)
            {
                CheckUi(behavior.Phase=="down" && renderedAction=="click_down","qualified long press immediately replaces random image");smokeStage++;
            }
            else if(smokeStage==14 && real>=4550){behavior.PrimaryUp(Now);smokeStage++;}
            if(real<7000 || finished)return;finished=true;
            CheckUi(behavior.Phase=="idle" && renderedAction=="idle","full descent and release complete without old automatic resumption");
            Directory.CreateDirectory(smokeOutput);
            string report=String.Format(CultureInfo.InvariantCulture,
                "{{\"real_elapsed_ms\":{0},\"final_phase\":\"{1}\",\"rendered_action\":\"{2}\",\"checks\":[\"{3}\"],\"phases\":[\"{4}\"]}}",
                real,behavior.Phase,renderedAction,String.Join("\",\"",uiChecks),String.Join("\",\"",phaseLog));
            File.WriteAllText(Path.Combine(smokeOutput,"interrupt-smoke.json"),report);Close();
        }
        private void CheckUi(bool condition, string name) { uiChecks.Add((condition ? "PASS:" : "FAIL:") + name); }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);
        private void SmokeTick()
        {
            // Diagnostic mode drives our own adapter/model without injecting OS input.
            long t = Now;
            if (smokeStage == 0 && t >= 500)
            {
                layout.ResizeFrom(1,0,-120,Left+135,Top+292); ApplyLayout();
                CheckUi(Math.Abs(Width-480)<0.01,"upward scale 1.5x");
                CheckUi(sleepMenu.IsChecked==true && (string)sleepMenu.Header=="睡眠：启用" && traySleep.Checked && traySleep.Text=="睡眠：启用","enabled sleep labels checked in both menus");
                ToggleSleep();
                CheckUi(sleepMenu.IsChecked==false && (string)sleepMenu.Header=="睡眠：关闭" && !traySleep.Checked && traySleep.Text=="睡眠：关闭","disabled sleep labels unchecked in both menus");
                ToggleSleep();
                int extendedStyle=GetWindowLong(new System.Windows.Interop.WindowInteropHelper(this).Handle,-20);
                CheckUi(!ShowInTaskbar && (extendedStyle & 0x40000)==0,"no taskbar application style");
                CheckUi(tray.Visible,"tray entry remains available"); smokeStage++;
            }
            else if (smokeStage == 1 && t >= 1000)
            { layout.ResizeFrom(1,0,5000,Left+Width*135/320,Top+Height*292/320); ApplyLayout(); CheckUi(Math.Abs(Width-128)<0.01,"minimum 0.4x"); smokeStage++; }
            else if (smokeStage == 2 && t >= 1500)
            { layout.ResizeFrom(1,0,-5000,Left+Width*135/320,Top+Height*292/320); ApplyLayout(); CheckUi(Math.Abs(Width-640)<0.01,"maximum 2x"); smokeStage++; }
            else if (smokeStage == 3 && t >= 2000)
            {
                ToggleLock(); double oldLeft=Left, oldTop=Top;
                layout.MoveTo(0,0); layout.ResizeFrom(1,0,900,0,0); ApplyLayout();
                CheckUi(Width==640 && Left==oldLeft && Top==oldTop,"lock prevents size and position changes"); smokeStage++;
            }
            else if (smokeStage == 4 && t >= 2400) { behavior.PrimaryDown(t); smokeStage++; }
            else if (smokeStage == 5 && t >= 2500)
            { behavior.PrimaryUp(t); CheckUi(behavior.Phase=="idle","single click does not jump"); smokeStage++; }
            else if (smokeStage == 6 && t >= 2600) { behavior.PrimaryDown(t); smokeStage++; }
            else if (smokeStage == 7 && t >= 2700)
            { behavior.PrimaryUp(t); CheckUi(behavior.Phase=="automatic" && renderedAction=="dance","double click starts dance"); smokeStage++; }
            else if (smokeStage == 8 && t >= 4300) { ToggleVisibility(); CheckUi(!IsVisible && timer.IsEnabled,"hidden window keeps timer alive"); smokeStage++; }
            else if (smokeStage == 9 && t >= 4800) { ToggleVisibility(); CheckUi(IsVisible,"restore hidden window"); smokeStage++; }
            else if (smokeStage == 10 && t >= 5100)
            {
                ToggleLock(); layout.ResizeFrom(1,0,0,Left+Width*135/320,Top+Height*292/320); ApplyLayout();
                CheckUi(Width==320,"restore 1x"); smokeStage++;
            }
            if (t < 7000 || finished) return;
            finished = true; Directory.CreateDirectory(smokeOutput);
            var bitmap = new RenderTargetBitmap(320,320,96,96,PixelFormats.Pbgra32); bitmap.Render(sprite);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream=File.Create(Path.Combine(smokeOutput,"desktop-render.png"))) encoder.Save(stream);
            string report=String.Format(CultureInfo.InvariantCulture,
                "{{\"elapsed_ms\":{0},\"idle_frames_seen\":{1},\"final_phase\":\"{2}\",\"checks\":[\"{3}\"],\"phases\":[\"{4}\"]}}",
                Now,idleSeen.Count,behavior.Phase,String.Join("\",\"",uiChecks),String.Join("\",\"",phaseLog));
            File.WriteAllText(Path.Combine(smokeOutput,"desktop-smoke.json"),report); Close();
        }
        private void OnClosed(object sender, EventArgs e)
        {
            timer.Stop(); timer.Tick -= OnTick;
            mood.Close();
            rocketArtwork.Close();
            behavior.PlaybackRequested -= OnPlaybackRequested;
            behavior.SkateRequested -= OnSkateRequested;
            behavior.RocketRequested -= OnRocketRequested;
            player.FrameChanged -= OnFrameChanged; player.Stop();
            if (tray != null) { tray.Visible=false; if(tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Dispose(); tray.Dispose(); }
            if(petIcon!=null){petIcon.Dispose();petIcon=null;}
        }
    }
}
