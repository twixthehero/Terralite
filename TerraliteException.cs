using System;

namespace Terralite
{
	public class TerraliteException : Exception
	{
		public TerraliteException() { }
		public TerraliteException(string message) : base(message) { }
		public TerraliteException(string message, Exception inner) : base(message, inner) { }
	}
}
