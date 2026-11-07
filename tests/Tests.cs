using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using EarthRadioVaultHelper;

public static class Tests
{
    private static int passed;
    private static string work;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("Test failed: " + name); passed++; }
    private static byte[] Sample() { return Encoding.Unicode.GetBytes("synthetic-" + Guid.NewGuid().ToString("N")); }
    private static string Receipt() { return Path.Combine(work, Guid.NewGuid().ToString("N") + ".json"); }
    private sealed class FakeVault : ICredentialStore
    {
        public bool Present, ReadError, WriteError, VerifyError;
        public int Writes, Reads;
        public bool Exists(string target) { Reads++; if (ReadError) throw new VaultFailure("E_VAULT_READ", 5); return Present; }
        public void Write(string target, string account, byte[] value) { Writes++; if (WriteError) throw new VaultFailure("E_VAULT_WRITE", 5); Present = true; }
        public bool Matches(string target, string account, byte[] value) { return !VerifyError; }
    }
    private sealed class FakeReceipts : IReceiptFactory
    {
        public bool FailReserve, FailCommit;
        private sealed class Sink : IReceipt
        {
            private readonly bool fail;
            public Sink(bool fail) { this.fail = fail; }
            public void Commit(Dictionary<string, object> value) { if (fail) throw new IOException(); }
            public void Dispose() { }
        }
        public IReceipt Reserve(string path) { if (FailReserve) throw new IOException(); return new Sink(FailCommit); }
    }
    private sealed class BlockingVault : ICredentialStore
    {
        public readonly ManualResetEvent Entered = new ManualResetEvent(false);
        public readonly ManualResetEvent Continue = new ManualResetEvent(false);
        public bool Exists(string target) { return false; }
        public void Write(string target, string account, byte[] value)
        { Entered.Set(); if (!Continue.WaitOne(5000)) throw new VaultFailure("E_TEST_TIMEOUT", 0); }
        public bool Matches(string target, string account, byte[] value) { return true; }
    }
    private static Result Create(FakeVault vault, FakeReceipts receipts, byte[] value, string target, string format)
    { return CredentialService.Create(vault, receipts, target, "test", value, format, Receipt()); }
    private static void Unit()
    {
        byte[] value = Sample();
        try
        {
            FakeVault vault = new FakeVault(); Result result = Create(vault, new FakeReceipts(), value, "Example/New", "generic");
            Check(result.ExitCode == 0 && result.Stored == true && result.Verified && result.ReceiptWritten && vault.Writes == 1, "create verified receipt");
            Check(!result.Json().Contains(Encoding.Unicode.GetString(value)), "result excludes input");
            vault = new FakeVault { Present = true }; result = Create(vault, new FakeReceipts(), value, "Example/Existing", "generic");
            Check(result.Code == "E_EXISTS" && vault.Writes == 0 && result.Stored == false, "existing including empty preserved");
            vault = new FakeVault { ReadError = true }; result = Create(vault, new FakeReceipts(), value, "Example/ReadError", "generic");
            Check(result.Code == "E_VAULT_READ" && result.NativeError == 5 && vault.Writes == 0, "read failure is not absence");
            vault = new FakeVault { WriteError = true }; result = Create(vault, new FakeReceipts(), value, "Example/WriteError", "generic");
            Check(result.Code == "E_VAULT_WRITE" && result.Stored == null && !result.ReceiptWritten, "write failure state unknown");
            vault = new FakeVault { VerifyError = true }; result = Create(vault, new FakeReceipts(), value, "Example/VerifyError", "generic");
            Check(result.Code == "E_VERIFY_AFTER_STORE" && result.Stored == true && !result.Verified, "readback failure retains entry");
            vault = new FakeVault(); result = Create(vault, new FakeReceipts { FailReserve = true }, value, "Example/Reserve", "generic");
            Check(result.Code == "E_RECEIPT_RESERVE" && vault.Writes == 0, "receipt permission failure before write");
            vault = new FakeVault(); result = Create(vault, new FakeReceipts { FailCommit = true }, value, "Example/Commit", "generic");
            Check(result.Code == "E_RECEIPT_AFTER_STORE" && result.Stored == true && result.Verified && !result.ReceiptWritten, "receipt failure after store explicit");
            foreach (string invalid in new string[] { "", "../bad", "bad name", "bad\nname", new string('a', 129) })
            {
                vault = new FakeVault(); result = Create(vault, new FakeReceipts(), value, invalid, "generic");
                Check(result.Code == "E_IDENTIFIER" && vault.Reads == 0 && vault.Writes == 0, "identifier rejected");
            }
            foreach (byte[] invalid in new byte[][] { new byte[0], new byte[1], new byte[1026], Encoding.Unicode.GetBytes("a\0b"), Encoding.Unicode.GetBytes("a\nb"), new byte[] { 0, 216 } })
            {
                vault = new FakeVault(); result = Create(vault, new FakeReceipts(), invalid, "Example/Input", "generic");
                Check(result.Code == "E_SECRET" && vault.Reads == 0, "malformed input rejected");
            }
            result = Create(new FakeVault(), new FakeReceipts(), value, "Example/Format", "unknown");
            Check(result.Code == "E_FORMAT", "unknown format rejected");
            result = Create(new FakeVault(), new FakeReceipts(), value, "Example/Resend", "resend");
            Check(result.Code == "E_SECRET", "provider format enforced");
            byte[] shaped = Encoding.Unicode.GetBytes("re" + "_" + new string('A', 20));
            try { Check(Create(new FakeVault(), new FakeReceipts(), shaped, "Example/Shape", "resend").Code == "OK", "synthetic provider shape accepted"); }
            finally { Array.Clear(shaped, 0, shaped.Length); }
            string existing = Receipt(); File.WriteAllText(existing, "metadata only");
            vault = new FakeVault(); result = CredentialService.Create(vault, new FakeReceipts(), "Example/Receipt", "test", value, "generic", existing);
            Check(result.Code == "E_RECEIPT_EXISTS" && vault.Reads == 0, "existing receipt preserved");
            result = CredentialService.Create(new FakeVault(), new FakeReceipts(), "Example/Receipt", "test", value, "generic", "relative.json");
            Check(result.Code == "E_RECEIPT_PATH", "relative receipt denied");
            result = CredentialService.Create(new FakeVault(), new FakeReceipts(), "Example/Receipt", "test", value, "generic", "\\\\server\\share\\receipt.json");
            Check(result.Code == "E_RECEIPT_PATH", "network receipt denied");
            string binary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VaultHelper.exe");
            ProcessStartInfo info = new ProcessStartInfo(binary, "store --target Example/Input --account test --receipt \"" + Receipt() + "\"");
            info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true; info.RedirectStandardError = true;
            using (Process child = Process.Start(info))
            {
                child.StandardInput.Close(); string output = child.StandardOutput.ReadToEnd();
                if (!child.WaitForExit(10000)) { child.Kill(); throw new Exception("CLI deadline"); }
                Check(child.ExitCode == 1 && output.Contains("E_INTERACTIVE"), "redirected input rejected");
            }
            BlockingVault blocker = new BlockingVault(); Result held = null;
            string gateTarget = "Example/Concurrency-" + Guid.NewGuid().ToString("N");
            Thread owner = new Thread(delegate() { held = CredentialService.Create(blocker, new FakeReceipts(), gateTarget,
                "test", value, "generic", Receipt()); });
            owner.Start();
            try
            {
                Check(blocker.Entered.WaitOne(5000), "first writer acquired mutex");
                FakeVault contender = new FakeVault();
                Result busy = CredentialService.Create(contender, new FakeReceipts(), gateTarget.ToUpperInvariant(),
                    "different-account", value, "generic", Receipt());
                Check(busy.Code == "E_BUSY" && contender.Reads == 0 && contender.Writes == 0, "case-folded target serializes helper writers");
            }
            finally { blocker.Continue.Set(); if (!owner.Join(5000)) throw new Exception("Writer cleanup deadline"); blocker.Entered.Dispose(); blocker.Continue.Dispose(); }
            Check(held != null && held.Code == "OK", "first writer completes after release");
            bool lookalikeDenied = false;
            try { Cleanup("EarthRadioVaultHelperXSelfTest/" + Guid.NewGuid().ToString("N")); }
            catch (Exception failure) { lookalikeDenied = failure.Message == "Test cleanup scope denied"; }
            Check(lookalikeDenied, "lookalike cleanup namespace denied before native access");
        }
        finally { Array.Clear(value, 0, value.Length); }
    }
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteNative(string target, UInt32 type, UInt32 flags);
    private static void Cleanup(string target)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(target, "\\AEarthRadioVaultHelper[.]SelfTest/[a-f0-9]{32}\\z"))
            throw new Exception("Test cleanup scope denied");
        if (!DeleteNative(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Exception("Test credential cleanup failed");
        Check(!new WindowsCredentials().Exists(target), "native cleanup readback");
    }
    private static void Native()
    {
        WindowsCredentials vault = new WindowsCredentials();
        string target = "EarthRadioVaultHelper.SelfTest/" + Guid.NewGuid().ToString("N");
        byte[] first = Encoding.Unicode.GetBytes("synthetic Unicode \u03bb\u6f22 " + Guid.NewGuid().ToString("N"));
        byte[] second = Sample();
        try
        {
            Check(!vault.Exists(target), "native missing is absent without creation");
            Result result = CredentialService.Create(vault, new FileReceipts(), target, "test", first, "generic", Receipt());
            Check(result.Code == "OK" && vault.Matches(target, "test", first), "native create and Unicode readback");
            result = CredentialService.Create(vault, new FileReceipts(), target, "test", second, "generic", Receipt());
            Check(result.Code == "E_EXISTS" && vault.Matches(target, "test", first), "native repeat preserves first value");
        }
        finally { Cleanup(target); Array.Clear(first, 0, first.Length); Array.Clear(second, 0, second.Length); }
        target = "EarthRadioVaultHelper.SelfTest/" + Guid.NewGuid().ToString("N");
        byte[] empty = new byte[0], replacement = Sample();
        try
        {
            vault.Write(target, "test", empty);
            Result result = CredentialService.Create(vault, new FileReceipts(), target, "test", replacement, "generic", Receipt());
            Check(result.Code == "E_EXISTS" && vault.Matches(target, "test", empty), "native existing empty is preserved");
        }
        finally { Cleanup(target); Array.Clear(replacement, 0, replacement.Length); }
        target = "EarthRadioVaultHelper.SelfTest/" + Guid.NewGuid().ToString("N");
        Process left = null, right = null;
        try
        {
            string binary = System.Reflection.Assembly.GetExecutingAssembly().Location;
            ProcessStartInfo leftInfo = new ProcessStartInfo(binary, "--race-worker " + target + " left");
            leftInfo.UseShellExecute = false; leftInfo.CreateNoWindow = true; leftInfo.RedirectStandardOutput = true;
            ProcessStartInfo rightInfo = new ProcessStartInfo(binary, "--race-worker " + target + " right");
            rightInfo.UseShellExecute = false; rightInfo.CreateNoWindow = true; rightInfo.RedirectStandardOutput = true;
            left = Process.Start(leftInfo); right = Process.Start(rightInfo);
            if (!left.WaitForExit(10000) || !right.WaitForExit(10000)) throw new Exception("Native race deadline");
            string leftOutput = left.StandardOutput.ReadToEnd(), rightOutput = right.StandardOutput.ReadToEnd();
            Check((left.ExitCode == 0) != (right.ExitCode == 0), "one process creates the shared target");
            string losing = left.ExitCode == 0 ? rightOutput : leftOutput;
            Check(losing.Contains("E_EXISTS") || losing.Contains("E_BUSY"), "other process fails closed");
            byte[] winner = Encoding.Unicode.GetBytes("synthetic-" + target + (left.ExitCode == 0 ? "left" : "right"));
            try { Check(vault.Matches(target, "test", winner), "winning process value preserved"); }
            finally { Array.Clear(winner, 0, winner.Length); }
        }
        finally
        {
            if (left != null) { if (!left.HasExited) left.Kill(); left.Dispose(); }
            if (right != null) { if (!right.HasExited) right.Kill(); right.Dispose(); }
            Cleanup(target);
        }
    }
    public static int Main(string[] args)
    {
        work = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            if (args.Length == 3 && args[0] == "--race-worker")
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(args[1], "\\AEarthRadioVaultHelper[.]SelfTest/[a-f0-9]{32}\\z") ||
                    (args[2] != "left" && args[2] != "right")) throw new Exception("Worker scope denied");
                byte[] dummy = Encoding.Unicode.GetBytes("synthetic-" + args[1] + args[2]);
                try
                {
                    Result result = CredentialService.Create(new WindowsCredentials(), new FileReceipts(), args[1],
                        "test", dummy, "generic", Receipt());
                    Console.WriteLine(result.Json()); return result.ExitCode;
                }
                finally { Array.Clear(dummy, 0, dummy.Length); }
            }
            if (args.Length == 2 && args[0] == "--cleanup-target") { Cleanup(args[1]); return 0; }
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--native")) throw new Exception("Unsupported test arguments");
            Unit(); if (args.Length == 1) Native();
            string json = new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "passed", passed }, { "failed", 0 }, { "nativeVaultTested", args.Length == 1 },
                { "processBits", IntPtr.Size * 8 }, { "secretIncluded", false }
            });
            File.WriteAllText(Path.Combine(work, "result.json"), json + "\n"); Console.WriteLine(json); return 0;
        }
        catch (Exception failure) { Console.Error.WriteLine(failure.Message); return 1; }
    }
}
