using System;
using System.Threading;

namespace ArcPet.Settings
{
    // Capture immutable snapshots on the UI thread; serialize disk work off it.
    public sealed class PreferencesSaveQueue
    {
        private readonly IPreferencesStore store;
        private readonly object gate=new object();
        private PetPreferences pending,lastRequested;
        private bool running;
        private string lastError;
        public PreferencesSaveQueue(IPreferencesStore store)
        {if(store==null)throw new ArgumentNullException("store");this.store=store;}
        private static bool Same(PetPreferences a,PetPreferences b)
        {
            return a!=null && b!=null && a.Version==b.Version && a.Scale==b.Scale && a.Left==b.Left && a.Top==b.Top &&
                a.Locked==b.Locked && a.SleepEnabled==b.SleepEnabled && a.Visible==b.Visible && a.Topmost==b.Topmost &&
                a.Movement==b.Movement && a.Monitor==b.Monitor && a.WorkLeft==b.WorkLeft && a.WorkTop==b.WorkTop && a.WorkWidth==b.WorkWidth && a.WorkHeight==b.WorkHeight;
        }
        public void Enqueue(PetPreferences snapshot)
        {
            if(snapshot==null)throw new ArgumentNullException("snapshot");
            var copy=snapshot.Normalize();
            lock(gate)
            {
                if(Same(copy,lastRequested))return;
                pending=copy;lastRequested=copy;
                if(running)return;
                running=true;ThreadPool.QueueUserWorkItem(delegate {Drain();});
            }
        }
        private void Drain()
        {
            while(true)
            {
                PetPreferences snapshot;
                lock(gate)
                {
                    if(pending==null){running=false;Monitor.PulseAll(gate);return;}
                    snapshot=pending;pending=null;
                }
                string error=null;bool saved;
                try{saved=store.TrySave(snapshot,out error);}
                catch(Exception e){saved=false;error=e.Message;}
                if(!saved)lock(gate)
                {
                    lastError=error ?? "设置未能保存。";
                    if(Same(lastRequested,snapshot))lastRequested=null;
                }
            }
        }
        public string TakeError()
        {lock(gate){string error=lastError;lastError=null;return error;}}
        // Called only for orderly shutdown or explicit persistence diagnostics.
        public void Flush(){lock(gate){while(running)Monitor.Wait(gate);}}
    }
}
