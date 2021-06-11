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
	public class CompressThreadVariables
	{
		public int BytesRead { get; set; }
		public long ReadTotal { get; private set; }
		public byte[] Buffer { get; }
		public Thread Thread { get; }
		//private readonly string tempOutFilePath;

		public CompressThreadVariables(int bufferSize, string tempOutFilePath)
		{
			if (bufferSize <= 0)
				throw new ArgumentException("Cannot have buffer of non-positive size.");

			//this.tempOutFilePath = tempOutFilePath;
			BytesRead = 0;
			ReadTotal = 0;
			Buffer = new byte[bufferSize];
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
