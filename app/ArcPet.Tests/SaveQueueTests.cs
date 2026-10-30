using System;
using System.Collections.Generic;
using System.Threading;
using ArcPet.Settings;

internal sealed class ControlledPreferences : IPreferencesStore
{
    public readonly ManualResetEvent Entered=new ManualResetEvent(false),Proceed=new ManualResetEvent(false);
    public readonly List<double> Scales=new List<double>();
    public bool Fail;
    public int Writes,ThreadId;
    public string FilePath {get{return "memory-only";}}
    public string LoadStatus {get{return "loaded";}}
    public PetPreferences Load(){return new PetPreferences();}
    public bool TrySave(PetPreferences value,out string error)
    {
        ThreadId=Thread.CurrentThread.ManagedThreadId;Interlocked.Increment(ref Writes);Entered.Set();
        if(!Proceed.WaitOne(5000))throw new Exception("Test write timed out");
        lock(Scales)Scales.Add(value.Scale);
        error=Fail ? "deliberate failure" : null;return !Fail;
    }
}
internal static class SaveQueueTests
{
    public static void Run(Action<bool,string> check)
    {
        var store=new ControlledPreferences();var queue=new PreferencesSaveQueue(store);int caller=Thread.CurrentThread.ManagedThreadId;
        try
        {
            var first=new PetPreferences{Scale=1.1};queue.Enqueue(first);
            check(store.Entered.WaitOne(2000),"background writer starts without blocking caller");first.Scale=1.9;
            check(store.ThreadId!=caller,"disk writer runs outside caller thread");
            queue.Enqueue(new PetPreferences{Scale=1.2});queue.Enqueue(new PetPreferences{Scale=1.3});
            store.Proceed.Set();queue.Flush();
            check(store.Writes==2 && store.Scales[0]==1.1 && store.Scales[1]==1.3,"latest pending snapshot wins without mutating an in-flight save");
            queue.Enqueue(new PetPreferences{Scale=1.3});queue.Flush();
            check(store.Writes==2,"unchanged preferences do not write again");
            queue.Enqueue(new PetPreferences{Scale=1.3,Locked=true});queue.Flush();
            check(store.Writes==3,"non-layout setting changes still persist");
            store.Fail=true;queue.Enqueue(new PetPreferences{Scale=1.4});queue.Flush();
            check(queue.TakeError()=="deliberate failure" && queue.TakeError()==null,"failure reported once without blocking input");
            store.Fail=false;queue.Enqueue(new PetPreferences{Scale=1.4});queue.Flush();
            check(store.Writes==5 && store.Scales[4]==1.4,"failed snapshot can retry unchanged values");
            check(queue.TakeError()==null,"successful retry leaves no new error");
            queue.Enqueue(new PetPreferences{Scale=1.4,Movement="long"});queue.Flush();
            queue.Enqueue(new PetPreferences{Scale=1.4,Movement="short"});queue.Flush();
            check(store.Writes==7,"movement-mode-only changes are not lost to save deduplication");
        }
        finally{store.Proceed.Set();queue.Flush();store.Entered.Close();store.Proceed.Close();}
    }
}
