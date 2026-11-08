using System;
using System.IO;
using System.Reflection;
using System.Windows;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;

internal sealed class InputPreviewPreferences : IPreferencesStore
{
    public string FilePath { get { return "memory-only-input-preview.json"; } }
    public string LoadStatus { get { return "loaded"; } }
    public PetPreferences Load() { return new PetPreferences { Left=400,Top=250,SleepEnabled=false,Scale=1,Locked=true }; }
    public bool TrySave(PetPreferences value,out string error) { error=null;return true; }
}
// Opt-in visual QA host. Same production window/event handlers and in-memory
// settings; only taskbar visibility differs so computer-use can select it.
internal static class PomodoroInputPreview
{
    [STAThread] public static void Main(string[] args)
    {
        string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ArcPet.Preview.exe");var asm=Assembly.LoadFrom(exe);
        var clips=new JsonAnimationCatalog(Path.Combine(Path.GetDirectoryName(exe),"assets")).LoadAll();
        var app=new Application();
        var window=(Window)Activator.CreateInstance(asm.GetType("ArcPet.Desktop.PetWindow",true),
            new object[] { new AnimationPlayer(new MonotonicPlaybackClock()),new PetBehaviorController(clips,new RandomSource()),clips,new InputPreviewPreferences(),null,null });
        window.ShowInTaskbar=true;window.Title="ArcPet · 输入修复验收";app.MainWindow=window;
        app.Startup+=delegate { window.GetType().GetMethod("Start").Invoke(window,null); window.GetType().GetMethod("StartPomodoro",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[] {20}); };
        app.Run();
    }
}
