# Parallel compression in .NET 5 using GZipStream class

------

## Architecture

### Arguments

Program expects 3/4 arguments: mode [compress, decompress], path to source file, path of file to be created (output path), number of threads to use (optional).

Program checks for existence of source file path and output path. If output path already exists, program prompts user for input whether program should overwrite the file or not.

### Compressed file format

File begins with two 32-bit integers for thread count and buffer size respectively.

Than format differs depending on the thread count:

- *1 thread:* simple single-threaded compressed
- *N threads:* 
	- *N* 64-bit integers signifying length of compressed data for each thread
	- All compressed data from threads in ascending order

### Compression

Program creates *consumer threads* for compression and creates a *producer thread* that fills buffers of the consumer threads in a sequential manner. (i.e. for 4 threads: 1,2,3,4,1,2,3,4...)

Consumer threads save compressed output to temporary files stored in temporary directory that will be deleted after the compression is done.

Then we add prefix and we append files in ascending order to the output path. (output for 1<sup>st</sup> consumer thread, output for 2<sup>nd</sup> consumer thread, ...).

If we use **only 1 thread**:

We save thread count and buffer size to output path followed by simple single-threaded compression.

### Decompression

Program first reads thread count, buffer size and compressed-data sizes.

Then we separate these files into directory created in temporary directory, we decompress each of them to separate files, and delete the compressed files.

Finally, we merge decompressed files into output path in sequential manner. (i.e. for 4 threads: 1,2,3,4,1,2,3,4...)

If we use **only 1 thread**:

We read thread count and buffer size from source file followed by simple single-threaded decompression.

---

## Default Values

- threads: a half of logical processors

- buffer size: 1 MiB = 2<sup>20</sup> B

## Used algorithms 

Main algorithm used is algorithm for solving *producer-consumer problem* for **many-to-one** and **one-to-many** versions.

No other significant algorithms were used in this project.
