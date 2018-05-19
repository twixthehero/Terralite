using System;

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
			byte[][] result = new byte[buffer.Length / Packet.MAX_SIZE + 1][];
			byte[] header;
			byte[] tmp;
			byte pid = 1;

			for (uint i = 0; i < result.GetLength(0); i++)
			{
				header = new byte[3 + head.Length];
				header[0] = Packet.MULTI;
				header[1] = (byte)result.GetLength(0);
				header[2] = pid++;
				Array.Copy(head, 0, header, 3, head.Length);
				
				tmp = new byte[i == result.GetLength(0) - 1 ? buffer.Length % Packet.MAX_SIZE : Packet.MAX_SIZE];
				Array.Copy(buffer, i * Packet.MAX_SIZE, tmp, 0, tmp.Length);

				result[i] = Combine(header, tmp);
			}

			return result;
		}

		/// <summary>
		/// Splits <paramref name="buffer"/> into two byte[] at <paramref name="index"/>.
		/// If index is -1, it calculates where to split using the packet type.
		/// </summary>
		/// <param name="buffer">Byte[] to be split</param>
		/// <param name="index">Index to split at</param>
		/// <returns>Two byte arrays</returns>
		public static byte[][] Split(byte[] buffer, int index = -1)
		{
			byte[][] result = new byte[2][];

			//calculate index from header
			if (index == -1)
			{
				switch (buffer[0])
				{
					case Packet.INIT:
					case Packet.INIT_ACK:
					case Packet.INIT_FIN:
					case Packet.NON_RELIABLE:
					case Packet.PING:
					case Packet.PING_ACK:
					case Packet.DISCONNECT:
						index = 1;
						break;
					case Packet.RELIABLE:
					case Packet.ACK:
						index = 2;
						break;
				}
			}

			result[0] = new byte[index];
			result[1] = new byte[buffer.Length - index];

			Array.Copy(buffer, result[0], result[0].Length);
			Array.Copy(buffer, index, result[1], 0, result[1].Length);

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
		public static byte[] Combine(byte[] buffer1, byte[] buffer2, int count1 = -1, int count2 = -1)
		{
			if (buffer1 == null && buffer2 != null)
				return buffer2;
			if (buffer1 != null && buffer2 == null)
				return buffer1;

			int size1 = count1 == -1 ? buffer1.Length : count1;
			int size2 = count2 == -1 ? buffer2.Length : count2;
			byte[] result = new byte[size1 + size2];

			Array.Copy(buffer1, 0, result, 0, size1);
			Array.Copy(buffer2, 0, result, size1, size2);

			return result;
		}

		/// <summary>
		/// Used to compare two byte arrays.
		/// </summary>
		/// <param name="buffer1">Byte array one</param>
		/// <param name="buffer2">Byte array two</param>
		/// <returns>Returns whether or not their data is equal</returns>
		public static bool Compare(byte[] buffer1, byte[] buffer2)
		{
			if (buffer1 == buffer2)
				return true;

			if ((buffer1 == null && buffer2 != null) ||
				(buffer1 != null && buffer2 == null))
				return false;

			if (buffer1.Length != buffer2.Length)
				return false;

			for (int i = 0; i < buffer1.Length; i++)
				if (buffer1[i] != buffer2[i])
					return false;

			return true;
		}
	}
}
