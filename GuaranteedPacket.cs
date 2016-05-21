using System;

namespace Terralite
{
    /// <summary>
    /// Class to hold data about a guaranteed packet
    /// </summary>
    public class GuaranteedPacket
    {
        public byte PacketID { get; private set; }
        public byte[] MD5 { get; private set; }
        public byte[] Header { get; private set; }
        public byte[] ByteArray { get; private set; }
        
        private byte[] data;

        public int Tries
        {
            get; set;
        }

        public GuaranteedPacket(byte packetID, byte[] md5, byte[] packet)
        {
            PacketID = packetID;
            data = packet;

            MD5 = md5;
            Header = CreateHeader();
            ByteArray = ToByteArray();
        }

        /// <summary>
        /// Checks a byte[] md5 sum against this packet's MD5 sum
        /// </summary>
        /// <param name="sum">MD5 hash sum</param>
        /// <returns>Whether the MD5s match</returns>
        public bool CheckMD5(byte[] sum)
        {
            if (sum.Length != MD5.Length)
                return false;

            for (int i = 0; i < sum.Length; i++)
                if (MD5[i] != sum[i])
                    return false;

            return true;
        }
        
        /// <summary>
        /// Creates a byte array containing the header information.
        /// </summary>
        /// <returns>Byte array with type 2 + packet id</returns>
        private byte[] CreateHeader()
        {
            byte[] header = new byte[2];
            header[0] = 2;
            header[1] = PacketID;

            return header;
        }

        /// <summary>
        /// Creates a byte array containing the MD5 and packet data.
        /// </summary>
        /// <returns>Byte array with MD5 + packet data</returns>
        private byte[] ToByteArray()
        {
            byte[] packet = new byte[MD5.Length + data.Length];

            int index = 0;

            Array.Copy(MD5, 0, packet, index, MD5.Length);
            index += MD5.Length;

            Array.Copy(data, 0, packet, index, data.Length);
            index += data.Length;

            return packet;
        }
    }
}
