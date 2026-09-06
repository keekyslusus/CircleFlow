# Visual search browser providers

The shared browser accepts one of two prepared request shapes. A URL provider uploads or otherwise prepares its result in `IVisualSearchProvider.PrepareAsync`, validates the resulting absolute URI with its own policy, and returns `PreparedVisualSearch.ForUrl`. It supplies an external fallback URI only when that exact URI is safe and usable outside the WebView2 profile.

A browser-operation provider returns `PreparedVisualSearch.ForBrowserOperation`. Its one-shot `IVisualSearchBrowserOperation` uses only `IVisualSearchBrowserSession` navigation, script, and message methods, validates its own final result location, and releases image data after execution.

Register either provider with its descriptor and factory in `CompositionRoot`. The shared host and presenter do not require provider-specific branches.
