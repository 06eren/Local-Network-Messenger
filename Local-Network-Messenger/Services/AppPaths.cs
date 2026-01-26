using System;
using System.IO;

namespace Local_Network_Messenger.Services
{
    public static class AppPaths
    {
        private const string AppFolderName = "LocalNetworkMessenger";

        public static string DataRoot
        {
            get
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppFolderName);
                Directory.CreateDirectory(root);
                return root;
            }
        }

        public static string UserDatabasePath => Path.Combine(DataRoot, "users.db");

        public static string SessionPath => Path.Combine(DataRoot, "session.dat");

        public static string ConfigPath => Path.Combine(DataRoot, "config.json");

        public static string ReceivedFilesPath
        {
            get
            {
                var path = Path.Combine(DataRoot, "Received");
                Directory.CreateDirectory(path);
                return path;
            }
        }
    }
}
