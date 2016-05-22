using System;

namespace Terralite
{
    /// <summary>
    /// Class to hold data about a guaranteed packet
    /// </summary>
    public class GuaranteedPacket
    {
        public byte PacketID { get; private set; }
        public byte[] Header { get; private set; }
        public byte[] ByteArray { get; private set; }
        
        private byte[] data;

        public int Tries
        {
            get; set;
        }

        public GuaranteedPacket(byte packetID, byte[] packet)
        {
            PacketID = packetID;
            data = packet;
            
            Header = CreateHeader();
            ByteArray = ToByteArray();
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
            byte[] packet = new byte[data.Length];

            int index = 0;

            Array.Copy(data, 0, packet, index, data.Length);
            index += data.Length;

            return packet;
        }
    }
}
