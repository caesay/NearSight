using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NearSight
{
    public enum HashType
    {
        MD5,
        SHA1,
        SHA256,
        SHA384,
        SHA512,
    }
    internal static class HashHelper
    {
        public static string Format(HashType type, string input)
        {
            return $"{{{type.ToString()}:{input}}}";
        }
        public static HashType Parse(ref string hash)
        {
            if (hash.StartsWith("{") && hash.EndsWith("}"))
            {
                hash = hash.Trim(new char[] {'{', '}'});
                var spt = hash.Split(':');
                hash = spt[1];
                var type = (HashType)Enum.Parse(typeof (HashType), spt[0]);
                return type;
            }
            throw new ArgumentException("String was not in the correct format");
        }
        public static string Compute(HashType type, bool format, string data, string salt)
        {
            HashAlgorithm hash;
            string prefix;
            switch (type)
            {
                case HashType.MD5:
                    prefix = "MD5";
                    hash = new MD5Cng();
                    break;
                case HashType.SHA1:
                    prefix = "SHA1";
                    hash = new SHA1Cng();
                    break;
                case HashType.SHA256:
                    prefix = "SHA256";
                    hash = new SHA256Cng();
                    break;
                case HashType.SHA384:
                    prefix = "SHA384";
                    hash = new SHA384Cng();
                    break;
                case HashType.SHA512:
                    prefix = "SHA512";
                    hash = new SHA512Cng();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(salt+data);
            byte[] hashed = hash.ComputeHash(inputBytes);
            return format ? $"{{{prefix}:{GetHashHex(hashed)}}}" : GetHashHex(hashed);
        }
        private static string GetHashHex(byte[] hash)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
