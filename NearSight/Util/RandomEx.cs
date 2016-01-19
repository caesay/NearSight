using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NearSight.Util
{
    public class RandomEx
    {
        private static int saltLengthLimit = 32;
        public static string GenerateSalt()
        {
            return GenerateSalt(saltLengthLimit);
        }
        public static string GenerateSalt(int maximumSaltLength)
        {
            return GetString(maximumSaltLength);
        }
        public static string GetString(int size)
        {
            char[] chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();
            var data = GetBytes(size);
            StringBuilder result = new StringBuilder(size);
            foreach (byte b in data)
            {
                result.Append(chars[b % chars.Length]);
            }
            return result.ToString();
        }
        public static byte[] GetBytes(int size)
        {
            byte[] data = new byte[1];
            RNGCryptoServiceProvider crypto = new RNGCryptoServiceProvider();
            crypto.GetNonZeroBytes(data);
            data = new byte[size];
            crypto.GetNonZeroBytes(data);
            return data;
        }
    }
}
