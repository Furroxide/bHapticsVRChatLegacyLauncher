using System;
using System.IO;

namespace OscLib.VRChat
{
    public static class VRCAvatarConfig
    {
        public static void RemoveFile(string avatarID)
        {
            string appdataFolder = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}Low";
            string VRChatFolder = Path.Combine(appdataFolder, "VRChat", "VRChat", "OSC");
            RemoveFile(avatarID, VRChatFolder);
        }

        // OSC is untrusted input. Accept only an actual avatar ID, never a path:
        // Path.Combine otherwise allows an incoming avatar change to delete an
        // arbitrary .json file outside the avatar configuration folder.
        internal static void RemoveFile(string avatarID, string oscFolder)
        {
            if (avatarID == null || !avatarID.StartsWith("avtr_", StringComparison.Ordinal)
                || avatarID.Length != 41 || !Guid.TryParseExact(avatarID.Substring(5), "D", out _))
                return;

            string[] userFolders;
            try
            {
                userFolders = Directory.GetDirectories(oscFolder);
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            foreach (string folderPath in userFolders)
            {
                string filePath = Path.Combine(folderPath, "Avatars", $"{avatarID}.json");
                if (!File.Exists(filePath))
                    continue;
                try
                {
                    File.Delete(filePath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
