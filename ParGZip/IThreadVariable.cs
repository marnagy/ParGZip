using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ParGZip
{
	public abstract class ThreadVariable
	{
		public int BytesRead { get; set; }
		public byte[] Buffer { get; }
		public long ReadTotal { get; protected set; }
		public Thread Thread { get; protected set; }
		protected ThreadVariable(int bufferSize)
		{
			if (bufferSize <= 0)
				throw new ArgumentException("Cannot have buffer of non-positive size.");

			Buffer = new byte[bufferSize];
			BytesRead = 0;
			ReadTotal = 0;
		}
	}
}
