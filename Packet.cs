namespace Terralite
{
	internal class Packet
	{
		public const int MAX_SIZE = 1400;
		public const int MAX_SEND_SIZE = 1450;

		public const byte INIT = 1;
		public const byte INIT_ACK = 2;
		public const byte INIT_FIN = 3;
		public const byte NON_RELIABLE = 10;
		public const byte RELIABLE = 11;
		public const byte MULTI = 12;
		public const byte ACK = 20;
		public const byte PING = 25;
		public const byte PING_ACK = 26;
		public const byte DISCONNECT = 30;

		public const byte MIN_VALUE = INIT;
		public const byte MAX_VALUE = DISCONNECT;

		public static byte[] HEADER_INIT = new byte[1] { INIT };
		public static byte[] HEADER_INIT_ACK = new byte[1] { INIT_ACK };
		public static byte[] HEADER_INIT_FIN = new byte[1] { INIT_FIN };
		public static byte[] HEADER_NON_RELIABLE = new byte[1] { NON_RELIABLE };
		public static byte[] ACK_PACKET = new byte[1] { ACK };
		public static byte[] PING_PACKET = new byte[1] { PING };
		public static byte[] PING_ACK_PACKET = new byte[1] { PING_ACK };
		public static byte[] DISCONNECT_PACKET = new byte[1] { DISCONNECT };
	}
}