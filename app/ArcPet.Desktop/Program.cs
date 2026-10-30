using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using ArcPet.Animation;
using ArcPet.Behavior;
using ArcPet.Settings;

namespace ArcPet.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                bool diagnostic = args.Length==2 && (args[0]=="--smoke-test" || args[0]=="--interrupt-test" || args[0]=="--settings-test-write" || args[0]=="--settings-test-read");
                if(args.Length!=0 && !diagnostic)throw new ArgumentException("Unsupported diagnostic arguments.");
                string mode=diagnostic ? args[0] : null;
                string diagnosticOutput=diagnostic ? Path.GetFullPath(args[1]) : null;
                string settingsPath=diagnostic ? Path.Combine(diagnosticOutput,mode=="--smoke-test" ? "smoke-profile/settings.json" : mode=="--interrupt-test" ? "interrupt-profile/settings.json" : "restart-profile/settings.json") : PreferencesLocation.Resolve(AppDomain.CurrentDomain.BaseDirectory);
                var preferences=new JsonPreferencesStore(settingsPath);
                if(mode=="--smoke-test" || mode=="--interrupt-test" || mode=="--settings-test-write")
                {string error;if(!preferences.TrySave(new PetPreferences(),out error))throw new IOException(error);}
                string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
                var catalog = new JsonAnimationCatalog(assets);
                var clips = catalog.LoadAll();
                var player = new AnimationPlayer(new MonotonicPlaybackClock());
                var behavior = new PetBehaviorController(clips, new RandomSource());
                var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
                IStartupValueStore startupStore=diagnostic ? (IStartupValueStore)new MemoryStartupValueStore() : new WindowsStartupValueStore();
                var startup=new StartupRegistration(startupStore,System.Reflection.Assembly.GetExecutingAssembly().Location);
                var window=new PetWindow(player,behavior,clips,preferences,diagnosticOutput,mode,startup);
                app.MainWindow=window;
                app.Startup += delegate { window.Start(); };
                return app.Run();
            }
            catch (Exception exception)
            {
                MessageBox.Show("桌宠无法启动：\n" + exception.Message, "ArcPet", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
