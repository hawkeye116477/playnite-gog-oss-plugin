using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace GogOssLibraryNS
{
    public sealed class MemoryLimiter : IDisposable
    {
        private readonly long _maxBytes;
        private long _currentUsage;
        private readonly Queue<(long bytes, TaskCompletionSource<bool> tcs)> _waiters
            = new Queue<(long bytes, TaskCompletionSource<bool> tcs)>();
        private readonly object _lock = new object();
        private bool _disposed;

        public MemoryLimiter(long maxBytes)
        {
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes), "MaxBytes must be positive.");
            _maxBytes = maxBytes;
        }

        public bool TryReserve(long bytes)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MemoryLimiter));
            if (bytes <= 0) return true;

            while (true)
            {
                long current = _currentUsage;
                long newTotal = current + bytes;

                if (newTotal > _maxBytes)
                    return false;

                if (Interlocked.CompareExchange(ref _currentUsage, newTotal, current) == current)
                    return true;
            }
        }

        public void Release(long bytesToRelease)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MemoryLimiter));
            if (bytesToRelease <= 0) return;

            long newValue = Interlocked.Add(ref _currentUsage, -bytesToRelease);

            if (newValue < 0)
            {
                Interlocked.CompareExchange(ref _currentUsage, 0, newValue);
            }

            lock (_lock)
            {
                var ready = new List<TaskCompletionSource<bool>>();
                while (_waiters.Count > 0)
                {
                    var (requiredBytes, tcs) = _waiters.Peek();
                    if (_currentUsage + requiredBytes <= _maxBytes)
                    {
                        _waiters.Dequeue();
                        ready.Add(tcs);
                    }
                    else
                        break;
                }

                foreach (var tcs in ready)
                    tcs.TrySetResult(true);
            }
        }

        public long CurrentUsage => Interlocked.Read(ref _currentUsage);

        public long MaxBytes => _maxBytes;

        public void Dispose()
        {
            _disposed = true;
            Interlocked.Exchange(ref _currentUsage, 0);

            lock (_lock)
            {
                while (_waiters.Count > 0)
                {
                    var (_, tcs) = _waiters.Dequeue();
                }
            }

            GC.SuppressFinalize(this);
        }
    }

}