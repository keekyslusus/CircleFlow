using System.Runtime.Loader;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class WinRtRuntimeBindingTests
{
    [Fact]
    public void Ocr_and_webview_controls_share_the_default_context_runtime()
    {
        _ = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
        var runtime = typeof(WinRT.ComWrappersSupport).Assembly;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var applicationDirectory = Path.Combine(TestOutputPaths.RepoDirectory, "bin", configuration);
        Assert.True(File.Exists(Path.Combine(applicationDirectory, "CircleFlow.exe")));
        Assert.True(File.Exists(Path.Combine(applicationDirectory, "CircleFlow.dll")));
        Assert.True(File.Exists(Path.Combine(applicationDirectory, "WinRT.Runtime.dll")));
        Assert.Equal("CircleFlow", typeof(AppRuntime).Assembly.GetName().Name);
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(runtime));

        var reference = Assert.Single(typeof(WebView2CompositionControl).Assembly.GetReferencedAssemblies(),
            name => name.Name == "WinRT.Runtime");
        Assert.Same(runtime, AssemblyLoadContext.Default.LoadFromAssemblyName(reference));

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                using var browser = new WebView2();
                using var composition = new WebView2CompositionControl();
                var panel = new System.Windows.Controls.StackPanel();
                panel.Children.Add(browser);
                panel.Children.Add(composition);
                window = new Window
                {
                    Content = panel, Width = 200, Height = 120,
                    ShowActivated = false, ShowInTaskbar = false,
                };
                window.Show();
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
                Assert.True(browser.IsLoaded);
                Assert.True(composition.IsLoaded);
                Assert.Same(runtime, Assert.Single(AssemblyLoadContext.Default.Assemblies,
                    assembly => assembly.GetName().Name == "WinRT.Runtime"));
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
