using System.IO;

namespace OscLib.Config
{
    internal class ConfigFileWatcher
    {
        internal volatile bool IgnoreEvents = false;
        private ConfigFile File;
        private FileSystemWatcher Watcher;

        internal ConfigFileWatcher(ConfigFile config)
        {
            File = config;
            BeginInit();
        }

        internal void BeginInit()
        {
            if (Watcher != null)
                return;

            string filepath = File.GetFilePath();
            Watcher = new FileSystemWatcher(Path.GetDirectoryName(filepath));

            Watcher.NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size;

            Watcher.Changed += OnChanged;
            Watcher.Created += OnChanged;
            Watcher.Renamed += OnRenamed;
            Watcher.Error += OnError;

            Watcher.Filter = Path.GetFileName(filepath);
            Watcher.IncludeSubdirectories = false;
            Watcher.EnableRaisingEvents = true;
        }

        internal void EndInit()
        {
            if (Watcher == null)
                return;

            Watcher.Dispose();
            Watcher = null;
        }


        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (IgnoreEvents)
                return;

            if (File.TryLoad(false))
                File.OnFileModified_SafeInvoke();
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (IgnoreEvents)
                return;
            if (File.TryLoad(false))
                File.OnFileModified_SafeInvoke();
        }

        private void OnError(object sender, ErrorEventArgs e)
            => File.ReportError("watch settings", e.GetException());
    }
}
