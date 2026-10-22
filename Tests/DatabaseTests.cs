using System;
using System.IO;
using System.Linq;
using Vortex.Core.Database;

namespace Vortex.Tests
{
    public static class DatabaseTests
    {
        public static void RunAll()
        {
            Test_DatabaseSchemaInitialization();
            Test_VaultSettingsCRUD();
            Test_VaultFoldersCRUD();
        }

        private static void Test_DatabaseSchemaInitialization()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DB_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var db = new DatabaseService(dbPath);
                if (!File.Exists(dbPath))
                    throw new Exception("SQLite database file was not created on disk.");

                var settings = db.GetVaultSettings();
                if (settings == null)
                    throw new Exception("GetVaultSettings returned null on fresh database.");

                if (settings.IsSetup != false)
                    throw new Exception("Default IsSetup should be false.");

                TestRunner.Pass("Database: SQLite Schema Initialization & Default Settings Table");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }

        private static void Test_VaultSettingsCRUD()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DBSettings_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var db = new DatabaseService(dbPath);

                var settings = new VaultSettingsModel
                {
                    IsSetup = true,
                    PasswordHash = "TestHash123",
                    PasswordSalt = "TestSalt123",
                    RecoveryKeyHash = "TestRecoveryHash123",
                    AutoLockOnScreenLock = true,
                    AutoLockIdleMinutes = 15,
                    UsbWriteBlockEnabled = true
                };

                db.SaveVaultSettings(settings);

                var retrieved = db.GetVaultSettings();
                if (!retrieved.IsSetup ||
                    retrieved.PasswordHash != "TestHash123" ||
                    retrieved.PasswordSalt != "TestSalt123" ||
                    retrieved.RecoveryKeyHash != "TestRecoveryHash123" ||
                    !retrieved.AutoLockOnScreenLock ||
                    retrieved.AutoLockIdleMinutes != 15 ||
                    !retrieved.UsbWriteBlockEnabled)
                {
                    throw new Exception("Retrieved VaultSettings do not match saved values.");
                }

                TestRunner.Pass("Database: VaultSettings Persistence & Round-Trip Retrieval");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }

        private static void Test_VaultFoldersCRUD()
        {
            string dbPath = Path.Combine(Path.GetTempPath(), "VortexTest_DBFolders_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                var db = new DatabaseService(dbPath);

                var folder1 = new VaultFolderModel
                {
                    FolderPath = @"C:\Users\Test\SecretDocs",
                    FolderName = "SecretDocs",
                    ProtectionMode = "Encrypted",
                    IsLocked = true,
                    IsStealth = true,
                    TotalSizeBytes = 1048576,
                    FileCount = 12,
                    EncryptedPayloadPath = @"C:\Users\Test\AppData\Local\Vortex\VaultContainers\vault_1.vortexvault"
                };

                db.SaveVaultFolder(folder1);

                var list = db.GetVaultFolders();
                if (list.Count != 1)
                    throw new Exception($"Expected 1 folder in vault, found {list.Count}");

                var loaded = list[0];
                if (loaded.FolderName != "SecretDocs" || !loaded.IsLocked || !loaded.IsStealth || loaded.FileCount != 12)
                    throw new Exception("Loaded VaultFolder model properties do not match.");

                // Update lock status
                loaded.IsLocked = false;
                db.SaveVaultFolder(loaded);

                var updated = db.GetVaultFolders().First(f => f.FolderPath == folder1.FolderPath);
                if (updated.IsLocked)
                    throw new Exception("Updated IsLocked should be false.");

                // Delete folder
                db.DeleteVaultFolder(folder1.FolderPath);
                var emptyList = db.GetVaultFolders();
                if (emptyList.Count != 0)
                    throw new Exception("Folder was not removed after DeleteVaultFolder.");

                TestRunner.Pass("Database: VaultFolders Insert, Update, and Delete Lifecycle");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }
    }
}
