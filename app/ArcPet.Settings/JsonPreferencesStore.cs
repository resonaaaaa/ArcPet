using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ArcPet.Settings
{
    public interface IPreferencesStore
    {
        string FilePath { get; }
        string LoadStatus { get; }
        PetPreferences Load();
        bool TrySave(PetPreferences preferences,out string error);
    }
    public sealed class JsonPreferencesStore : IPreferencesStore
    {
        private bool invalidPrimary, readOnlyFutureVersion;
        public string FilePath { get; private set; }
        public string LoadStatus { get; private set; }
        public JsonPreferencesStore(string path) { FilePath=Path.GetFullPath(path);LoadStatus="not-loaded"; }
        private static PetPreferences Read(string path)
        {
            using(var stream=File.OpenRead(path))
            {
                var value=(PetPreferences)new DataContractJsonSerializer(typeof(PetPreferences)).ReadObject(stream);
                if(value==null || value.Version<1)throw new SerializationException("Invalid settings document.");
                return value.Normalize();
            }
        }
        private static bool ExpectedFailure(Exception e)
        {return e is IOException || e is UnauthorizedAccessException || e is SerializationException || e is FormatException || e is ArgumentException || e is NotSupportedException;}
        public PetPreferences Load()
        {
            invalidPrimary=false;readOnlyFutureVersion=false;
            bool exists=File.Exists(FilePath);
            if(exists)
            {
                try
                {
                    var value=Read(FilePath);
                    if(value.Version>1){readOnlyFutureVersion=true;LoadStatus="future-version-preserved";return new PetPreferences();}
                    LoadStatus="loaded";return value;
                }
                catch(Exception e){if(!ExpectedFailure(e))throw;invalidPrimary=true;}
            }
            if(File.Exists(FilePath+".bak"))
            {
                try
                {
                    var backup=Read(FilePath+".bak");
                    if(backup.Version==1){LoadStatus="backup-recovered";return backup;}
                }
                catch(Exception e){if(!ExpectedFailure(e))throw;}
            }
            LoadStatus=exists ? "defaults-invalid-file" : "defaults-first-run";
            return new PetPreferences();
        }
        public bool TrySave(PetPreferences preferences,out string error)
        {
            error=null;
            if(readOnlyFutureVersion){error="设置来自更新版本，已保留原文件。";return false;}
            string temporary=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var normalized=preferences.Normalize();normalized.Version=1;
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {new DataContractJsonSerializer(typeof(PetPreferences)).WriteObject(stream,normalized);stream.Flush(true);}
                if(File.Exists(FilePath))File.Replace(temporary,FilePath,invalidPrimary ? null : FilePath+".bak",true);
                else File.Move(temporary,FilePath);
                invalidPrimary=false;return true;
            }
            catch(Exception e){if(!ExpectedFailure(e))throw;error=e.Message;return false;}
            finally
            {
                try{if(File.Exists(temporary))File.Delete(temporary);}
                catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
    public static class PreferencesLocation
    {
        public static string Resolve(string executableDirectory)
        {
            var directory=new DirectoryInfo(Path.GetFullPath(executableDirectory));
            var root=directory.Parent==null ? null : directory.Parent.Parent;
            if(root!=null && File.Exists(Path.Combine(root.FullName,"manifest.json")) &&
               Directory.Exists(Path.Combine(root.FullName,"anime")) && Directory.Exists(Path.Combine(root.FullName,"app")))
                return Path.Combine(root.FullName,"data","settings.json");
            return Path.Combine(directory.FullName,"data","settings.json");
        }
    }
}
