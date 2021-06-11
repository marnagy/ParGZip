using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ParGZip
{
	public class CompressThreadVariable : ThreadVariable
	{
		public CompressThreadVariable(int bufferSize, string tempOutFilePath) : base(bufferSize)
		{
			Thread = new Thread(new ThreadStart( () => ThreadAction(tempOutFilePath) ));
		}
		private void ThreadAction(string tempFilePath)
		{
			using (var gzip = new GZipStream(
					new FileStream(tempFilePath, FileMode.Create),
					CompressionMode.Compress)
				)
				{
					while (true)
					{
						lock (this)
						{
							while (BytesRead == 0)
							{
								Monitor.Wait(this);
							}

							if (BytesRead == -1)
								break;

							gzip.Write(Buffer, 0, BytesRead);
							ReadTotal += BytesRead;
							BytesRead = 0;

							Monitor.Pulse(this);
						}
					}
				}
		}
	}
}
