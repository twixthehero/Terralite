using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terralite;

namespace TerraliteTests
{
	[TestClass]
	public class UtilTests
    {
		private static byte[] HEADER_NON_RELIABLE = new byte[1] { 10 };
		private const int MULTI = 12;
		private const int MAX_SIZE = 1400;

		[TestMethod]
		public void TestSplitBuffer()
		{
			byte[] buffer = new byte[10 * 1000];
			for (int i = 0; i < buffer.Length; i++)
			{
				buffer[i] = (byte)((i / 1000) + 1);
			}

			byte[][] expected = new byte[buffer.Length / MAX_SIZE + 1][];
			int value = 0;
			for (int i = 0; i < expected.Length; i++)
			{
				expected[i] = new byte[4 + (i == expected.Length - 1 ? buffer.Length % MAX_SIZE : MAX_SIZE)];
				expected[i][0] = MULTI;
				expected[i][1] = (byte)expected.Length;
				expected[i][2] = (byte)(i + 1);
				expected[i][3] = 10;

				for (int k = 4; k < expected[i].Length; k++)
				{
					expected[i][k] = (byte)((value / 1000) + 1);
					value++;
				}
			}

			byte[][] split = Utils.SplitBuffer(HEADER_NON_RELIABLE, buffer);

			Assert.AreEqual(expected.Length, split.Length);

			for (int i = 0; i < split.Length; i++)
			{
				Assert.AreEqual(expected[i].Length, split[i].Length);

				for (int k = 0; k < expected[i].Length; k++)
				{
					Assert.AreEqual(expected[i][k], split[i][k]);
				}
			}
		}

		[TestMethod]
		public void TestSplit()
		{
			byte[] buffer = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
			byte[] expected1 = new byte[] { 0, 1, 2, 3, 4 };
			byte[] expected2 = new byte[] { 5, 6, 7, 8, 9 };
			byte[][] split = Utils.Split(buffer, 5);

			Assert.AreEqual(expected1.Length, split[0].Length);
			Assert.AreEqual(expected2.Length, split[1].Length);

			for (int i = 0; i < expected1.Length; i++)
			{
				Assert.AreEqual(expected1[i], split[0][i]);
			}

			for (int i = 0; i < expected2.Length; i++)
			{
				Assert.AreEqual(expected2[i], split[1][i]);
			}
		}

		[TestMethod]
		public void TestCombine()
		{
			byte[] buffer1 = new byte[] { 0, 1, 2, 3, 4 };
			byte[] buffer2 = new byte[] { 5, 6, 7, 8, 9 };
			byte[] expected = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
			byte[] combined = Utils.Combine(buffer1, buffer2);

			Assert.AreEqual(expected.Length, combined.Length);

			for (int i = 0; i < expected.Length; i++)
			{
				Assert.AreEqual(expected[i], combined[i]);
			}

			buffer1 = new byte[] { 0, 1, 2, 3, 4 };
			buffer2 = null;
			expected = new byte[] { 0, 1, 2, 3, 4 };
			combined = Utils.Combine(buffer1, buffer2);

			Assert.AreSame(buffer1, combined);
			Assert.AreEqual(expected.Length, combined.Length);

			for (int i = 0; i < expected.Length; i++)
			{
				Assert.AreEqual(expected[i], combined[i]);
			}

			buffer1 = null;
			buffer2 = new byte[] { 5, 6, 7, 8, 9 };
			expected = new byte[] { 5, 6, 7, 8, 9 };
			combined = Utils.Combine(buffer1, buffer2);

			Assert.AreSame(buffer2, combined);
			Assert.AreEqual(expected.Length, combined.Length);

			for (int i = 0; i < expected.Length; i++)
			{
				Assert.AreEqual(expected[i], combined[i]);
			}

			buffer1 = new byte[] { 0, 1, 2, 3, 4 };
			buffer2 = new byte[] { 5, 6, 7, 8, 9 };
			expected = new byte[] { 0, 1, 2, 5, 6, 7, 8, 9 };
			combined = Utils.Combine(buffer1, buffer2, 3);

			Assert.AreEqual(expected.Length, combined.Length);

			for (int i = 0; i < expected.Length; i++)
			{
				Assert.AreEqual(expected[i], combined[i]);
			}

			buffer1 = new byte[] { 0, 1, 2, 3, 4 };
			buffer2 = new byte[] { 5, 6, 7, 8, 9 };
			expected = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
			combined = Utils.Combine(buffer1, buffer2, -1, 3);

			Assert.AreEqual(expected.Length, combined.Length);

			for (int i = 0; i < expected.Length; i++)
			{
				Assert.AreEqual(expected[i], combined[i]);
			}
		}

		[TestMethod]
		public void TestCompare()
		{
			byte[] buffer1 = new byte[] { 0, 1, 2, 3, 4 };

			Assert.AreEqual(true, Utils.Compare(buffer1, buffer1));

			byte[] buffer2 = null;

			Assert.AreEqual(false, Utils.Compare(buffer1, buffer2));
			Assert.AreEqual(false, Utils.Compare(buffer2, buffer1));

			buffer2 = new byte[] { 0, 1, 2, 3 };

			Assert.AreEqual(false, Utils.Compare(buffer1, buffer2));

			buffer2 = new byte[] { 0, 1, 2, 3, 4 };

			Assert.AreEqual(true, Utils.Compare(buffer1, buffer2));

			buffer1 = new byte[] { 0, 1, 2, 3, 4 };
			buffer2 = new byte[] { 5, 6, 7, 8, 9 };

			Assert.AreEqual(false, Utils.Compare(buffer1, buffer2));
		}
	}
}
