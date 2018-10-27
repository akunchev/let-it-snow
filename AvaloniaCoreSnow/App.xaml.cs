using System.Configuration;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Diagnostics;
using Avalonia.Markup.Xaml;

namespace AvaloniaCoreSnow
{
    internal class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            base.Initialize();
        }

        private static void Main(string[] args)
        {
            AppBuilder.Configure<App>()
                .UsePlatformDetectCustom()
                .UseReactiveUI()
                .Start<MainWindow>();
        }

        public static void AttachDevTools(Window window)
        {
#if DEBUG
            DevTools.Attach(window);
#endif
        }
    }

    internal static class Ext
    {
        public static AppBuilder UsePlatformDetectCustom(this AppBuilder builder)
        {
            string defSett = ConfigurationManager.AppSettings["DeferedRendering"];

            bool useDeferedRendering;

            if (!bool.TryParse(defSett, out useDeferedRendering))
                useDeferedRendering = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                builder.UseWin32(useDeferedRendering);

                bool forceskia = ConfigurationManager.AppSettings["forceskia"] == "true";

                if (forceskia) builder.UseSkia();
                else builder.UseDirect2D1();

                return builder;
            }
            else
            {
                return builder
                    .UseAvaloniaNative(configure: c =>
                    {
                        c.UseDeferredRendering = useDeferedRendering;
                        //check cpu against cpu e.g. hardware accelerated may be ???
                        //c.UseGpu = true;
                    })
                    .UseSkia();
            }
        }
    }
}