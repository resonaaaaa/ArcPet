using System;
using System.Collections.Generic;
using System.IO;
using ArcPet.Behavior;
using ArcPet.Settings;

internal static class SettingsTests
{
    public static void Run(string assets,string scratch,Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetFullPath(scratch),"settings-tests-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file=Path.Combine(root,"profile","settings.json");
            var store=new JsonPreferencesStore(file);var first=store.Load();
            check(first.Scale==1 && first.Visible && first.SleepEnabled && first.Topmost && !first.Locked,"first run defaults");
            check(store.LoadStatus=="defaults-first-run" && !File.Exists(file),"load does not write user defaults prematurely");
            var saved=new PetPreferences { Scale=1.6,Left=80,Top=90,Locked=true,SleepEnabled=false,Visible=false,Topmost=false,Monitor="main",WorkLeft=0,WorkTop=0,WorkWidth=1920,WorkHeight=1040 };
            string error;check(store.TrySave(saved,out error) && error==null,"atomic settings save");
            var loaded=new JsonPreferencesStore(file).Load();
            check(loaded.Scale==1.6 && loaded.Left==80 && loaded.Top==90 && loaded.Locked && !loaded.SleepEnabled && !loaded.Visible && !loaded.Topmost,"all settings round trip");
            check(loaded.Monitor=="main" && loaded.WorkWidth==1920 && loaded.WorkHeight==1040,"monitor reference round trip");
            saved.Scale=0.8;check(store.TrySave(saved,out error) && File.Exists(file+".bak"),"subsequent save creates backup");
            File.WriteAllText(file,"{broken");var recovery=new JsonPreferencesStore(file);loaded=recovery.Load();
            check(loaded.Scale==1.6 && recovery.LoadStatus=="backup-recovered","corrupt primary recovers previous good settings");
            byte[] backup=File.ReadAllBytes(file+".bak");check(recovery.TrySave(loaded,out error),"recovered settings saved safely");
            check(Convert.ToBase64String(backup)==Convert.ToBase64String(File.ReadAllBytes(file+".bak")),"recovery does not overwrite good backup with corrupt primary");
            check(new JsonPreferencesStore(file).Load().Scale==1.6,"repaired primary readable");
            File.Delete(file);recovery=new JsonPreferencesStore(file);loaded=recovery.Load();
            check(loaded.Scale==1.6 && recovery.LoadStatus=="backup-recovered","missing primary recovers backup");
            File.WriteAllText(file,"{bad");File.WriteAllText(file+".bak","also bad");recovery=new JsonPreferencesStore(file);loaded=recovery.Load();
            check(loaded.Scale==1 && loaded.SleepEnabled && recovery.LoadStatus=="defaults-invalid-file","two broken files fall back without startup failure");
            check(recovery.TrySave(loaded,out error),"defaults can replace unusable settings");
            File.WriteAllText(file,"{\"version\":1,\"scale\":1.25}");loaded=new JsonPreferencesStore(file).Load();
            check(loaded.Scale==1.25 && loaded.Visible && loaded.SleepEnabled && loaded.Topmost && !loaded.Locked,"missing fields use compatibility defaults");
            File.WriteAllText(file,"{\"version\":99,\"scale\":1.8}");string future=File.ReadAllText(file);recovery=new JsonPreferencesStore(file);loaded=recovery.Load();
            check(recovery.LoadStatus=="future-version-preserved" && loaded.Scale==1,"unknown future schema safely detected");
            check(!recovery.TrySave(new PetPreferences(),out error) && File.ReadAllText(file)==future,"older program never overwrites future settings");
            string blocked=Path.Combine(root,"not-a-folder");File.WriteAllText(blocked,"keep");
            var failed=new JsonPreferencesStore(Path.Combine(blocked,"settings.json"));
            check(!failed.TrySave(new PetPreferences(),out error) && error!=null && File.ReadAllText(blocked)=="keep","unwritable location reports failure without damaging existing file");
            var normalized=new PetPreferences{Scale=9,Left=Double.NaN,Top=10}.Normalize();
            check(normalized.Scale==2 && normalized.Left==null && normalized.Top==null,"invalid coordinates rejected and scale bounded");
            check(new PetPreferences{Scale=Double.NaN}.Normalize().Scale==1,"nonfinite scale defaults to one");
            check(new PetPreferences{Scale=-3}.Normalize().Scale==0.4,"scale lower bound on restore");

            var areas=new List<DisplayArea>{new DisplayArea("main",0,0,1920,1040,true),new DisplayArea("left",-1600,0,1600,900,false)};
            var placement=PlacementPolicy.Restore(new PetPreferences(),areas);
            check(placement.Monitor=="main" && placement.Left==1564 && placement.Top==700,"first run bottom-right placement");
            saved=new PetPreferences{Scale=1.6,Left=80,Top=90,Locked=true,SleepEnabled=false,Visible=false,Monitor="main",WorkLeft=0,WorkTop=0,WorkWidth=1920,WorkHeight=1040};
            placement=PlacementPolicy.Restore(saved,areas);
            check(placement.Scale==1.6 && placement.Left==80 && placement.Top==90 && placement.Locked && !placement.Visible,"restore before applying lock preserves exact position");
            saved.Monitor="left";saved.Left=-1400;saved.Top=100;saved.WorkLeft=-1600;saved.WorkWidth=1600;saved.WorkHeight=900;
            placement=PlacementPolicy.Restore(saved,areas);
            check(placement.Left==-1400 && placement.Top==100,"negative-coordinate monitor preserved");
            placement=PlacementPolicy.Restore(saved,new List<DisplayArea>{areas[0]});
            check(placement.Monitor=="main" && placement.Left>=0 && placement.Left+320*placement.Scale<=1920,"unplugged monitor returns to visible desktop");
            saved=new PetPreferences{Scale=1.6,Left=80,Top=90,Monitor="main",WorkLeft=0,WorkTop=0,WorkWidth=1920,WorkHeight=1040};
            placement=PlacementPolicy.Restore(saved,new List<DisplayArea>{new DisplayArea("main",0,0,1280,720,true)});
            check(placement.Left>=0 && placement.Top>=0 && placement.Left+512<=1280 && placement.Top+512<=720,"resolution change stays fully visible");
            check(Math.Abs(placement.Left.Value-80.0/1408*768)<0.0001,"resolution change retains relative position");
            placement=PlacementPolicy.Restore(new PetPreferences{Scale=2,Left=9000,Top=9000},new List<DisplayArea>{new DisplayArea("small",0,0,400,300,true)});
            check(placement.Scale*320<=300 && placement.Left>=0 && placement.Top>=0,"small display shrinks oversized remembered window");
            placement=PlacementPolicy.Restore(new PetPreferences{Scale=1,Left=10,Top=20,Monitor="main",WorkLeft=0,WorkTop=0,WorkWidth=1920,WorkHeight=1040},new List<DisplayArea>{new DisplayArea("main",-1280,0,1280,720,true)});
            check(placement.Left>=-1280 && placement.Left+320<=0,"display-origin move maps saved position");
            var project=new DirectoryInfo(Path.GetDirectoryName(assets)).Parent.Parent;
            check(PreferencesLocation.Resolve(Path.GetDirectoryName(assets))==Path.Combine(project.FullName,"data","settings.json"),"project versions share root settings path");
            string portable=Path.Combine(root,"portable","ArcPet-v0.4");
            check(PreferencesLocation.Resolve(portable)==Path.Combine(portable,"data","settings.json"),"standalone distribution uses portable local data");
            bool uiFree=true;foreach(var name in typeof(JsonPreferencesStore).Assembly.GetReferencedAssemblies())
                uiFree &= name.Name!="PresentationFramework" && name.Name!="PresentationCore" && name.Name!="WindowsBase" && name.Name!="System.Windows.Forms";
            check(uiFree,"settings persistence and placement are UI independent");
            var layout=new PetLayout(80,90,1.6);layout.IsLocked=true;layout.MoveTo(500,500);
            check(layout.Scale==1.6 && layout.Left==80 && layout.Top==90,"restored layout locks only after placement");
        }
        finally
        {
            string absolute=Path.GetFullPath(root),allowed=Path.GetFullPath(scratch).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!absolute.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe test cleanup path");
            Directory.Delete(absolute,true);
        }
    }
}
