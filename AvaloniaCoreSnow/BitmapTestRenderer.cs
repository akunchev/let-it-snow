using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AvaloniaCoreSnow
{
    public class BitmapTestRenderer : Control
    {
        public static AvaloniaProperty<IBitmap> BitmapProperty =
            AvaloniaProperty.RegisterDirect<BitmapTestRenderer, IBitmap>(nameof(Bitmap), t => t.Bitmap, (t, b) => t.Bitmap = b);

        private IBitmap _bitmap;

        public IBitmap Bitmap
        {
            get => _bitmap;
            set
            {
                var dbb = _bitmap as DynamicWriteableBitmap;
                if (dbb != null) dbb.NeedInvalidate -= Bitmap_NeedInvalidate;

                this.SetAndRaise(BitmapProperty, ref _bitmap, value);

                dbb = _bitmap as DynamicWriteableBitmap;
                if (dbb != null) dbb.NeedInvalidate += Bitmap_NeedInvalidate;
            }
        }

        private Typeface _typeFace = new Typeface("Segoe UI");

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            _renderStats.BeginRender();
            RenderBitmap(context);
            _renderStats.EndRender();

            string stats = _renderStats.ToString();

            context.DrawText(Brushes.White, new Point(20, 20), new FormattedText() { Typeface = _typeFace, Text = stats });

            Debug.WriteLine(stats);
        }

        private void Bitmap_NeedInvalidate(object sender, EventArgs e)
        {
            //may be invoke on dispatcher is safer
            Dispatcher.UIThread.Post(() => InvalidateVisual());
            //InvalidateVisual();
            _renderStats.RenderScheduled();
        }

        private RenderStats _renderStats = new RenderStats();

        private void RenderBitmap(DrawingContext context)
        {
            if (string.IsNullOrEmpty(_renderStats.RenderingEngine) && Bitmap != null)
            {
                var bmType = Bitmap.PlatformImpl.Item.GetType();
                var rendering = bmType.FullName.Contains("Skia") ? "Skia" : "Direct2D";
                var defered = context.PlatformImpl.GetType().FullName.Contains("Deferred") ? "Deferred" : "NotDeferrred";
                _renderStats.RenderingEngine = $"{rendering}/{defered}";
            }

            var bm = Bitmap;

            var dbBmp = bm as DynamicWriteableBitmap;

            if (dbBmp != null)
            {
                //_renderStats.BeginRenderBitmap();
                //dbBmp.Render(context, 1, new Rect(0, 0, bm.PixelWidth, bm.PixelHeight), Bounds);
                //_renderStats.EndRenderBitmap();

                dbBmp.Render(b =>
                {
                    _renderStats.BeginRenderBitmap();
                    context.DrawImage(b, 1, new Rect(0, 0, bm.PixelSize.Width, bm.PixelSize.Height), Bounds);
                    _renderStats.EndRenderBitmap();
                });
            }
            else if (bm != null)
            {
                _renderStats.BeginRenderBitmap();
                context.DrawImage(bm, 1, new Rect(0, 0, bm.PixelSize.Width, bm.PixelSize.Height), Bounds);
                _renderStats.EndRenderBitmap();
            }
        }
    }

    public class RenderStats
    {
        private int _frames = 0;
        private int SamplesCount = 60;
        private int MinSamplesCount = 10;

        private DateTime _begin;
        private DateTime _end;
        private DateTime _beginBM;
        private DateTime _endBM;

        private Queue<double> _bmRender = new Queue<double>();
        private Queue<double> _render = new Queue<double>();
        private Queue<double> _timeToRender = new Queue<double>();
        private Queue<DateTime> _frameTimes = new Queue<DateTime>();

        private double _avgRenderTimems;
        private double _avgbmRenderTimems;
        private double _avgTimeToRenderms;
        private double _fps;
        private DateTime _lastStat = DateTime.Now;

        DateTime _scheduled;

        public void RenderScheduled()
        {
            _scheduled = DateTime.Now;
        }

        public void BeginRender()
        {
            _begin = DateTime.Now;
            _frames++;
        }

        public void EndRender()
        {
            _end = DateTime.Now;

            //add data
            _bmRender.Enqueue((_endBM - _beginBM).TotalMilliseconds);
            _render.Enqueue((_end - _begin).TotalMilliseconds);
            _frameTimes.Enqueue(_begin);
            _timeToRender.Enqueue((_begin - _scheduled).TotalMilliseconds);

            if (_bmRender.Count > SamplesCount) _bmRender.Dequeue();
            if (_render.Count > SamplesCount) _render.Dequeue();
            if (_frameTimes.Count > SamplesCount) _frameTimes.Dequeue();
            if (_timeToRender.Count > SamplesCount) _timeToRender.Dequeue();


            if (_frameTimes.Count < MinSamplesCount) return;

            //calculate stats
            if ((_frames % 10 == 0) ||
                (_end - _lastStat).TotalMilliseconds > 1000
                )
            {
                _avgRenderTimems = _render.Average();
                _avgbmRenderTimems = _bmRender.Average();
                _avgTimeToRenderms = _timeToRender.Average();

                _fps = _frameTimes.Count / (_end - _frameTimes.First()).TotalSeconds;
                _lastStat = _end;
            }
        }

        public void BeginRenderBitmap()
        {
            _beginBM = DateTime.Now;
        }

        public void EndRenderBitmap()
        {
            _endBM = DateTime.Now;
        }

        public string RenderingEngine { get; set; }

        public override string ToString()
        {
            string fmt(double v) => v.ToString("0.00");
            return
                 $"RenderStats (last {SamplesCount} frames): {RenderingEngine}\n"+
                 $"Current FPS: {fmt(_fps)} InvalidateToRender (ms): {fmt(_avgTimeToRenderms)}\n" +
                 $"Render: Max FPS {fmt(1000.0 / _avgRenderTimems)}, ms {fmt(_avgRenderTimems)}\n" +
                 $"BitmapRender: Max FPS {fmt(1000.0 / _avgbmRenderTimems)}, ms {fmt(_avgbmRenderTimems)}";
        }
    }
}