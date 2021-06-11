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
	public class DecompressVariables
	{
		public byte[] Buffer { get; }
		public int BytesRead { get; private set; }
		public readonly Thread Thread;
		public DecompressVariables(int bufferSize, string fromPath, string toPath)
		{
			if (bufferSize <= 0)
				throw new ArgumentException("Cannot create a buffer of non-positive size");

			Buffer = new byte[bufferSize];
			Thread = new Thread(new ThreadStart( () => ThreadAction(fromPath, toPath) ));
		}
		private void ThreadAction(string from, string to)
		{
			using (var gzip = new GZipStream(File.OpenRead( from ), CompressionMode.Decompress))
			{
				using (var fs = File.OpenWrite( to ))
				{
					int read;
					while ( (read = gzip.Read(Buffer, 0, Buffer.Length) ) > 0)
					{
						fs.Write(Buffer, 0, read);
					}
							
				}
			}
		}
	}
}
