using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Kiosk.Device.IT.NV
{
    internal static class NV_Util
    {
        private const ulong MAX_RANDOM_INTEGER = 2147483648;
        private const ulong MAX_PRIME_NUMBER = 2147483648;

        internal static byte[] CRC16(byte[] bytes)
        {
            const ushort poly = 0x8005;
            ushort[] table = new ushort[256];
            ushort initialValue = 0xFFFF;
            ushort temp, a;
            ushort crc = initialValue;
            for (int i = 0; i < table.Length; ++i)
            {
                temp = 0;
                a = (ushort)(i << 8);
                for (int j = 0; j < 8; ++j)
                {
                    if (((temp ^ a) & 0x8000) != 0)
                        temp = (ushort)((temp << 1) ^ poly);
                    else
                        temp <<= 1;
                    a <<= 1;
                }
                table[i] = temp;
            }
            for (int i = 0; i < bytes.Length; ++i)
            {
                crc = (ushort)((crc << 8) ^ table[((crc >> 8) ^ (0xff & bytes[i]))]);
            }


            return BitConverter.GetBytes(crc);
        }

        internal static byte[] AESEncrypt(NV_Keys key, byte[] data)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (Aes aes = (Aes)new AesManaged())
                {
                    byte[] numArray = new byte[16];
                    for (byte index = 0; index < (byte)8; ++index)
                    {
                        numArray[(int)index] = (byte)(key.FixedKey >> 8 * (int)index);
                        numArray[(int)index + 8] = (byte)(key.HostKey >> 8 * (int)index);
                    }
                    aes.BlockSize = 128;
                    aes.KeySize = 128;
                    aes.Key = numArray;
                    aes.Mode = CipherMode.ECB;
                    aes.Padding = PaddingMode.None;
                    key.EncryptKey = numArray;
                    using (CryptoStream cryptoStream = new CryptoStream((Stream)memoryStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(data, 0, data.Length);
                    }

                    return memoryStream.ToArray();
                }
            }
        }

        internal static byte[] AESDecrypt(NV_Keys key, byte[] data)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (Aes aes = (Aes)new AesManaged())
                {
                    byte[] numArray1 = new byte[data.Length];
                    byte[] numArray2 = new byte[16];
                    for (byte index = 0; index < (byte)8; ++index)
                    {
                        numArray2[(int)index] = (byte)(key.FixedKey >> 8 * (int)index);
                        numArray2[(int)index + 8] = (byte)(key.HostKey >> 8 * (int)index);
                    }
                    aes.BlockSize = 128;
                    aes.KeySize = 128;
                    aes.Key = numArray2;
                    aes.Mode = CipherMode.ECB;
                    aes.Padding = PaddingMode.None;
                    using (CryptoStream cryptoStream = new CryptoStream((Stream)memoryStream, aes.CreateDecryptor(), CryptoStreamMode.Write))
                        cryptoStream.Write(data, 0, data.Length);
                    byte[] array = memoryStream.ToArray();
                    for (byte index = 0; (int)index < data.Length; ++index)
                        data[(int)index + 0] = array[(int)index];


                }
            }

            return data;
        }

        internal static ulong GenerateRandomNumber()
        {
            RNGCryptoServiceProvider rngCsp = new RNGCryptoServiceProvider();

            ulong num = 0;
            byte[] data = new byte[8];
            rngCsp.GetBytes(data);
            for (byte index = 0; index < (byte)8; ++index)
                num += (ulong)data[(int)index] << 8 * (int)index;
            return num;
        }

        internal static ulong GeneratePrimeRandomNumber()
        {
            ulong n = GenerateRandomNumber() % 2147483648UL;
            if (((long)n & 1L) == 0L)
                ++n;
            while (MillerRabin(n, (ushort)5) == Primality.COMPOSITE)
                n += 2UL;
            return n;
        }

        internal static Primality MillerRabin(ulong n, ushort trials)
        {
            for (ushort index = 0; (int)index < (int)trials; ++index)
            {
                ulong a = GenerateRandomNumber() % (n - 3UL) + 2UL;
                if (SingleMillerRabin(n, a) == Primality.COMPOSITE)
                    return Primality.COMPOSITE;
            }
            return Primality.PSEUDOPRIME;
        }

        internal static Primality SingleMillerRabin(ulong n, ulong a)
        {
            ushort num1 = 0;
            ulong y;
            for (y = n - 1UL; ((long)y & 1L) == 0L; y >>= 1)
                ++num1;
            if (num1 == (ushort)0)
                return Primality.COMPOSITE;
            ulong num2 = XpowYmodN(a, y, n);
            if (num2 == 1UL || (long)num2 == (long)n - 1L)
                return Primality.PSEUDOPRIME;
            for (ushort index = 1; (int)index < (int)num1; ++index)
            {
                num2 = num2 * num2 % n;
                if (num2 == 1UL)
                    return Primality.COMPOSITE;
                if ((long)num2 == (long)n - 1L)
                    return Primality.PSEUDOPRIME;
            }
            return Primality.COMPOSITE;
        }

        internal static ulong XpowYmodN(ulong x, ulong y, ulong N)
        {
            ulong num1 = x;
            ulong num2 = 1;
            for (; y != 0UL; y >>= 1)
            {
                if (((long)y & 1L) != 0L)
                    num2 = num2 * num1 % N;
                num1 = num1 * num1 % N;
            }
            return num2;
        }

        internal enum Primality
        {
            COMPOSITE,
            PSEUDOPRIME,
        }

        internal static byte[] RandomArray(int length = 0)
        {
            List<byte> array = new List<byte>();
            var randomNumber = new Random();
            for (var i = 1; i <= length; i++)
            {
                array.Add((byte)randomNumber.Next(0, 255));

            }
            return array.ToArray();
        }

        internal static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
    }
}
