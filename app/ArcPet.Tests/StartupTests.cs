using System;
using System.IO;
using System.Security;
using ArcPet.Settings;

internal sealed class FailingStartupStore : IStartupValueStore
{
    public bool DenyRead,DenyWrite,IgnoreWrite;
    public string Value;
    public string Read(){if(DenyRead)throw new SecurityException("read denied");return Value;}
    public void Write(string command){if(DenyWrite)throw new UnauthorizedAccessException("write denied");if(!IgnoreWrite)Value=command;}
    public void Delete(){if(DenyWrite)throw new IOException("delete denied");if(!IgnoreWrite)Value=null;}
}
internal static class StartupTests
{
    public static void Run(Action<bool,string> check)
    {
        string executable=Path.Combine(Path.GetTempPath(),"Arc Pet 中文","ArcPet.Preview.exe");
        var store=new MemoryStartupValueStore();var startup=new StartupRegistration(store,executable);
        bool enabled;string error;
        check(startup.TryRead(out enabled,out error) && !enabled && error==null && store.Command==null,"startup is off and reading does not register defaults");
        check(startup.Command=="\""+Path.GetFullPath(executable)+"\"","startup command quotes complete executable path with spaces and Unicode");
        check(startup.TrySet(true,out error) && error==null && store.Command==startup.Command,"enable registers exactly current executable without shell or extra arguments");
        check(startup.TryRead(out enabled,out error) && enabled,"read reflects enabled startup registration");
        var reopened=new StartupRegistration(store,executable);
        check(reopened.TryRead(out enabled,out error) && enabled,"startup persists independently of the window and JSON preferences");
        check(reopened.TrySet(false,out error) && store.Command==null,"disable removes own registration");
        check(reopened.TrySet(false,out error),"disabling absent entry is idempotent");
        store.Command="\"C:\\previous release\\ArcPet.Preview.exe\"";
        check(startup.TryRead(out enabled,out error) && enabled,"previous install registration is visible rather than silently overwritten on read");
        check(startup.TrySet(true,out error) && store.Command==startup.Command,"explicit enable refreshes a relocated executable");
        store.Command=null;check(startup.TryRead(out enabled,out error) && !enabled,"external registration removal is reflected");
        var failure=new FailingStartupStore();startup=new StartupRegistration(failure,executable);
        failure.DenyRead=true;
        check(!startup.TryRead(out enabled,out error) && !enabled && error!=null,"read denial is reported without crashing");
        failure.DenyRead=false;failure.DenyWrite=true;
        check(!startup.TrySet(true,out error) && error!=null && failure.Value==null,"write denial leaves startup disabled and reports failure");
        failure.Value="previous";
        check(!startup.TrySet(false,out error) && error!=null && failure.Value=="previous","delete denial preserves the existing registration");
        failure.DenyWrite=false;failure.IgnoreWrite=true;
        check(!startup.TrySet(true,out error) && error!=null,"verification detects ignored registration write");
        check(!startup.TrySet(false,out error) && error!=null,"verification detects ignored registration removal");
        bool invalid=false;try{new StartupRegistration(store,"bad\"path");}catch(ArgumentException){invalid=true;}
        check(invalid,"invalid quoting in executable path is rejected");
    }
}
