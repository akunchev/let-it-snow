using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReactiveUI;
using SixLabors.ImageSharp;

namespace AvaloniaCoreSnow
{
    public class SnowViewModel : ReactiveObject
    {
        private const byte MaxSpeed = 200;

        private int _flakeCount = 3000;

        private Flake[] _flakes;

        private readonly Random _rnd = new Random();

        private int _delayMs = 10;

        public SnowViewModel()
        {
            ResetCommand = ReactiveCommand.Create(Reset);

            // Bgra8888 is device-native and much faster.
            Bitmap = new DynamicWriteableBitmap( new PixelSize(640, 480), PixelFormat.Bgra8888);
            BitmapSize = BitmapSizes.Skip(1).First();
            Bitmap.AutoInvalidate = true;
            IsDoubleBuffered = true;
            Reset();
            Task.Run(() => MoveFlakes());
        }

        public DynamicWriteableBitmap Bitmap { get; }

        private bool _isDoubleBuffered;

        public bool IsDoubleBuffered
        {
            get { lock (_lock) { return _isDoubleBuffered; } }
            set { lock (_lock) { _isDoubleBuffered = value; } }
        }

        private WriteableBitmap WriteBitmap;

        private ILockedFramebuffer LockForWrite()
        {
            lock (_lock)
            {
                return IsDoubleBuffered ? WriteBitmap.Lock() : Bitmap.Lock();
            }
        }

        public int FlakeCount
        {
            get => _flakeCount;
            set
            {
                ResizeFlakes(value);
                this.RaisePropertyChanged();
            }
        }

        public int DelayMsInverted
        {
            get => MaxDelay - _delayMs;
            set
            {
                _delayMs = MaxDelay - value;

                this.RaisePropertyChanged(nameof(DelayInfo));
            }
        }

        public string[] BitmapSizes { get; } = new[]
        {
            "320x240",
            "640x480",
            "1280x720",
            "1920x1080"
        };

        private string _bitmapSize;

        public string BitmapSize
        {
            get { return _bitmapSize; }
            set
            {
                this.RaiseAndSetIfChanged(ref _bitmapSize, value);
                UpdateBitmapSize();
            }
        }

        private void UpdateBitmapSize()
        {
            string[] tmp = BitmapSize.Split('x');

            int width = int.Parse(tmp[0]);
            int height = int.Parse(tmp[1]);

            lock (_lock)
            {
                Bitmap.Update( new PixelSize(width, height), PixelFormat.Bgra8888);
                WriteBitmap = new WriteableBitmap(new PixelSize(width, height), Bitmap.Dpi,  PixelFormat.Bgra8888);

                Reset();
            }
        }

        public string DelayInfo => DelayMsInverted == 0 ?
            "Paused" : $"{_delayMs} ms, max FPS={(1000 / _delayMs).ToString("000")}";

        public int MaxDelay => 50;

        public ICommand ResetCommand { get; }

        public IEnumerable<Color> Brushes => new[]
        {
            Colors.Red, Colors.Orange, Colors.Yellow, Colors.Green, Colors.Cyan, Colors.Blue,
            Color.FromArgb(250, 0, 0, 0)
        };

        public Color SelectedBrush { get; set; } = Colors.Red;

        public unsafe void PutPixel(double x, double y, int size)
        {
            // Convert relative to absolute.
            var width = WriteBitmap.PixelSize.Width;
            var height = WriteBitmap.PixelSize.Height;

            var px = (int)(x * width);
            var py = (int)(y * height);

            var c = SelectedBrush;
            var pixel = c.B + ((uint)c.G << 8) + ((uint)c.R << 16) + ((uint)c.A << 24);
            lock (_lock)
            {
                using (var buf = LockForWrite())
                {
                    for (var x0 = px - size; x0 <= px + size; x0++)
                        for (var y0 = py - size; y0 <= py + size; y0++)
                        {
                            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                            {
                                var ptr = (uint*)buf.Address;
                                ptr += (uint)(width * y0 + x0);

                                *ptr = pixel;
                            }
                        }
                }
            }
        }

        public unsafe void LoadFile(string fileName, double x, double y)
        {
            // Convert relative to absolute.
            var width = WriteBitmap.PixelSize.Width;
            var height = WriteBitmap.PixelSize.Height;

            var px = (int)(x * width);
            var py = (int)(y * height);

            using (var img = Image.Load(fileName))
            using (var buf = LockForWrite())
            {
                var w = Math.Min(width - px, img.Width);
                var h = Math.Min(height - py, img.Height);

                var ptr = (uint*)buf.Address;

                // Move snow out of the picture rectangle.
                for (var i = 0; i < _flakes.Length; i++)
                {
                    ref var f = ref _flakes[i];

                    if (f.X > px && f.Y > py && f.X < px + w && f.Y < py + h)
                    {
                        // Clear pixel and reset flake to top.
                        *(ptr + f.Y * width + f.X) = 0;
                        InitFlake(ref f);
                    }
                }

                // Load picture.
                for (var i = 0; i < w; i++)
                    for (var j = 0; j < h; j++)
                    {
                        var pix = img[i, j];

                        // Alpha threshold: transparent pixels don't work with snow logic.
                        if (pix.A > 200)
                        {
                            var pixPtr = ptr + (j + py) * width + i + px;
                            *pixPtr = (uint)(pix.B | pix.G << 8 | pix.R << 16 | byte.MaxValue << 24);
                        }
                    }
            }
        }

        private object _lock = new object();

        private void Reset()
        {
            lock (_lock)
            {
                InitFlakes();
                ResetBitmap();
            }
        }

        private void InitFlakes()
        {
            _flakes = new Flake[_flakeCount];

            for (var i = 0; i < _flakes.Length; i++)
            {
                ref var f = ref _flakes[i];
                InitFlake(ref f);
                f.Y = (short)_rnd.Next(40);
                f.Y2 = 0;
            }
        }

        private void InitFlake(ref Flake f)
        {
            var tone = (byte)_rnd.Next(MaxSpeed);
            f.X = (short)_rnd.Next(WriteBitmap.PixelSize.Width);
            f.Speed = tone;
            f.Y = 0;
            f.Y2 = 0;
        }

        private unsafe void ResizeFlakes(int newCount)
        {
            using (var buf = WriteBitmap.Lock())
            {
                var ptr = (uint*)buf.Address;
                var old = _flakes;
                var oldCount = _flakeCount;
                _flakes = new Flake[newCount];

                if (newCount < oldCount)
                {
                    // Remove extra flakes, trim array.
                    for (var i = newCount; i < oldCount; i++)
                    {
                        *(ptr + old[i].X + old[i].Y * WriteBitmap.PixelSize.Width) = 0;
                    }

                    Array.Copy(old, _flakes, newCount);
                }
                else
                {
                    // Add more flakes.
                    Array.Copy(old, _flakes, oldCount);

                    for (var i = oldCount; i < newCount; i++)
                    {
                        InitFlake(ref _flakes[i]);
                    }
                }
            }

            _flakeCount = newCount;
        }

        private unsafe void ResetBitmap()
        {
            using (var buf = LockForWrite())
            {
                var ptr = (uint*)buf.Address;

                var w = WriteBitmap.PixelSize.Width;
                var h = WriteBitmap.PixelSize.Height;

                // Clear.
                for (var i = 0; i < w * (h - 1); i++)
                {
                    *(ptr + i) = 0;
                }

                // Draw bottom line.
                for (var i = w * (h - 1); i < w * h; i++)
                {
                    *(ptr + i) = uint.MaxValue;
                }
            }
        }

        private unsafe void MoveFlakes()
        {
            int mswork = 0;
            while (true)
            {
                lock (_lock)
                {
                    mswork = 0;
                    // MaxDelay means pause.
                    if (_delayMs < MaxDelay)
                    {
                        var start = DateTime.Now;
                        var bmp = WriteBitmap;
                        var w = bmp.PixelSize.Width;

                        using (var buf = LockForWrite())
                        {
                            var ptr = (uint*)buf.Address;

                            var flakes = _flakes;

                            for (var i = 0; i < flakes.Length; i++)
                            {
                                MoveFlake(ref flakes[i], ptr, w);
                            }

                            if (IsDoubleBuffered)
                            {
                                using (var rb = Bitmap.Lock())
                                {
                                    long size = rb.Size.Width * rb.Size.Height * 4;
                                    long buffsize = buf.Size.Width * buf.Size.Height * 4;
                                    if (size == buffsize)
                                    {
                                        Buffer.MemoryCopy((void*)buf.Address, (void*)rb.Address, size, size);
                                    }
                                }
                            }
                        }

                        mswork = Convert.ToInt32((DateTime.Now - start).TotalMilliseconds);
                    }
                }

                if (!Bitmap.AutoInvalidate)
                {
                    //wait until it's renderer then generate new image
                    //it's optional no need to wait, but in case we need it
                    Bitmap.InvalidateAndRenderAsync().Wait();
                }

                int delay = _delayMs - mswork;

                if (delay < 1) delay = 1;
                Thread.Sleep(delay);
            }
        }

        private unsafe void MoveFlake(ref Flake f, uint* ptr, int width)
        {
            f.Y2 += f.Speed;

            const short slowdown = 200;
            if (f.Y2 < slowdown)
            {
                return;
            }

            // Erase old flake.
            var oldPtr = ptr + width * f.Y + f.X;
            *oldPtr = 0;

            // New position.
            f.Y2 = (short)(f.Y2 % slowdown);
            f.Y++;
            var newPtr = oldPtr + width;
            var newAlphaPtr = (byte*)newPtr + 3;

            // Check snow below us.
            if (*newAlphaPtr == byte.MaxValue)
            {
                // Check pixels to the left or to the right: we might be on a slope.
                if (f.X > 0 && *(newAlphaPtr - 4) != byte.MaxValue)
                {
                    f.X--;
                    newPtr--;
                }
                else if (f.X + 1 < width && *(newAlphaPtr + 4) != byte.MaxValue)
                {
                    f.X++;
                    newPtr++;
                }
                else
                {
                    // Not on a slope, stop here and preserve the pixel.
                    InitFlake(ref f);
                    newPtr = ptr + width * f.Y + f.X;

                    // Mark as static by setting alpha to 255.
                    // Make persistent color lighter.
                    var clr = MaxSpeed * 0.8 + f.Speed * 0.2;
                    *oldPtr = GetGray((byte)clr) | 0xFF000000;
                }
            }

            *newPtr = GetGray(f.Speed);
        }

        private static uint GetGray(byte tone)
        {
            var c = (byte)(byte.MaxValue - MaxSpeed + tone);

            // Non-max alpha indicates moving pixel.
            return (uint)(c | c << 8 | c << 16 | 0xFE000000);
        }

        private struct Flake
        {
            public short X;
            public short Y;
            public short Y2;
            public byte Speed;
        }
    }
}