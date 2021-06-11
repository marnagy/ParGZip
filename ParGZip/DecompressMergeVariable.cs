using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ParGZip
{
	public class DecompressMergeVariable : ThreadVariable
	{
		public DecompressMergeVariable(int bufferSize, string fromPath) : base(bufferSize)
		{
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
