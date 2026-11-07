using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace EarthRadioVaultHelper
{
    public static class Program
    {
        private static byte[] Masked(string prompt)
        {
            char[] chars = new char[512]; int length = 0;
            bool previous = Console.TreatControlCAsInput;
            Console.Error.Write(prompt);
            try
            {
                Console.TreatControlCAsInput = true;
                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape || (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0))
                        throw new VaultFailure("E_CANCELLED", 0);
                    if (key.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); break; }
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (length > 0) { chars[--length] = '\0'; Console.Error.Write("\b \b"); }
                        continue;
                    }
                    if (Char.IsControl(key.KeyChar)) continue;
                    if (length == chars.Length) throw new VaultFailure("E_SECRET", 0);
                    chars[length++] = key.KeyChar; Console.Error.Write('*');
                }
                return new UnicodeEncoding(false, false, true).GetBytes(chars, 0, length);
            }
            finally { Array.Clear(chars, 0, chars.Length); Console.TreatControlCAsInput = previous; }
        }
        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
        public static int Main(string[] args)
        {
            byte[] secret = null, confirmation = null;
            try
            {
                if (args.Length == 1 && (args[0] == "--help" || args[0] == "--version"))
                {
                    Console.WriteLine("Earth Radio Vault Helper " + Policy.Version);
                    if (args[0] == "--help")
                    {
                        Console.WriteLine("store --target NAME --account NAME --receipt ABSOLUTE.json [--format generic|resend]");
                        Console.WriteLine("probe --target NAME");
                        Console.WriteLine("Store requires an interactive console; there is no secret argument, environment, file or stdin option.");
                    }
                    return 0;
                }
                if (Environment.OSVersion.Platform != PlatformID.Win32NT || WindowsIdentity.GetCurrent().IsSystem)
                    throw new VaultFailure("E_CONTEXT", 0);
                if (args.Length < 3 || (args[0] != "store" && args[0] != "probe")) throw new VaultFailure("E_ARGUMENTS", 0);
                Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int i = 1; i < args.Length; i += 2)
                {
                    if (i + 1 >= args.Length || (args[i] != "--target" && args[i] != "--account" && args[i] != "--receipt" && args[i] != "--format") || options.ContainsKey(args[i]))
                        throw new VaultFailure("E_ARGUMENTS", 0);
                    options.Add(args[i], args[i + 1]);
                }
                if (!options.ContainsKey("--target")) throw new VaultFailure("E_ARGUMENTS", 0);
                string target = options["--target"]; Policy.Identifier(target);
                WindowsCredentials vault = new WindowsCredentials();
                if (args[0] == "probe")
                {
                    if (options.Count != 1) throw new VaultFailure("E_ARGUMENTS", 0);
                    bool exists = vault.Exists(target);
                    Console.WriteLine(new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                        { "code", "OK" }, { "exists", exists }, { "secretReturned", false }
                    }));
                    return 0;
                }
                if (!Environment.UserInteractive || Console.IsInputRedirected) throw new VaultFailure("E_INTERACTIVE", 0);
                if (!options.ContainsKey("--account") || !options.ContainsKey("--receipt")) throw new VaultFailure("E_ARGUMENTS", 0);
                string account = options["--account"], receipt = Policy.ReceiptPath(options["--receipt"]);
                string format = options.ContainsKey("--format") ? options["--format"] : "generic";
                Policy.Identifier(account);
                if (format != "generic" && format != "resend") throw new VaultFailure("E_FORMAT", 0);
                if (vault.Exists(target)) throw new VaultFailure("E_EXISTS", 0);
                secret = Masked("Secret (masked): "); Policy.Secret(secret, format);
                confirmation = Masked("Confirm secret (masked): ");
                if (!Equal(secret, confirmation)) throw new VaultFailure("E_CONFIRMATION", 0);
                Result result = CredentialService.Create(vault, new FileReceipts(), target, account, secret, format, receipt);
                Console.WriteLine(result.Json());
                return result.ExitCode;
            }
            catch (VaultFailure error)
            {
                Console.WriteLine(new Result { Code = error.Code, NativeError = error.NativeError }.Json()); return 1;
            }
            catch { Console.WriteLine(new Result { Code = "E_UNEXPECTED" }.Json()); return 1; }
            finally
            {
                if (secret != null) Array.Clear(secret, 0, secret.Length);
                if (confirmation != null) Array.Clear(confirmation, 0, confirmation.Length);
            }
        }
    }
}
