using System;
using System.IO;

namespace Terralite
{
    public class Utils
    {
        /// <summary>
        /// Takes <paramref name="buffer"/> and returns an array of byte[]
        /// that each holds <paramref name="head"/> plus up to MAX_SIZE (1400 bytes) of data.
        /// </summary>
        /// <param name="head">Header to be appended</param>
        /// <param name="buffer">Data to be split</param>
        /// <returns></returns>
        public static byte[][] SplitBuffer(byte[] head, byte[] buffer)
        {
            byte[][] result = new byte[buffer.Length / Packet.MAX_SIZE][];
            int size;

            MemoryStream s;
            byte[] tmp;
            byte[] header;
            byte pid = 1;

            for (uint i = 0; i < result.GetLength(0); i++)
            {
                s = new MemoryStream(5);
                s.WriteByte(Packet.MULTI);
                s.WriteByte((byte)result.GetLength(0));
                s.WriteByte(pid++);
                s.Write(head, 0, head.Length);
                header = new byte[s.Length];
                s.Read(header, 0, header.Length);

                size = i == result.GetLength(0) - 1 ? buffer.Length % Packet.MAX_SIZE : Packet.MAX_SIZE;
                tmp = new byte[header.Length + size];
                Array.Copy(buffer, i * (Packet.MAX_SIZE + header.Length), tmp, 0, size);

                result[i] = Combine(header, tmp);
            }

            return result;
        }

        /// <summary>
        /// Takes two byte arrays and returns their combination
        /// </summary>
        /// <param name="buffer1">The first byte array</param>
        /// <param name="buffer2">The second byte array</param>
        /// <returns><paramref name="buffer1"/> + <paramref name="buffer2"/></returns>
        /// <remarks>
        /// If only one buffer is null, it returns that buffer.
        /// </remarks>
        public static byte[] Combine(byte[] buffer1, byte[] buffer2)
        {
            if (buffer1 == null && buffer2 != null)
                return buffer2;
            if (buffer1 != null && buffer2 == null)
                return buffer1;

            byte[] result = new byte[buffer1.Length + buffer2.Length];

            Array.Copy(buffer1, 0, result, 0, buffer1.Length);
            Array.Copy(buffer2, 0, result, buffer1.Length, buffer2.Length);

            return result;
        }
    }
}
