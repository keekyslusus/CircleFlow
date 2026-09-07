using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

using Xunit;

namespace CircleToSearch.Tests;

public sealed class WinRtRuntimeBindingTests
{
    [Fact]
    public void Private_runtime_reproduces_the_logged_tracker_registration_failure()
    {
        _ = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
        var context = new FlowLikeContext(AppContext.BaseDirectory);
        try
        {
            var consumer = context.LoadFromAssemblyPath(typeof(WebView2CompositionControl).Assembly.Location);
            var reference = Assert.Single(consumer.GetReferencedAssemblies(), name => name.Name == "WinRT.Runtime");
            var duplicate = context.LoadFromAssemblyName(reference);
            Assert.NotSame(typeof(WinRT.ComWrappersSupport).Assembly, duplicate);
            var wrappers = duplicate.GetType("WinRT.ComWrappersSupport", throwOnError: true)!
                .GetProperty("ComWrappers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            var failure = Assert.Throws<TargetInvocationException>(() => wrappers.GetValue(null));
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Contains("Attempt to update previously set global instance", failure.InnerException!.Message);
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void Webview_uses_hosts_runtime_with_Flow_style_exact_identity_resolution()
    {
        _ = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
        var runtime = typeof(WinRT.ComWrappersSupport).Assembly;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var pluginDirectory = Path.Combine(TestOutputPaths.RepoDirectory, "bin", configuration);
        Assert.True(File.Exists(Path.Combine(pluginDirectory, "CircleToSearch.dll")));
        Assert.False(File.Exists(Path.Combine(pluginDirectory, "WinRT.Runtime.dll")));
        var context = new FlowLikeContext(pluginDirectory);
        try
        {
            var consumer = context.LoadFromAssemblyPath(typeof(WebView2CompositionControl).Assembly.Location);
            var reference = Assert.Single(consumer.GetReferencedAssemblies(), name => name.Name == "WinRT.Runtime");
            var resolved = context.LoadFromAssemblyName(reference);
            Assert.Same(runtime, resolved);
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                Window? window = null;
                IDisposable? disposable = null;
                try
                {
                    var view = (FrameworkElement)Activator.CreateInstance(consumer.GetType(typeof(WebView2CompositionControl).FullName!)!)!;
                    disposable = (IDisposable)view;
                    window = new Window { Content = view, Width = 200, Height = 120, ShowActivated = false, ShowInTaskbar = false };
                    window.Show();
                    var frame = new DispatcherFrame();
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                    timer.Start();
                    Dispatcher.PushFrame(frame);
                    Assert.True(view.IsLoaded);
                }
                catch (Exception exception) { failure = exception; }
                finally { disposable?.Dispose(); window?.Close(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(failure);
        }
        finally { context.Unload(); }
    }

    private sealed class FlowLikeContext(string pluginDirectory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            var shared = Default.Assemblies.FirstOrDefault(assembly => assembly.FullName == name.FullName);
            if (shared is not null) return shared;
            var path = Path.Combine(pluginDirectory, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
