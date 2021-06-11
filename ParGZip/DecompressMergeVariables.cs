using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ParGZip
{
	public class DecompressMergeVariables
	{
		public int BytesRead { get; set; }
		public byte[] Buffer { get; }
		public readonly Thread Thread;
		public DecompressMergeVariables(int bufferSize, string fromPath)
		{
			if (bufferSize <= 0)
				throw new ArgumentException("Cannot create a buffer of non-positive size");

			Buffer = new byte[bufferSize];
			Thread = new Thread(new ThreadStart( () => ThreadAction(fromPath) ));
		}

		private void ThreadAction(string fromPath)
		{
			using (var fs = File.OpenRead( fromPath ))
			{
				while (BytesRead != -1)
				{
					lock (this)
					{
						while (BytesRead > 0)
							Monitor.Wait(this);

						BytesRead = fs.Read(Buffer, 0, Buffer.Length);

						if (BytesRead == 0)
							BytesRead = -1;

						Monitor.Pulse(this);
					}
				}
			}
		}
	}
}
