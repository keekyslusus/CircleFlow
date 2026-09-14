using System.Windows.Media;
using CircleToSearch.Ui;
using static CircleToSearch.Ui.OverlayScrollbarPolicy;

namespace CircleToSearch.Search.Browser;

internal static class OverlayScrollbarScript
{
    internal static string Create()
    {
        var lightThumb = ToCssColor(PluginPalette.For(lightTheme: true).SearchBrowserScrollbarThumb);
        var darkThumb = ToCssColor(PluginPalette.For(lightTheme: false).SearchBrowserScrollbarThumb);

        return $$"""
            (() => {
                const installationKey = '__circleFlowOverlayScrollbar_v1__';
                if (window[installationKey]) return;
                window[installationKey] = true;

                const install = () => {
                    const nativeStyle = document.createElement('style');
                    nativeStyle.dataset.circleFlowScrollbar = 'native';
                    nativeStyle.textContent = `
                        :root, body {
                            scrollbar-width: none !important;
                            scrollbar-gutter: auto !important;
                        }
                        :root::-webkit-scrollbar, body::-webkit-scrollbar {
                            display: none !important;
                            width: 0 !important;
                            height: 0 !important;
                        }
                    `;
                    (document.head || document.documentElement).append(nativeStyle);

                    const host = document.createElement('div');
                    host.dataset.circleFlowScrollbar = 'overlay';
                    const setHostStyle = (name, value) => host.style.setProperty(name, value, 'important');
                    setHostStyle('all', 'initial');
                    setHostStyle('position', 'fixed');
                    setHostStyle('top', '{{EdgeInsetPixels}}px');
                    setHostStyle('right', '0');
                    setHostStyle('bottom', '{{EdgeInsetPixels}}px');
                    setHostStyle('width', '{{TrackWidthPixels}}px');
                    setHostStyle('z-index', '2147483647');
                    setHostStyle('opacity', '0');
                    setHostStyle('pointer-events', 'none');
                    setHostStyle('transition-property', 'opacity');
                    setHostStyle('transition-duration', '{{FadeOutMilliseconds}}ms');
                    setHostStyle('transition-timing-function', 'ease-out');
                    setHostStyle('contain', 'strict');

                    const shadow = host.attachShadow({ mode: 'open' });
                    const thumb = document.createElement('div');
                    thumb.setAttribute('part', 'thumb');
                    const setThumbStyle = (name, value) => thumb.style.setProperty(name, value, 'important');
                    setThumbStyle('position', 'absolute');
                    setThumbStyle('top', '0');
                    setThumbStyle('right', '{{EdgeInsetPixels}}px');
                    setThumbStyle('width', '{{ThumbWidthPixels}}px');
                    setThumbStyle('min-height', '{{MinimumThumbHeightPixels}}px');
                    setThumbStyle('border-radius', '999px');
                    setThumbStyle('cursor', 'default');
                    setThumbStyle('touch-action', 'none');
                    shadow.append(thumb);
                    document.documentElement.append(host);

                    const colorScheme = window.matchMedia('(prefers-color-scheme: dark)');
                    const applyColor = () => setThumbStyle(
                        'background-color',
                        colorScheme.matches ? '{{darkThumb}}' : '{{lightThumb}}');
                    applyColor();
                    colorScheme.addEventListener?.('change', applyColor);

                    const hideDelay = {{HideDelayMilliseconds}};
                    let hideTimer;
                    let dragging = false;
                    let dragOffset = 0;

                    const scroller = () => document.scrollingElement || document.documentElement;
                    const metrics = () => {
                        const target = scroller();
                        const viewportHeight = window.innerHeight;
                        const scrollRange = Math.max(0, target.scrollHeight - viewportHeight);
                        const trackHeight = Math.max(0, viewportHeight - {{EdgeInsetPixels * 2}});
                        const thumbHeight = Math.min(
                            trackHeight,
                            Math.max({{MinimumThumbHeightPixels}}, trackHeight * viewportHeight / Math.max(target.scrollHeight, 1)));
                        const thumbRange = Math.max(0, trackHeight - thumbHeight);
                        return { target, scrollRange, trackHeight, thumbHeight, thumbRange };
                    };

                    const update = () => {
                        const current = metrics();
                        if (current.scrollRange <= 1 || current.trackHeight <= 0) {
                            setHostStyle('display', 'none');
                            return current;
                        }

                        setHostStyle('display', 'block');
                        const top = current.thumbRange === 0
                            ? 0
                            : current.target.scrollTop / current.scrollRange * current.thumbRange;
                        setThumbStyle('height', `${current.thumbHeight}px`);
                        setThumbStyle('transform', `translateY(${Math.max(0, Math.min(current.thumbRange, top))}px)`);
                        return current;
                    };

                    const hide = () => {
                        if (dragging) return;
                        setHostStyle('transition-duration', '{{FadeOutMilliseconds}}ms');
                        setHostStyle('opacity', '0');
                        setHostStyle('pointer-events', 'none');
                    };

                    const scheduleHide = () => {
                        clearTimeout(hideTimer);
                        hideTimer = setTimeout(hide, hideDelay);
                    };

                    const reveal = () => {
                        const wasNotDisplayed = getComputedStyle(host).display === 'none';
                        const current = update();
                        if (current.scrollRange <= 1) return;
                        setHostStyle('transition-duration', '{{FadeInMilliseconds}}ms');
                        if (wasNotDisplayed) host.getBoundingClientRect();
                        setHostStyle('opacity', '1');
                        setHostStyle('pointer-events', 'auto');
                        scheduleHide();
                    };

                    const scrollFromPointer = clientY => {
                        const current = metrics();
                        if (current.scrollRange <= 1 || current.thumbRange <= 0) return;
                        const trackTop = host.getBoundingClientRect().top;
                        const thumbTop = Math.max(
                            0,
                            Math.min(current.thumbRange, clientY - trackTop - dragOffset));
                        current.target.scrollTop = thumbTop / current.thumbRange * current.scrollRange;
                    };

                    host.addEventListener('pointerdown', event => {
                        const thumbBounds = thumb.getBoundingClientRect();
                        dragOffset = event.composedPath().includes(thumb)
                            ? event.clientY - thumbBounds.top
                            : thumbBounds.height / 2;
                        dragging = true;
                        clearTimeout(hideTimer);
                        host.setPointerCapture(event.pointerId);
                        scrollFromPointer(event.clientY);
                        event.preventDefault();
                    });
                    host.addEventListener('pointermove', event => {
                        if (dragging) scrollFromPointer(event.clientY);
                    });
                    const stopDragging = event => {
                        if (!dragging) return;
                        dragging = false;
                        if (host.hasPointerCapture(event.pointerId)) host.releasePointerCapture(event.pointerId);
                        scheduleHide();
                    };
                    host.addEventListener('pointerup', stopDragging);
                    host.addEventListener('pointercancel', stopDragging);

                    window.addEventListener('scroll', reveal, { capture: true, passive: true });
                    window.addEventListener('resize', update, { passive: true });
                    const resizeObserver = new ResizeObserver(update);
                    resizeObserver.observe(document.documentElement);
                    if (document.body) resizeObserver.observe(document.body);
                    update();
                };

                if (document.readyState === 'loading')
                    document.addEventListener('DOMContentLoaded', install, { once: true });
                else
                    install();
            })();
            """;
    }

    private static string ToCssColor(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
}
