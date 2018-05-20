using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Terralite;

namespace TerraliteTests
{
	[TestClass]
	public class ReliabilityTest
	{
		private const byte INIT_ACK = 2;
		private const byte RELIABLE = 11;
		private const byte ACK = 20;
		private const byte PING = 25;
		private const byte PING_ACK = 26;
		private static byte[] HEADER_INIT_ACK = new byte[1] { INIT_ACK };
		private static byte[] PING_ACK_PACKET = new byte[1] { PING_ACK };

		[TestMethod]
		public void TestResend()
		{
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(new IPEndPoint(IPAddress.Any, 2000));

			ReliableConnection rc = new ReliableConnection();
			int connId = rc.Connect("127.0.0.1", 2000);

			EndPoint from = new IPEndPoint(IPAddress.Any, 0);
			byte[] shake1 = new byte[1024];
			int len = socket.ReceiveFrom(shake1, ref from);
			byte[] trimmed = new byte[len];
			Array.Copy(shake1, trimmed, len);

			byte[][] pieces = Utils.Split(trimmed);
			int generated = BitConverter.ToInt32(pieces[1], 0);

			int generated2 = new Random().Next();
			socket.SendTo(Utils.Combine(HEADER_INIT_ACK, Utils.Combine(BitConverter.GetBytes(generated + 1), BitConverter.GetBytes(generated2))), from);

			byte[] shake3 = new byte[1024];
			len = socket.ReceiveFrom(shake3, ref from);
			trimmed = new byte[len];
			Array.Copy(shake3, trimmed, len);

			pieces = Utils.Split(trimmed);
			int calculated = BitConverter.ToInt32(pieces[1], 0);
			int calculated2 = BitConverter.ToInt32(pieces[1], 4);

			#region Retries 10/20

			socket.ReceiveTimeout = 1000;

			string msg = "Hello Terralite";
			rc.SendReliable(connId, msg);

			int rxCount = 0;
			byte[] expected = Encoding.UTF8.GetBytes(msg);
			int expectedId = 1;
			
			for (int i = 0; i < rc.MaxRetries + 1; i++)
			{
				byte[] data = new byte[1024];

				try
				{
					len = socket.ReceiveFrom(data, ref from);
				}
				catch (SocketException)
				{
					break;
				}

				trimmed = new byte[len];
				Array.Copy(data, trimmed, len);

				//probably a ping, check
				if (len == 1)
				{
					if (trimmed[0] == PING)
					{
						socket.SendTo(PING_ACK_PACKET, from);

						i--;
						continue;
					}
				}

				//header should be 2 long -- expected.Length + 2
				Assert.AreEqual(expected.Length + 2, len);

				//check header values
				Assert.AreEqual(RELIABLE, trimmed[0]); // type
				Assert.AreEqual(expectedId, trimmed[1]); // ordered id

				for (int k = 0; k < expected.Length; k++)
				{
					Assert.AreEqual(expected[k], trimmed[k + 2]);
				}

				rxCount++;
			}

			Assert.AreEqual(rc.MaxRetries, rxCount);

			rc.MaxRetries = 20;

			msg = "Hello Terralite 2";
			rc.SendReliable(connId, msg);

			rxCount = 0;
			expected = Encoding.UTF8.GetBytes(msg);
			expectedId++;

			for (int i = 0; i < rc.MaxRetries; i++)
			{
				byte[] data = new byte[1024];

				try
				{
					len = socket.ReceiveFrom(data, ref from);
				}
				catch (SocketException)
				{
					break;
				}

				//probably a ping, check
				if (len == 1)
				{
					if (data[0] == PING)
					{
						socket.SendTo(PING_ACK_PACKET, from);

						i--;
						continue;
					}
				}

				trimmed = new byte[len];
				Array.Copy(data, trimmed, len);

				//header should be 2 long -- expected.Length + 2
				Assert.AreEqual(expected.Length + 2, len);

				//check header values
				Assert.AreEqual(RELIABLE, trimmed[0]); // type
				Assert.AreEqual(expectedId, trimmed[1]); // ordered id

				for (int k = 0; k < expected.Length; k++)
				{
					Assert.AreEqual(expected[k], trimmed[k + 2]);
				}

				rxCount++;
			}

			Assert.AreEqual(rc.MaxRetries, rxCount);

			#endregion Retries 10/20

			#region Ack

			//socket.ReceiveTimeout = -1;

			msg = "Hello Terralite 3";
			rc.SendReliable(connId, msg);

			rxCount = 0;
			expected = Encoding.UTF8.GetBytes(msg);
			expectedId++;

			for (int i = 0; i < 5; i++)
			{
				byte[] data = new byte[1024];

				try
				{
					len = socket.ReceiveFrom(data, ref from);
				}
				catch (SocketException)
				{
					break;
				}

				//probably a ping, check
				if (len == 1)
				{
					if (data[0] == PING)
					{
						socket.SendTo(PING_ACK_PACKET, from);

						i--;
						continue;
					}
				}

				trimmed = new byte[len];
				Array.Copy(data, trimmed, len);

				//header should be 2 long -- expected.Length + 2
				Assert.AreEqual(expected.Length + 2, len);

				//check header values
				Assert.AreEqual(RELIABLE, trimmed[0]); // type
				Assert.AreEqual(expectedId, trimmed[1]); // ordered id

				for (int k = 0; k < expected.Length; k++)
				{
					Assert.AreEqual(expected[k], trimmed[k + 2]);
				}

				rxCount++;
			}

			Assert.AreEqual(5, rxCount);

			{
				socket.ReceiveTimeout = 1000;
				socket.SendTo(new byte[] { ACK, (byte)expectedId }, from);

				byte[] data = new byte[1024];
				try
				{
					len = socket.ReceiveFrom(data, ref from);
				}
				catch (SocketException)
				{
					//timeout should be thrown because we ACK'd the reliable packet
				}
			}

			#endregion Ack

			rc.Shutdown();
			socket.Close();
		}
	}
}
