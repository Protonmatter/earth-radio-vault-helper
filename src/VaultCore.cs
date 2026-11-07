using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace EarthRadioVaultHelper
{
    public sealed class VaultFailure : Exception
    {
        public readonly string Code;
        public readonly int NativeError;
        public VaultFailure(string code, int nativeError) : base(code) { Code = code; NativeError = nativeError; }
    }

    public interface ICredentialStore
    {
        bool Exists(string target);
        void Write(string target, string account, byte[] secret);
        bool Matches(string target, string account, byte[] secret);
    }

    public interface IReceipt : IDisposable { void Commit(Dictionary<string, object> metadata); }
    public interface IReceiptFactory { IReceipt Reserve(string path); }

    public sealed class Result
    {
        public string Code = "E_UNEXPECTED";
        public bool? Stored = false;
        public bool Verified;
        public bool ReceiptWritten;
        public int NativeError;
        public int ExitCode { get { return Code == "OK" ? 0 : 1; } }
        public string Json()
        {
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "code", Code }, { "stored", Stored }, { "verified", Verified },
                { "receiptWritten", ReceiptWritten }, { "nativeError", NativeError }, { "secretIncluded", false }
            });
        }
    }

    public static class Policy
    {
        public const string Version = "1.0.0";
        public static void Identifier(string value)
        {
            if (value == null || !Regex.IsMatch(value, "\\A[A-Za-z0-9][A-Za-z0-9._/@:+-]{0,127}\\z"))
                throw new VaultFailure("E_IDENTIFIER", 0);
        }
        public static void Secret(byte[] bytes, string format)
        {
            if (format != "generic" && format != "resend") throw new VaultFailure("E_FORMAT", 0);
            if (bytes == null || bytes.Length < 2 || bytes.Length > 1024 || bytes.Length % 2 != 0)
                throw new VaultFailure("E_SECRET", 0);
            char[] chars = null;
            try
            {
                chars = new UnicodeEncoding(false, false, true).GetChars(bytes);
                foreach (char c in chars) if (c == '\0' || c == '\r' || c == '\n') throw new VaultFailure("E_SECRET", 0);
                if (format == "resend")
                {
                    if (chars.Length < 23 || chars.Length > 203 || chars[0] != 'r' || chars[1] != 'e' || chars[2] != '_')
                        throw new VaultFailure("E_SECRET", 0);
                    for (int i = 3; i < chars.Length; i++)
                    {
                        char c = chars[i];
                        if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                            throw new VaultFailure("E_SECRET", 0);
                    }
                }
            }
            catch (DecoderFallbackException) { throw new VaultFailure("E_SECRET", 0); }
            finally { if (chars != null) Array.Clear(chars, 0, chars.Length); }
        }
        public static string ReceiptPath(string input)
        {
            if (String.IsNullOrEmpty(input) || !Path.IsPathRooted(input) || input.StartsWith("\\", StringComparison.Ordinal))
                throw new VaultFailure("E_RECEIPT_PATH", 0);
            string full = Path.GetFullPath(input);
            if (full.Length > 240 || full.IndexOf(':', 2) >= 0 || !full.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new VaultFailure("E_RECEIPT_PATH", 0);
            if (new DriveInfo(Path.GetPathRoot(full)).DriveType != DriveType.Fixed)
                throw new VaultFailure("E_RECEIPT_PATH", 0);
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(full));
            if (!directory.Exists) throw new VaultFailure("E_RECEIPT_PATH", 0);
            for (DirectoryInfo cursor = directory; cursor != null; cursor = cursor.Parent)
                if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0) throw new VaultFailure("E_RECEIPT_PATH", 0);
            if (File.Exists(full) || Directory.Exists(full)) throw new VaultFailure("E_RECEIPT_EXISTS", 0);
            return full;
        }
    }

    public sealed class FileReceipts : IReceiptFactory
    {
        private sealed class Reserved : IReceipt
        {
            private readonly FileStream stream;
            public Reserved(string path) { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            public void Commit(Dictionary<string, object> metadata)
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(metadata) + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            public void Dispose() { stream.Dispose(); }
        }
        public IReceipt Reserve(string path) { return new Reserved(path); }
    }

    public static class CredentialService
    {
        public static Result Create(ICredentialStore store, IReceiptFactory receipts, string target, string account,
            byte[] secret, string format, string receiptPath)
        {
            Result result = new Result();
            Mutex gate = null;
            bool owned = false;
            try
            {
                Policy.Identifier(target); Policy.Identifier(account); Policy.Secret(secret, format);
                receiptPath = Policy.ReceiptPath(receiptPath);
                string sid = WindowsIdentity.GetCurrent().User.Value;
                string name;
                using (SHA256 hash = SHA256.Create())
                    name = "Global\\EarthRadioVaultHelper." + BitConverter.ToString(hash.ComputeHash(
                        Encoding.UTF8.GetBytes(sid + "\n" + target.ToUpperInvariant()))).Replace("-", "");
                MutexSecurity security = new MutexSecurity();
                security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(sid), MutexRights.FullControl, AccessControlType.Allow));
                bool created;
                gate = new Mutex(false, name, out created, security);
                try { owned = gate.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) throw new VaultFailure("E_BUSY", 0);
                if (store.Exists(target)) throw new VaultFailure("E_EXISTS", 0);
                IReceipt receipt;
                try { receipt = receipts.Reserve(receiptPath); }
                catch { throw new VaultFailure("E_RECEIPT_RESERVE", 0); }
                using (receipt)
                {
                    result.Stored = null;
                    store.Write(target, account, secret);
                    result.Stored = true;
                    if (!store.Matches(target, account, secret)) throw new VaultFailure("E_VERIFY_AFTER_STORE", 0);
                    result.Verified = true;
                    try
                    {
                        receipt.Commit(new Dictionary<string, object> {
                            { "schemaVersion", 1 }, { "helperVersion", Policy.Version },
                            { "createdAtUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                            { "vault", "Windows Credential Manager" }, { "credentialType", "Generic" },
                            { "target", target }, { "account", account }, { "persistence", "LocalMachine-current-user" },
                            { "stored", true }, { "readbackPassed", true }, { "secretIncluded", false }
                        });
                    }
                    catch { throw new VaultFailure("E_RECEIPT_AFTER_STORE", 0); }
                    result.ReceiptWritten = true;
                    result.Code = "OK";
                }
            }
            catch (VaultFailure failure) { result.Code = failure.Code; result.NativeError = failure.NativeError; }
            catch { result.Code = "E_UNEXPECTED"; }
            finally { if (owned) gate.ReleaseMutex(); if (gate != null) gate.Dispose(); }
            return result;
        }
    }

    public sealed class WindowsCredentials : ICredentialStore
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public UInt32 Flags, Type;
            public string TargetName, Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public UInt32 BlobSize;
            public IntPtr Blob;
            public UInt32 Persist, AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias, UserName;
        }
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Read(string target, UInt32 type, UInt32 flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteNative(ref Credential credential, UInt32 flags);
        [DllImport("advapi32.dll", EntryPoint = "CredFree")]
        private static extern void Free(IntPtr credential);
        private static void ClearAndFree(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return;
            try
            {
                Credential value = (Credential)Marshal.PtrToStructure(pointer, typeof(Credential));
                if (value.Blob != IntPtr.Zero && value.BlobSize <= 2560)
                    for (int i = 0; i < value.BlobSize; i++) Marshal.WriteByte(value.Blob, i, 0);
            }
            finally { Free(pointer); }
        }
        public bool Exists(string target)
        {
            IntPtr pointer = IntPtr.Zero;
            if (!Read(target, 1, 0, out pointer))
            {
                int code = Marshal.GetLastWin32Error();
                if (code == 1168) return false;
                throw new VaultFailure("E_VAULT_READ", code);
            }
            ClearAndFree(pointer);
            return true;
        }
        public void Write(string target, string account, byte[] secret)
        {
            GCHandle pin = GCHandle.Alloc(secret, GCHandleType.Pinned);
            try
            {
                Credential value = new Credential { Type = 1, TargetName = target, UserName = account,
                    Comment = "Earth Radio Vault Helper", BlobSize = (UInt32)secret.Length,
                    Blob = secret.Length == 0 ? IntPtr.Zero : pin.AddrOfPinnedObject(), Persist = 2 };
                if (!WriteNative(ref value, 0)) throw new VaultFailure("E_VAULT_WRITE", Marshal.GetLastWin32Error());
            }
            finally { pin.Free(); }
        }
        public bool Matches(string target, string account, byte[] secret)
        {
            IntPtr pointer = IntPtr.Zero;
            if (!Read(target, 1, 0, out pointer)) throw new VaultFailure("E_VAULT_VERIFY", Marshal.GetLastWin32Error());
            try
            {
                Credential value = (Credential)Marshal.PtrToStructure(pointer, typeof(Credential));
                if (value.Type != 1 || value.Persist != 2 || value.BlobSize != secret.Length ||
                    !String.Equals(value.UserName, account, StringComparison.Ordinal)) return false;
                int difference = 0;
                for (int i = 0; i < secret.Length; i++) difference |= secret[i] ^ Marshal.ReadByte(value.Blob, i);
                return difference == 0;
            }
            finally { ClearAndFree(pointer); }
        }
    }
}
