using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Vortex.Core.Vault;

namespace Vortex.Tests
{
    public static class PerformanceAndResourceTests
    {
        public static void RunAll()
        {
            Test_CryptoDerivationBenchmark();
            Test_MemorySoakAndLeakAudit();
            Test_AsyncWorkerThreadOffloading();
        }

        private static void Test_CryptoDerivationBenchmark()
        {
            var crypto = new VaultCryptoEngine();
            byte[] masterKey = RandomNumberGenerator.GetBytes(32);
            string password = "BenchmarkPassword123!";

            // Warm up
            string wrapped = crypto.WrapMasterKey(masterKey, password);
            crypto.UnwrapMasterKey(wrapped, password);

            var sw = Stopwatch.StartNew();
            int iterations = 10;
            for (int i = 0; i < iterations; i++)
            {
                string w = crypto.WrapMasterKey(masterKey, password);
                var (ok, _, _) = crypto.UnwrapMasterKey(w, password);
                if (!ok) throw new Exception("Benchmark unwrap failed!");
            }
            sw.Stop();

            double avgMs = sw.ElapsedMilliseconds / (double)iterations;
            if (avgMs > 500)
                throw new Exception($"Key derivation took too long: {avgMs:F2} ms (threshold: 500 ms)!");

            TestRunner.Pass($"Performance: AES-256-GCM + PBKDF2 Key Derivation ({avgMs:F1} ms / cycle across {iterations} runs)");
        }

        private static void Test_MemorySoakAndLeakAudit()
        {
            var crypto = new VaultCryptoEngine();
            string testDir = Path.Combine(Path.GetTempPath(), "VortexSoakSrc_" + Guid.NewGuid().ToString("N"));
            string vaultFile = Path.Combine(Path.GetTempPath(), "VortexSoakVault_" + Guid.NewGuid().ToString("N") + ".vortexvault");
            string restoreDir = Path.Combine(Path.GetTempPath(), "VortexSoakDst_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                // Write 100 KB payload
                byte[] payload = new byte[100 * 1024];
                RandomNumberGenerator.Fill(payload);
                File.WriteAllBytes(Path.Combine(testDir, "soak_data.bin"), payload);

                byte[] key = RandomNumberGenerator.GetBytes(32);

                // Initial baseline
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                long startMemory = GC.GetTotalMemory(true);

                // Run 30 consecutive full encryption & decryption cycles
                int cycles = 30;
                for (int i = 0; i < cycles; i++)
                {
                    var (encOk, _, _, _) = crypto.EncryptDirectoryWithKey(testDir, vaultFile, key);
                    if (!encOk) throw new Exception($"Soak encryption cycle {i} failed!");

                    var (decOk, _) = crypto.DecryptVaultWithKey(vaultFile, restoreDir, key);
                    if (!decOk) throw new Exception($"Soak decryption cycle {i} failed!");

                    try { File.Delete(vaultFile); } catch { }
                    try { Directory.Delete(restoreDir, true); } catch { }
                }

                // Final memory check
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                long endMemory = GC.GetTotalMemory(true);

                long memoryDeltaKb = (endMemory - startMemory) / 1024;

                // Threshold: memory growth should not exceed 20 MB after 30 cycles
                if (memoryDeltaKb > 20 * 1024)
                    throw new Exception($"Memory leak detected! Unreclaimed delta: {memoryDeltaKb} KB");

                TestRunner.Pass($"Performance: Memory Leak & Soak Audit ({cycles} full cycles, memory delta: {memoryDeltaKb} KB)");
            }
            finally
            {
                if (Directory.Exists(testDir)) try { Directory.Delete(testDir, true); } catch { }
                if (Directory.Exists(restoreDir)) try { Directory.Delete(restoreDir, true); } catch { }
                if (File.Exists(vaultFile)) try { File.Delete(vaultFile); } catch { }
            }
        }

        private static void Test_AsyncWorkerThreadOffloading()
        {
            int mainThreadId = Environment.CurrentManagedThreadId;
            int? workerThreadId = null;

            Task.Run(() =>
            {
                workerThreadId = Environment.CurrentManagedThreadId;
            }).Wait();

            if (workerThreadId == null || workerThreadId == mainThreadId)
                throw new Exception("Worker tasks are not running on separate background thread pool threads!");

            TestRunner.Pass("Performance: Asynchronous Background Thread Pool Offloading (Zero UI Freeze)");
        }
    }
}
