using System;
using System.IO;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Utilities;

namespace AvaloniaCoreSnow
{
    /// <summary>
    /// we don't need to implement bitmap tbh
    /// but why not
    /// </summary>
    public class DynamicWriteableBitmap : IBitmap
    {
        public Vector Dpi { get; } = new Vector(96, 96);

        public PixelSize PixelSize { get; private set; }

        public PixelFormat PixelFormat { get; private set; }

        public IRef<IBitmapImpl> PlatformImpl => Inner?.PlatformImpl;

        public Size Size => Inner.Size;

        private WriteableBitmap _inner;

        private WriteableBitmap Inner => _inner ?? (_inner = new WriteableBitmap(PixelSize, Dpi, PixelFormat));

        private readonly object _lock = new object();

        public DynamicWriteableBitmap(PixelSize pixelSize, PixelFormat? format = null)
        {
            Update(pixelSize, format);
        }

        public void Update(PixelSize pixelSize, PixelFormat? format = null)
        {
            var newFormat = format ?? PixelFormat.Bgra8888;

            using (LockInternal())
            {
                if (PixelSize != pixelSize || PixelFormat != newFormat)
                {
                    PixelSize = pixelSize;
                    PixelFormat = newFormat;
                    DisposeInner();
                }
            }
        }

        public bool AutoInvalidate { get; set; } = true;

        private void DisposeInner()
        {
            using (LockInternal())
            {
                var inner = _inner;
                _inner = null;

                inner?.Dispose();
            }
        }

        public void Dispose()
        {
            DisposeInner();
        }

        public void Save(string fileName)
        {
            using (LockInternal())
            {
                Inner?.Save(fileName);
            }
        }

        public void Save(Stream stream)
        {
            using (LockInternal())
            {
                Inner?.Save(stream);
            }
        }

        public event EventHandler<EventArgs> NeedInvalidate;

        private TaskCompletionSource<bool> _invalidateAsync = null;

        public void Invalidate()
        {
            InvalidateAndRenderAsync();
        }

        public Task InvalidateAndRenderAsync()
        {
            using (LockInternal())
            {
                if (_invalidateAsync != null) return _invalidateAsync.Task;

                if (NeedInvalidate != null)
                {
                    NeedInvalidate?.Invoke(this, EventArgs.Empty);
                    _invalidateAsync = _invalidateAsync ?? new TaskCompletionSource<bool>();
                    return _invalidateAsync.Task;
                }

                return Task.FromResult(false);
            }
        }

        private IDisposable CreateLock(object lockobj)
        {
            // return Disposable.Empty;
            Monitor.Enter(lockobj);

            return Disposable.Create(() => Monitor.Exit(lockobj));
        }

        private IDisposable LockInternal() => CreateLock(_lock);

        public ILockedFramebuffer Lock()
        {
            var d = LockInternal();
            return new LockedFramebufferWrapper(Inner.Lock(),
                Disposable.Create(() =>
                {
                    if (AutoInvalidate)
                    {
                        Invalidate();
                    }

                    d.Dispose();
                }));
        }

        public void Render(DrawingContext context, double opacity, Rect sourceRect, Rect destRect)
        {
            using (LockInternal())
            {
                _invalidateAsync?.SetResult(true);
                _invalidateAsync = null;

                var bitmap = Inner;
                if (bitmap != null)
                {
                    context.DrawImage(bitmap, opacity, sourceRect, destRect);
                }
            }
        }

        /// <summary>
        /// if we need safe way to render bitmap with some custom logic
        /// </summary>
        /// <param name="callback"></param>
        public void Render(Action<IBitmap> callback)
        {
            using (LockInternal())
            {
                _invalidateAsync?.SetResult(true);
                _invalidateAsync = null;

                var bitmap = Inner;
                if (bitmap != null)
                {
                    callback(bitmap);
                }
            }
        }

        private class LockedFramebufferWrapper : ILockedFramebuffer
        {
            private ILockedFramebuffer _inner;
            private IDisposable _disposable;

            public LockedFramebufferWrapper(ILockedFramebuffer inner, IDisposable disposable)
            {
                _inner = inner;
                _disposable = disposable;
            }

            public IntPtr Address => _inner.Address;

            public int RowBytes => _inner.RowBytes;

            public Vector Dpi => _inner.Dpi;

            public PixelFormat Format => _inner.Format;

            public PixelSize Size => _inner.Size;

            public void Dispose()
            {
                _inner.Dispose();
                _disposable.Dispose();
            }
        }
    }
}