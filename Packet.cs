namespace Terralite
{
    public class Packet
    {
        public static byte[] DISCONNECT = new byte[] { 0x54, 0x65, 0x72, 0x72, 0x61, 0x63, 0x6f, 0x72, 0x65 };
        public const int MAX_SIZE = 1400;
        public const int MAX_SEND_SIZE = 1450;

        public const byte NON_RELIABLE = 1;
        public const byte RELIABLE = 2;
        public const byte MULTI = 3;
        public const byte ACK = 4;
    }
}
