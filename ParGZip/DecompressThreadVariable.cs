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
	public class DecompressVariable : ThreadVariable
	{
		public DecompressVariable(int bufferSize, string fromPath, string toPath) : base(bufferSize)
		{
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
