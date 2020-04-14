using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CompressionApp
{
    public class Compressor
    {
        private Thread _main;
        private List<Thread> _workers;
        private ConcurrentQueue<CompressionChunk> _chunkQueue;
        private ConcurrentQueue<Tuple<int, string>> _filesQueue;
        private SortedList<int, CompressionChunk> _sortedChunkList;
        private List<Error> _errors;

        private bool _reading;
        private bool _forceStop;
        private bool _isMultiCoreSystem;

        private object _sortedChunkListLocker;
        private object _errorsLocker;

        private string _inputFile;
        private string _outputFile;
        private string _baseDir;

        private const int chunkSize = 1000000;
        private const int _maxQueueCapacity = 200;
        private static readonly Regex chunkNumReg = new Regex("_[0-9]+\\.zip");

        public Compressor(string inputFile, string outputFile)
        {
            var inputFileFullPath = !Utils.IsFullPath(inputFile) ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, inputFile) : inputFile;
            _baseDir = Path.GetDirectoryName(inputFileFullPath);
            _inputFile = Path.GetFileName(inputFileFullPath);
            _outputFile = outputFile;
            _errors = new List<Error>();
            _errorsLocker = new object();
            _isMultiCoreSystem = Environment.ProcessorCount > 1;
        }

        public int Compress()
        {
            int result = 1;
            var numberOfCoresForWriters = _isMultiCoreSystem ? Environment.ProcessorCount - 1 : 1;
            _workers = new List<Thread>(numberOfCoresForWriters);
            _chunkQueue = new ConcurrentQueue<CompressionChunk>();
            _errors.Clear();
            _reading = true;

            var affinityMask = _isMultiCoreSystem ? new IntPtr(1) : IntPtr.Zero;
            _main = new Thread(() => ProduceChunks(affinityMask));
            _main.Name = "Reader thread";
            _main.Start();

            for (int i = 0; i < numberOfCoresForWriters; i++)
            {
                var mask = _isMultiCoreSystem ? new IntPtr(1 << (i + 1)) : IntPtr.Zero;
                Thread th = new Thread(() => WriteChunks(mask));
                th.Name = "Writer thread #" + i;
                th.Start();
                _workers.Add(th);
            }

            _main.Join();

            foreach (var th in _workers)
            {
                th.Join();
            }

            if (HasErrors())
                result = 0;

            CleanUp();

            return result;
        }

        public int Decompress()
        {
            int result = 1;

            // find all relevant zip files
            var chunksFound = findAllChunks(_baseDir, _inputFile);

            _filesQueue = new ConcurrentQueue<Tuple<int, string>>();
            _errors.Clear();

            foreach (var ch in chunksFound)
            {
                _filesQueue.Enqueue(ch);
            }
            _sortedChunkList = new SortedList<int, CompressionChunk>();
            _sortedChunkListLocker = new object();

            var numberOfCoresForChunkReaders = _isMultiCoreSystem ? Environment.ProcessorCount - 1 : 1;
            _workers = new List<Thread>(numberOfCoresForChunkReaders);

            for (int i = 0; i < numberOfCoresForChunkReaders; i++)
            {
                var mask = _isMultiCoreSystem ? new IntPtr(1 << (i + 1)) : IntPtr.Zero;
                Thread th = new Thread(() => ReadChunks(mask));
                th.Name = "Zip Chunks reader thread #" + i;
                th.Start();
                _workers.Add(th);
            }

            var affinityMask = _isMultiCoreSystem ? new IntPtr(1) : IntPtr.Zero;
            _main = new Thread(() => WriteUnzipedChunks(affinityMask));
            _main.Name = "Writer thread";
            _main.Start();
            _reading = true;

            foreach (var th in _workers)
            {
                th.Join();
            }
            _reading = false;

            _main.Join();

            if (HasErrors())
                result = 0;

            CleanUp();

            return result;
        }

        private void ProduceChunks(IntPtr affinityMask)
        {
            var inputFilePath = Path.Combine(_baseDir, _inputFile);
            try
            {
                TrySetAffinityForCurrentThread(affinityMask);

                using (FileStream sourceFileSream = new FileStream(inputFilePath, FileMode.Open))
                {
                    int count = 1;
                    byte[] buffer = new byte[chunkSize];
                    int bytesRead;
                    while ((bytesRead = sourceFileSream.Read(buffer, 0, buffer.Length)) > 0 && !_forceStop)
                    {
                        byte[] data = new byte[bytesRead];
                        Buffer.BlockCopy(buffer, 0, data, 0, bytesRead);
                        CompressionChunk chunk = new CompressionChunk()
                        {
                            Number = count,
                            Data = data
                        };
                        _chunkQueue.Enqueue(chunk);
                        count++;

                        if (_chunkQueue.Count >= _maxQueueCapacity)
                        {
                            // if _chunkQueue is full, wait to slow down a little.
                            Thread.Sleep(200);
                            continue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError($"Error accured in ProduceChunks thread name:{Thread.CurrentThread.Name} thread id:{Thread.CurrentThread.ManagedThreadId}", ex);
            }
            _reading = false;
        }

        private void WriteChunks(IntPtr affinityMask)
        {
            try
            {
                TrySetAffinityForCurrentThread(affinityMask);

                while ((_reading || _chunkQueue.Count > 0) && !_forceStop)
                {
                    if (_chunkQueue.TryDequeue(out CompressionChunk chunk))
                    {
                        var filePath = Path.Combine(_baseDir, $"{_outputFile}_{chunk.Number}.zip");
                        using (FileStream fs = new FileStream(filePath, FileMode.CreateNew))
                        using (GZipStream zipStream = new GZipStream(fs, CompressionMode.Compress))
                        {
                            zipStream.Write(chunk.Data, 0, chunk.Data.Length);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError($"Error accured in WriteChunks, thread name:{Thread.CurrentThread.Name} thread id:{Thread.CurrentThread.ManagedThreadId}", ex);
            }
        }

        private void ReadChunks(IntPtr affinityMask)
        {
            if (_filesQueue != null)
            {
                try
                {
                    TrySetAffinityForCurrentThread(affinityMask);

                    while (_filesQueue.Count != 0 && !_forceStop)
                    {
                        if (_sortedChunkList.Count >= _maxQueueCapacity)
                        {
                            // if _chunkQueue is full, wait and pass to the next iteration
                            Thread.Sleep(200);
                            continue;
                        }
                        if (_filesQueue.TryDequeue(out Tuple<int, string> chunkFile))
                        {
                            var zipedFile = Path.Combine(_baseDir, chunkFile.Item2);
                            using (MemoryStream memStream = new MemoryStream())
                            using (Stream zippedFileStream = File.OpenRead(zipedFile))
                            using (Stream csStream = new GZipStream(zippedFileStream, CompressionMode.Decompress))
                            {
                                byte[] buffer = new byte[1024];
                                int bytesRead;
                                while ((bytesRead = csStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    memStream.Write(buffer, 0, bytesRead);
                                }

                                CompressionChunk chunk = new CompressionChunk()
                                {
                                    Number = chunkFile.Item1,
                                    Data = memStream.ToArray()
                                };
                                lock (_sortedChunkListLocker)
                                {
                                    _sortedChunkList.Add(chunk.Number, chunk);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    HandleError($"Error accured in ReadChunks, thread name:{Thread.CurrentThread.Name} thread id:{Thread.CurrentThread.ManagedThreadId}", ex);
                }
            }
        }

        private void WriteUnzipedChunks(IntPtr affinityMask)
        {
            // todo: first chunk number must be past as parameter or taken from class member.
            int count = 1;
            var outputFilePath = Path.Combine(_baseDir, _outputFile);
            CompressionChunk chunk = null;
            try
            {
                TrySetAffinityForCurrentThread(affinityMask);

                using (FileStream destFileSream = new FileStream(outputFilePath, FileMode.Create))
                {
                    while ((_reading || _sortedChunkList.Count > 0) && !_forceStop)
                    {
                        if (_sortedChunkList.Count > 0 && _sortedChunkList.ElementAt(0).Key == count)
                        {
                            lock (_sortedChunkListLocker)
                            {
                                // no need to check twice the key existance because there is only one writer.
                                chunk = _sortedChunkList.ElementAt(0).Value;
                                _sortedChunkList.RemoveAt(0);
                            }
                            destFileSream.Write(chunk.Data, 0, chunk.Data.Length);
                            count++;
                        }
                        else
                        {
                            // if list is stil or already empty, wait.
                            Thread.Sleep(100);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError($"Error accured in WriteUnzipedChunks, thread name:{Thread.CurrentThread.Name} thread id:{Thread.CurrentThread.ManagedThreadId}", ex);
            }
        }

        private List<Tuple<int, string>> findAllChunks(string baseDir, string inputFile)
        {
            List<Tuple<int, string>> files = new List<Tuple<int, string>>();
            var _files = Directory.GetFiles(baseDir);
            var cleanInputFile = chunkNumReg.Replace(inputFile, "");
            Regex specificFilereg = new Regex($"{cleanInputFile}_[0-9]+\\.zip");
            foreach (var file in _files)
            {
                if (specificFilereg.IsMatch(file))
                {
                    string numberStr = file.Substring(file.LastIndexOf("_") + 1).Replace(".zip", "");
                    files.Add(new Tuple<int, string>(int.Parse(numberStr), file));
                }
            }
            // sort by numbers
            files.Sort((f1, f2) => f1.Item1.CompareTo(f2.Item1));

            // Validation
            if (files.Count == 0)
            {
                throw new Exception("Zip files not found");
            }
            if (files.Last().Item1 != files.Count)
            {
                throw new Exception("Some zip files are missing");
            }

            return files;
        }

        private void TrySetAffinityForCurrentThread(IntPtr mask)
        {
            if (mask != IntPtr.Zero)
                Utils.SetThreadAffinityForCurrentThread(mask);
        }

        private void HandleError(string message, Exception ex)
        {
            _forceStop = true;
            lock (_errorsLocker)
            {
                _errors.Add(new Error(message, ex));
            }
        }

        private bool HasErrors()
        {
            return _errors.Count > 0;
        }

        private void CleanUp()
        {
            _chunkQueue = null;
            _filesQueue = null;
            _sortedChunkList = null;
            _sortedChunkListLocker = null;
            _errors.Clear();
            _forceStop = false;
            _reading = false;
        }
    }
}