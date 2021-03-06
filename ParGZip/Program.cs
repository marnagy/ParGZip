using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

using ParGZip;

namespace VeeamTest
{
	class Program
	{
		static void Main(string[] args)
		{
			(string mode, string input, string output, int threads) = LoadAndCheckArgs(args);

			bool success = false;
			string reason = string.Empty;
			switch (mode)
			{
				case "compress":
					Console.WriteLine($"Compressing file {input}...");
					try
					{
						(success, reason) = compressWithTempFiles(input, output, threads);
					}
					catch (Exception)
					{
						success = false;
						reason = "Compression failed. Please, check your available disk space.";
						break;
					}
					if (success)
						Console.WriteLine($"File has been compressed to {output}");
					break;
				case "decompress":
					Console.WriteLine($"Decompressing file {input}...");
					try
					{
						(success, reason) = decompressWithTempFiles(input, output);
					}
					catch (Exception e)
					{
						success = false;
						reason = "Decompression failed. Please, check your available disk space.";
						break;
					}
					if (success)
						Console.WriteLine($"File has been decompressed to {output}");
					break;
				default:
					Console.WriteLine($"Unsupported mode {mode} detected");
					Console.WriteLine("Choose one of the supported modes, please: compress, decompress");
					Environment.Exit(1);
					break;
			}
			if (success)
				Environment.Exit(0);
			else
			{

				Console.WriteLine("Program failed due to the following reason:");
				Console.WriteLine(reason);
				Environment.Exit(1);
			}
		}

		private static (string mode, string input, string output, int threads) LoadAndCheckArgs(string[] args)
		{
			if ( args.Length < 3 || args.Length > 4)
			{
				Console.WriteLine("Invalid number of arguments.");
				Console.WriteLine("Expected arguments: [compress/decompress] [source file name] [archive file name] [threads used (optional, only for compress)]");
				Environment.Exit(1);
			}

			string mode = args[0];

			string input = args[1];
			string output = args[2];
			int threads = Environment.ProcessorCount / 2;

			if ( mode == "compress" && args.Length == 4)
			{
				if ( !int.TryParse(args[3], out threads))
				{
					Console.WriteLine($"Given argument for threads ({args[3]}) is not a valid integer.");
					Environment.Exit(1);
				}
				else if (threads <= 0)
				{
					Console.WriteLine($"Given argument for threads ({args[3]}) is not a positive integer.");
					Environment.Exit(1);
				}
			}

			// test existence of given paths
			if ( !File.Exists(input) )
			{
				Console.WriteLine($"Source file name {input} does not exist.");
				Environment.Exit(1);
			}
			if ( File.Exists(output) )
			{
				Console.WriteLine($"File {output} already exists.");
				Console.WriteLine($"Do you wish to overwrite it? [y|n]");
				string line = Console.ReadLine();

				bool overwrite = line == "y" || line == "Y";

				if ( !overwrite )
					Environment.Exit(1);
			}

			if (threads <= 0)
			{
				Console.WriteLine($"Invalid number of threads: {threads}");
				Console.WriteLine($"Program requires a positive integer");
				Environment.Exit(1);
			}
			return (
				mode,
				input,
				output,
				threads
			);
		}

		private static (bool success, string reason) decompressWithTempFiles(string input, string output)
		{
			Func<int, string> getTmpGZipFilePath = i => $"tmp_{i}.gzip";
			Func<int, string> getTmpFilePath = i => $"tmp_{i}.txt";
			var rand = new Random();
			string tempDirPath = Path.Join(Path.GetTempPath(), rand.Next().ToString() );
			//Console.WriteLine($"DirPath: {tempDirPath}");

			while (Directory.Exists(tempDirPath))
				tempDirPath = Path.Join(Path.GetTempPath(), rand.Next().ToString() );

			var tempDir = Directory.CreateDirectory(tempDirPath);

			int threadCount;
			int bufferSize;
			long[] fileSizes = null;

			try
			{
				using ( var inS = File.Open(input, FileMode.Open, FileAccess.Read))
				{
					using (var bInS = new BinaryReader(inS, Encoding.ASCII, leaveOpen: true))
					{
						threadCount = bInS.ReadInt32();
						bufferSize = bInS.ReadInt32();
						if (threadCount > 1)
						{
							fileSizes = new long[threadCount];
							for (int i = 0; i < threadCount; i++)
							{
								fileSizes[i] = bInS.ReadInt64();
							}
						}
					}
					//Console.WriteLine($"Using {threadCount} threads to decompress");
					//Console.WriteLine($"Using BufferSize: {bufferSize} B");

					if (threadCount == 1)
					{
						byte[] buff = new byte[bufferSize];
						using (var gzip = new GZipStream(inS, CompressionMode.Decompress))
						{
							using (var outS = File.OpenWrite(output))
							{
								int read;
								while ( (read = gzip.Read(buff, 0, bufferSize)) > 0 )
									outS.Write(buff, 0, read);
							}
						}
						return (success: true, reason: string.Empty);
					}
					else
					{
						// split to separate files
						byte[] buffer = new byte[bufferSize];
						for (int i = 0; i < threadCount; i++)
						{
							long counter = 0;
							using (var fs = File.OpenWrite( Path.Join(tempDirPath, getTmpGZipFilePath(i)) ))
							{
								while (counter + bufferSize < fileSizes[i])
								{
									inS.Read(buffer, 0, bufferSize);
									fs.Write(buffer, 0, bufferSize);
									counter += bufferSize;
								}

								int wanted = (int)(fileSizes[i] - counter);
								inS.Read(buffer, 0, wanted);
								fs.Write(buffer, 0, wanted);
							}
						}
					}
				}

				// decompress files
				var decompVars = new DecompressVariable[threadCount];
				for (int i = 0; i < threadCount; i++)
				{
					decompVars[i] = new DecompressVariable(
						bufferSize,
						fromPath: Path.Join(tempDirPath, getTmpGZipFilePath(i)),
						toPath: Path.Join(tempDirPath, getTmpFilePath(i)));
					decompVars[i].Thread.Start();
				}
				for (int i = 0; i < threadCount; i++)
				{
					decompVars[i].Thread.Join();
				}

				//Console.WriteLine("Files decompressed");
				for (int i = 0; i < threadCount; i++)
				{
					File.Delete(Path.Join(tempDirPath, getTmpGZipFilePath(i)));
				}
				//Console.WriteLine($"GZip temp files deleted");

				// merge decompressed files
				var decompMergeVars = new DecompressMergeVariable[threadCount];
				for (int i = 0; i < threadCount; i++)
				{
					decompMergeVars[i] = new DecompressMergeVariable(bufferSize,
						Path.Join(tempDirPath, getTmpFilePath(i)));
					decompMergeVars[i].Thread.Start();
				}

				var consumer = GetConsumerThread(decompMergeVars, output);
				consumer.Start();

				consumer.Join();
				return (success: true, reason: string.Empty);
			}
			finally
			{
				// clean up
				tempDir.Refresh();
				tempDir.Delete(recursive: true);
			}
		}

		private static Thread GetConsumerThread(DecompressMergeVariable[] mergeVars, string outputPath)
		{
			int threadCount = mergeVars.Length;
			return new Thread(new ThreadStart( () =>
			{
				bool[] allEnded = new bool[threadCount];
				using (var ofs = File.Open(outputPath, FileMode.CreateNew, FileAccess.Write))
				{
					while ( !allEnded.All(x => x) )
					{
						for (int i = 0; i < threadCount; i++)
						{
							if (allEnded[i])
								break;

							lock (mergeVars[i])
							{
								while (mergeVars[i].BytesRead == 0)
									Monitor.Wait(mergeVars[i]);

								if (mergeVars[i].BytesRead == -1)
								{
									allEnded[i] = true;
									continue;
								}

								ofs.Write(mergeVars[i].Buffer, 0, mergeVars[i].BytesRead);
								mergeVars[i].BytesRead = 0;

								Monitor.Pulse(mergeVars[i]);
							}
						}
					}
				}
			}));
		}

		private static (bool success, string reason) compressWithTempFiles(string input, string output, int threadCount, int bufferSize = 1 << 20)
		{
			if (threadCount == 1)
			{
				byte[] buffer = new byte[bufferSize];
				using (var inS = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					using (var outS = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
					{
						using (var bOutS = new BinaryWriter(outS, Encoding.ASCII, leaveOpen:true) )
						{
							bOutS.Write( threadCount );
							bOutS.Write( bufferSize );
						}
						using (var gzip = new GZipStream(outS, CompressionMode.Compress, leaveOpen: true) )
						{
							int read;
							while ( ( read = inS.Read(buffer, 0, bufferSize) ) > 0 )
								gzip.Write(buffer, 0, read);
						}
					}
				}
				return (success: true, reason: string.Empty);
			}
			else
			{
				Func<int, string> getTmpFilePath = i => $"tmp_{i}.gzip";
				var rand = new Random();
				string tempDirPath = Path.Join(Path.GetTempPath(), rand.Next().ToString() );
				//Console.WriteLine($"DirPath: {tempDirPath}");

				if ( Directory.Exists(tempDirPath) )
					tempDirPath = Path.Join(Path.GetTempPath(), rand.Next().ToString() );

				var tempDir = Directory.CreateDirectory(tempDirPath);

				try
				{
					var compressionThreadVars = new CompressThreadVariable[threadCount];
					for (int i = 0; i < threadCount; i++)
					{
						compressionThreadVars[i] = new CompressThreadVariable(
							bufferSize, Path.Join(tempDirPath, getTmpFilePath(i)) );
						compressionThreadVars[i].Thread.Start();
					}

					// producer thread
					var producer = GetProducerThread(input, threadCount, bufferSize,
						compressionThreadVars); 
					producer.Start();

					producer.Join();
					for (int i = 0; i < threadCount; i++)
					{
						compressionThreadVars[i].Thread.Join();
					}

					SaveCompressed(
						threadCount,
						bufferSize,
						output,
						tempDir.GetFiles()
						);

					return (success: true, reason: string.Empty);
				}
				finally
				{
					// clean up
					tempDir.Refresh();
					tempDir.Delete(recursive: true);
				}
			}
		}

		private static Thread GetProducerThread(string input, int threadCount, int bufferSize, CompressThreadVariable[] compVars)
		{
			return new Thread(new ThreadStart( () =>
					{
						using ( var inS = File.Open(input, FileMode.Open, FileAccess.Read))
						{
							bool[] allEnded = new bool[threadCount];
							while ( ! allEnded.All(x => x) )
							{
								for (int i = 0; i < threadCount; i++)
								{
									if (allEnded[i])
										break;

									lock (compVars[i])
									{
										while (compVars[i].BytesRead != 0)
										{
											Monitor.Wait(compVars[i]);
										}

										compVars[i].BytesRead = inS.Read(compVars[i].Buffer, 0, bufferSize);
										if (compVars[i].BytesRead == 0)
										{
											compVars[i].BytesRead = -1;
											allEnded[i] = true;
										}

										Monitor.Pulse(compVars[i]);
									}
								}
							}
						}
					}));
		}

		private static void SaveCompressed(int threadCount, int bufferSize, string outputPath, FileInfo[] files)
		{
			using ( var os = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write))
			{
				using (var bos = new BinaryWriter(os, Encoding.ASCII, leaveOpen: true))
				{
					bos.Write( threadCount ); // int32
					bos.Write( bufferSize ); // int32
					Array.Sort(files, (f1, f2) => string.Compare(f1.Name, f2.Name));
					for (int i = 0; i < threadCount; i++)
					{
						bos.Write( files[i].Length ); // int64
						//bos.Write( readTotal[i] ); // int64
					}
				}

				byte[] buffer = new byte[bufferSize];
				for (int i = 0; i < threadCount; i++)
				{
					using (var fs = new FileStream(files[i].FullName, FileMode.Open, FileAccess.Read))
					{
						int read;
						while ( (read = fs.Read(buffer, 0, bufferSize)) > 0)
						{
							os.Write(buffer, 0, read);
						}
					}
				}
			}
		}

		public static (bool success, string reason) compress(string inputPath, string outputPath, int threadCount, int bufferSize = 1 << 20)
		{
			//if (threadCount == 1)
			//{
			//	byte[] buffer = new byte[bufferSize];
			//	// single-thread
			//	using (var fs = File.OpenRead(inputPath))
			//	{
			//		using (var gs = new GZipStream(File.Open(outputPath, FileMode.CreateNew, FileAccess.Write), CompressionLevel.Optimal))
			//		{
			//			int bufferlength = -1;
			//			while ( (bufferlength = fs.Read(buffer, 0, bufferSize) ) > 0)
			//			{
			//				gs.Write(buffer, 0, bufferlength);
			//			}
			//		}
			//	}
			//	return (success: true, reason: string.Empty);
			//}
			//else
			if (threadCount >= 1)
			{
				var inStream = File.OpenRead(inputPath);
				var outStream = File.OpenWrite(outputPath);

				var locks = new object[threadCount];
				var outLocks = new object[threadCount];
				var gZips = new GZipStream[threadCount];
				var outMemStreams = new MemoryStream[threadCount];
				var buffers = new byte[threadCount][];
				var outBuffers = new byte[threadCount][];
				var threads = new Thread[threadCount];
				var shared = new SharedParams()
				{
					BytesRead = new int[threadCount],
					BytesWritten = new int[threadCount],
					BytesInMemoryStream = new int[threadCount]
				};
				for (int i = 0; i < threadCount; i++)
				{
					locks[i] = new object();
					outLocks[i] = new object();
					buffers[i] = new byte[bufferSize];
					outBuffers[i] = new byte[bufferSize];
					outMemStreams[i] = new MemoryStream(bufferSize);
					gZips[i] = new GZipStream(outMemStreams[i], CompressionLevel.Optimal, leaveOpen: true);
					shared.BytesRead[i] = 0;
					int index = i;
					threads[i] = new Thread( new ThreadStart( () =>
						{
							int bytes;
							object threadLock = locks[index];
							while ( true )
							{
								Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId}");
								lock (threadLock)
								{
									while ( (bytes = shared.BytesRead[index]) == 0)
										Monitor.Wait(threadLock);

									// quit thread
									if (bytes == -1)
									{
										lock (outLocks[index])
										{
											Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId} ending");
											shared.BytesWritten[index] = -shared.BytesWritten[index];
											if (shared.BytesWritten[index] == 0)
											{
												shared.BytesWritten[index] = -bufferSize;
											}
											Console.WriteLine($"Last BytesWritten: {shared.BytesWritten[index]}");
											gZips[index].Close();
											//outMemStreams[index].Close();
											Monitor.Pulse(outLocks[index]);
										}

										break;
									}

									gZips[index].Write(buffers[index], 0, bytes);
									gZips[index].Flush();

									lock (outLocks[index])
									{
										// add signal to not read while empty MemoryStream
										while (bufferSize == shared.BytesWritten[index])
											Monitor.Wait(outLocks[index]);

										//int wantedLength = bufferSize - shared.BytesWritten[index];
										//outMemStreams[index].Write(outBuffers[index], shared.BytesWritten[index], wantedLength);
										//shared.BytesWritten[index] += justRead;
										if (bufferSize >= (int)outMemStreams[index].Length)
										{
											Console.WriteLine($"Thread {Thread.CurrentThread.ManagedThreadId} pulses outLock");
											Monitor.Pulse(outLocks[index]);
										}
									}

									shared.BytesRead[index] = 0;

									Monitor.Pulse(threadLock);
								}
							}
						}) 
					);
					Console.WriteLine($"Start thread {i}");
					threads[i].Start();
				}

				// "serve" thread
				var serveThread = new Thread(new ThreadStart( () =>
				{
					bool[] closedThreads = new bool[threadCount];
					bool isInClosed = false;
					for (int i = 0; i < threadCount; i++)
					{
						lock (locks[i])
						{
							var bytes = inStream.Read( buffers[i], 0, bufferSize);
							shared.BytesRead[i] = bytes;
							Monitor.Pulse(locks[i]);
						}
					}

					while ( !closedThreads.All(x => x) )
					{
						for (int i = 0; i < threadCount; i++)
						{
							Console.WriteLine("Serve thread");
							if (closedThreads[i])
								break;

							lock (locks[i])
							{
								while (shared.BytesRead[i] > 0)
								{
									Monitor.Wait(locks[i]);
								}


								int bytesRead = isInClosed ? 0 : inStream.Read(buffers[i], 0, bufferSize);
								if (bytesRead == 0)
								{
									if (!isInClosed)
									{
										inStream.Close();
										isInClosed = true;
									}
									shared.BytesRead[i] = -1;
									closedThreads[i] = true;
								}
								else
									shared.BytesRead[i] = bytesRead;

								Monitor.Pulse(locks[i]);
							}
						}
					}
				}));

				bool[] closedMemStreams = new bool[threadCount];
				// "consume" thread
				var conThread = new Thread(new ThreadStart( () => 
				{
					while ( !closedMemStreams.All(s => s) )
					{
						for (int i = 0; i < threadCount; i++)
						{
							
							if (closedMemStreams[i])
								break;

							lock (outLocks[i])
							{
								while (shared.BytesWritten[i] > 0 && bufferSize > (int)outMemStreams[i].Length )
								{
									Console.WriteLine($"Consumer thread {i}");
									Monitor.Wait(outLocks[i]);
									Console.WriteLine($"Consumer thread {i} - after wait");
								}

								if (closedMemStreams[i])
									break;

								Console.WriteLine($"Writing {i}\nClosed {closedMemStreams[i]}\nWritten: {shared.BytesWritten[i]}");
								// write the rest
								if (shared.BytesWritten[i] < 0 )
								{
									// flust MemoryStream
									int writable = (int)outMemStreams[i].Length;
									if (writable > 0)
									{
										Console.WriteLine($"Writable {writable}");
										outMemStreams[i].Write(outBuffers[i], 0, writable);
										for (int j = writable; j < bufferSize; j++)
										{
											outBuffers[i][j] = 0;
										}
									}
									outMemStreams[i].Close();
									outMemStreams[i] = null;
									closedMemStreams[i] = true;
									if (writable == 0)
									{
										shared.BytesWritten[i] = 0;
										continue;
									}
								}
								else
									outMemStreams[i].Write(outBuffers[i], 0, bufferSize);
								// full buffer
								outStream.Write(outBuffers[i], 0, bufferSize);
								shared.BytesWritten[i] = 0;

								//outStream.Flush();
							}
						}
					}
				} ));
				Console.WriteLine($"Start consumer");
				conThread.Start();
				Console.WriteLine($"Start server");
				serveThread.Start();

				conThread.Join();
				outStream.Close();
				return (success: true, string.Empty);
			}
			else
			{
				return (success: false, reason: "Invalid number of threads");
			}
		}
		public static int Min(int i, long l)
		{
			if (l > i)
			{
				return (int)l;
			}
			return i;
		}
		public static (bool success, string reason) decompress(string inputPath, string outputPath, int threadCount, int bufferSize = 1 << 20)
		{
			return (false, null);
			if (threadCount == 1)
			{
				byte[] buffer = new byte[bufferSize];
				// single-thread
				using (var gs = new GZipStream(File.OpenRead(inputPath), CompressionMode.Decompress))
				{
					using (var os = File.OpenWrite(outputPath))
					{
						int bufferlength = -1;
						while ( (bufferlength = gs.Read(buffer, 0, bufferSize) ) > 0)
						{
							os.Write(buffer, 0, bufferlength);
						}
					}
				}
				return (success: true, reason: string.Empty);
			}
			else if (threadCount > 1)
			{
				var inStream = File.OpenRead(inputPath);
				var outStream = File.OpenWrite(outputPath);

				var locks = new object[threadCount];
			}
			else
			{
				return (success: false, reason: "Invalid number of threads");
			}
		}
	}
}
