using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace ArcPet.Settings
{
    public interface IStartupValueStore
    {
        string Read();
        void Write(string command);
        void Delete();
    }
    // This adapter touches only ArcPet's value in the current user's Run key.
    public sealed class WindowsStartupValueStore : IStartupValueStore
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ArcPet";
        public string Read()
        {
            using(var key=Registry.CurrentUser.OpenSubKey(KeyPath,false))
                return key==null ? null : key.GetValue(ValueName,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        public void Write(string command)
        {
            using(var key=Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if(key==null)throw new IOException("无法打开当前账户的启动设置。");
                key.SetValue(ValueName,command,RegistryValueKind.String);
            }
        }
        public void Delete()
        {
            using(var key=Registry.CurrentUser.OpenSubKey(KeyPath,true))
                if(key!=null)key.DeleteValue(ValueName,false);
        }
    }
    // Diagnostics use this store and never modify the real login configuration.
    public sealed class MemoryStartupValueStore : IStartupValueStore
    {
        public string Command {get;set;}
        public string Read(){return Command;}
        public void Write(string command){Command=command;}
        public void Delete(){Command=null;}
    }
    public sealed class StartupRegistration
    {
        private readonly IStartupValueStore store;
        public string Command {get;private set;}
        public StartupRegistration(IStartupValueStore store,string executable)
        {
            if(store==null)throw new ArgumentNullException("store");
            if(String.IsNullOrWhiteSpace(executable) || executable.IndexOf('"')>=0)
                throw new ArgumentException("Invalid executable path.","executable");
            this.store=store;Command="\""+Path.GetFullPath(executable)+"\"";
        }
        private static bool Expected(Exception e)
        {return e is IOException || e is UnauthorizedAccessException || e is SecurityException;}
        public bool TryRead(out bool enabled,out string error)
        {
            enabled=false;error=null;
            try{enabled=!String.IsNullOrWhiteSpace(store.Read());return true;}
            catch(Exception e){if(!Expected(e))throw;error=e.Message;return false;}
        }
        public bool TrySet(bool enabled,out string error)
        {
            error=null;
            try
            {
                if(enabled)store.Write(Command);else store.Delete();
                string actual=store.Read();
                if(enabled ? !String.Equals(actual,Command,StringComparison.Ordinal) : !String.IsNullOrEmpty(actual))
                    throw new IOException("系统未保存开机自启动设置。");
                return true;
            }
            catch(Exception e){if(!Expected(e))throw;error=e.Message;return false;}
        }
    }
}
