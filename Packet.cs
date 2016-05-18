namespace Terralite
{
    public class Packet
    {
        public const int MAX_SIZE = 1400;
        public const int MAX_SEND_SIZE = 1450;

        public const byte NON_RELIABLE = 1;
        public const byte RELIABLE = 2;
        public const byte MULTI = 3;
        public const byte ACK = 4;
        public const byte DISCONNECT = 5;

        public static byte[] HEADER_NON_RELIABLE = new byte[1] { 1 };
        public static byte[] DISCONNECT_PACKET = new byte[1] { DISCONNECT };

        /// <summary>
        /// How long to wait until processing a packet that didn't come in order
        /// </summary>
        public const float ORDER_TIMEOUT = 0.5f;
    }
}