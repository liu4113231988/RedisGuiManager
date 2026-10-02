using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace RedisGuiManager
{
    // Windows DPAPI binds saved credentials to the current Windows user.
    public sealed class CredentialConverter : JsonConverter<string>
    {
        private const string Prefix = "dpapi:user:";
        [StructLayout(LayoutKind.Sequential)]
        private struct Blob { public int Size; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        private static byte[] Transform(byte[] bytes, bool protect)
        {
            var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
            Blob output = default;
            try
            {
                Marshal.Copy(bytes, 0, input.Data, bytes.Length);
                bool success = protect
                    ? CryptProtectData(ref input, "RedisGuiManager", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot protect/unprotect saved credentials for this Windows user");
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }

        public override void WriteJson(JsonWriter writer, string value, JsonSerializer serializer)
        {
            writer.WriteValue(string.IsNullOrEmpty(value) ? value : Prefix + Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true)));
        }

        public override string ReadJson(JsonReader reader, Type objectType, string existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            string value = (string)reader.Value;
            return value != null && value.StartsWith(Prefix, StringComparison.Ordinal)
                ? Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value.Substring(Prefix.Length)), false)) : value;
        }
    }
}
