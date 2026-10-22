using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Tomlet;
using Tomlet.Exceptions;
using Tomlet.Models;

namespace OscLib.Config
{
    public class ConfigFile
    {
        private static int UpdateRate = 100; // ms
        private ConfigFileWatcher Watcher;
        private TomlDocument Document = TomlDocument.CreateEmpty();
        private readonly object SyncRoot = new object();
        private bool RejectedFile;
        private bool Initialized;
        private string LastDiskText;
        public List<ConfigCategory> Categories = new List<ConfigCategory>();

        public Action OnFileModified;
        public void OnFileModified_SafeInvoke()
        {
            try { OnFileModified?.Invoke(); }
            catch (Exception ex) { ReportError("apply reloaded settings", ex); }
        }

        private string FilePath;
        public string GetFilePath()
            => FilePath;

        public ConfigFile(string filepath, bool useWatcher = true)
        {
            if (string.IsNullOrEmpty(filepath))
                throw new NullReferenceException(filepath);

            FilePath = filepath;
            if (useWatcher)
                Watcher = new ConfigFileWatcher(this);

            ConfigManager.AllConfigFiles.Add(this);
        }

        public void Load()
            => TryLoad(true);

        internal bool TryLoad(bool createIfMissing)
        {
            lock (SyncRoot)
            {
                // The base constructor starts the watcher before derived classes add categories.
                if (!createIfMissing && !Initialized)
                    return false;
                try
                {
                    if (!File.Exists(FilePath))
                    {
                        // Editors may temporarily remove/rename a file while replacing it.
                        // Only initial creation writes defaults; watcher callbacks never write.
                        if (!createIfMissing)
                            return false;
                        RejectedFile = false;
                        Save();
                        return !RejectedFile;
                    }

                    Thread.Sleep(UpdateRate);
                    string diskText = File.ReadAllText(FilePath);
                    TomlDocument document = new TomlParser().Parse(diskText);
                    var updates = new List<Action>();
                    bool hasCategory = Categories.Count == 0;
                    foreach (ConfigCategory category in Categories)
                    {
                        hasCategory |= document.ContainsKey(category.Name);
                        TomlValue table = document.ContainsKey(category.Name)
                            ? document.GetSubTable(category.Name)
                            : null;
                        updates.Add(category.PrepareLoad(table));
                    }
                    if (!hasCategory)
                        throw new FormatException("The file has no recognized settings categories; it may still be being written.");

                    foreach (Action update in updates)
                        update();
                    Document = document;
                    LastDiskText = diskText;
                    RejectedFile = false;
                    return true;
                }
                catch (Exception ex)
                {
                    RejectedFile = true;
                    ReportError("load settings; keeping defaults or the last valid values", ex);
                    return false;
                }
                finally
                {
                    if (createIfMissing)
                        Initialized = true;
                }
            }
        }

        public void Save()
        {
            lock (SyncRoot)
            {
                // ProcessExit/SaveAll must not overwrite a rejected user file.
                if (RejectedFile)
                    return;
                try
                {
                    foreach (ConfigCategory category in Categories)
                        Document.PutValue(category.Name, category.Save());

                    if (Watcher != null)
                        Watcher.IgnoreEvents = true;
                    string text = Document.SerializedValue;
                    // SaveAll can run before the watcher has processed a user's edit. Check
                    // immediately before writing and preserve pending edits or temporary renames.
                    bool exists = File.Exists(FilePath);
                    if ((exists && !string.Equals(File.ReadAllText(FilePath), LastDiskText, StringComparison.Ordinal))
                        || (!exists && LastDiskText != null))
                    {
                        Console.WriteLine($"[Config] Not saving '{FilePath}': the file changed outside the application; keeping the pending edit.");
                        return;
                    }
                    File.WriteAllText(FilePath, text);
                    LastDiskText = text;
                    Thread.Sleep(UpdateRate);
                }
                catch (Exception ex)
                {
                    RejectedFile = true;
                    ReportError("save settings", ex);
                }
                finally
                {
                    if (Watcher != null)
                        Watcher.IgnoreEvents = false;
                }
            }
        }

        internal void ReportError(string action, Exception ex)
            => Console.WriteLine($"[Config] Could not {action} for '{FilePath}': {ex.GetBaseException().Message}");

        private static string QuoteKey(string key) =>
          key.IndexOf('"') >= 0
              ? $"'{key}'"
              : $"\"{key}\"";

        internal void InsertIntoDocument(string category, string key, TomlValue value, bool should_inline = false)
        {
            if (!Document.ContainsKey(category))
                Document.PutValue(category, new TomlTable());

            try
            {
                var categoryTable = Document.GetSubTable(category);
                categoryTable.ForceNoInline = !should_inline;
                categoryTable.PutValue(QuoteKey(key), value);
            }
            catch (TomlTypeMismatchException) { }
            catch (TomlNoSuchValueException) { }
        }
    }
}
