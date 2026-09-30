using System.Drawing;
using System.Threading.Channels;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using CircleToSearch.Shell;
using CircleToSearch.Settings;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlaySessionWorkflowTests
{
    [Fact]
    public async Task Save_waits_for_overlay_close_before_showing_dialog_and_does_not_reopen_overlay()
    {
        var saved = new List<BitmapSource>();
        using var harness = new Harness(saveImage: image => { saved.Add(image); return Task.CompletedTask; });
        var image = TranslationImage();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.CloseAsyncCompletion = closed.Task;
        harness.Overlay.Enqueue(new SaveSelectedImage(image));
        var run = harness.RunAsync();
        try
        {
            await harness.Overlay.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(saved);
            Assert.False(run.IsCompleted);
        }
        finally { closed.TrySetResult(); }
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(image, Assert.Single(saved));
        Assert.Equal(1, harness.Overlay.DisposeCalls);
        Assert.Empty(harness.Opened);
        Assert.Equal(0, harness.Google.Calls);
    }

    [Fact]
    public async Task Ask_closes_overlay_and_sends_question_to_google_regardless_of_selected_provider()
    {
        using var harness = new Harness(providerId: SearchProviderIds.YandexImages);
        var selection = NewSelection();
        harness.Overlay.Enqueue(new AskAboutSelection(selection, "What is this?"));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("What is this?", await Assert.Single(harness.AskedQuestions));
        Assert.Equal([7], await Assert.Single(harness.AskedImages));
        Assert.Equal(1, Assert.Single(harness.CloseCallsWhenRevealed));
        Assert.Equal(1, harness.UploadStartedCalls);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
        Assert.Throws<ObjectDisposedException>(() => selection.FrozenFrame);
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task Ask_draft_warms_one_browser_and_uses_the_image_attached_while_typing()
    {
        using var harness = new Harness();
        var typed = NewSelection();
        var submitted = NewSelection();
        harness.Overlay.Enqueue(new AskDraftStarted());
        harness.Overlay.Enqueue(new AskImageAttached(typed));

        var run = harness.RunAsync();
        await WaitUntilAsync(() => harness.AskCrops == 1);
        var question = Assert.Single(harness.AskedQuestions);
        Assert.Equal([7], await Assert.Single(harness.AskedImages));
        Assert.False(question.IsCompleted);
        Assert.Equal(0, harness.Overlay.CloseCalls);
        Assert.Empty(harness.CloseCallsWhenRevealed);

        harness.Overlay.Enqueue(new AskAboutSelection(submitted, "What is this?"));
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("What is this?", await question);
        Assert.Equal(1, harness.AskCrops);
        Assert.Equal(1, Assert.Single(harness.CloseCallsWhenRevealed));
        Assert.Throws<ObjectDisposedException>(() => typed.FrozenFrame);
        Assert.Throws<ObjectDisposedException>(() => submitted.FrozenFrame);
    }

    [Fact]
    public async Task Question_without_a_selection_uses_the_image_attached_while_typing()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new AskDraftStarted());
        harness.Overlay.Enqueue(new AskImageAttached(NewSelection()));
        harness.Overlay.Enqueue(new AskAboutSelection(null, "What is this?"));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([7], await Assert.Single(harness.AskedImages));
        Assert.Equal("What is this?", await Assert.Single(harness.AskedQuestions));
        Assert.Equal(1, harness.AskCrops);
    }

    [Fact]
    public async Task Question_without_any_image_fails_the_draft_instead_of_waiting_forever()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new AskAboutSelection(null, "What is this?"));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        var image = Assert.Single(harness.AskedImages);
        await Assert.ThrowsAsync<InvalidOperationException>(() => image);
    }

    [Fact]
    public async Task Canceled_ask_draft_closes_the_hidden_browser_and_keeps_the_overlay()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new AskDraftStarted());
        harness.Overlay.Enqueue(new AskDraftCanceled());

        var run = harness.RunAsync();
        await WaitUntilAsync(() => harness.AskHost.CanceledCalls == 1);
        Assert.False(run.IsCompleted);
        Assert.Equal(0, harness.Overlay.CloseCalls);
        harness.Overlay.Enqueue(new AskDraftStarted());
        harness.Overlay.Enqueue(new CancelSession());
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, harness.AskedQuestions.Count);
        Assert.All(harness.AskedQuestions, question => Assert.False(question.IsCompleted));
        Assert.Equal(2, harness.AskHost.CanceledCalls);
        Assert.Equal(0, harness.AskCrops);
        Assert.Equal(0, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Lens_lasso_warms_hidden_uploads_during_close_and_reveals_after_it()
    {
        using var harness = new Harness();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.CloseCompletion = closed.Task;
        var selection = NewSelection();
        harness.Overlay.Enqueue(new VisualSelectionStarted(SearchProviderIds.GoogleLens));

        var run = harness.RunAsync();
        await WaitUntilAsync(() => harness.LensImages.Count == 1);
        var image = harness.LensImages[0];
        Assert.False(image.IsCompleted);

        harness.Overlay.Enqueue(new VisualSelection(selection, SearchProviderIds.GoogleLens));
        try
        {
            Assert.Equal([9], await image.WaitAsync(TimeSpan.FromSeconds(2)));
            await Task.Delay(50);
            Assert.False(run.IsCompleted);
            Assert.Equal(0, harness.UploadStartedCalls);
            Assert.Empty(harness.LensRevealedAfterClose);
        }
        finally { closed.TrySetResult(); }
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.UploadStartedCalls);
        Assert.Equal([true], harness.LensRevealedAfterClose);
        Assert.Equal(1, harness.LensCrops);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
    }

    [Fact]
    public async Task Repeated_lasso_starts_keep_one_warm_lens_that_closes_with_the_session()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new VisualSelectionStarted(SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new VisualSelectionStarted(SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        _ = Assert.Single(harness.LensImages);
        Assert.Equal(1, harness.LensHost.CanceledCalls);
        Assert.Equal(0, harness.LensCrops);
        Assert.Equal(0, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Lasso_with_another_provider_does_not_warm_lens()
    {
        using var harness = new Harness(providerId: SearchProviderIds.YandexImages);
        harness.Overlay.Enqueue(new VisualSelectionStarted(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.YandexImages));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(harness.LensImages);
        Assert.Equal(1, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Switching_away_from_lens_releases_the_warm_browser()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new VisualSelectionStarted(SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.YandexImages));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.LensHost.CanceledCalls);
        Assert.Equal(0, harness.LensCrops);
        Assert.Equal(1, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Music_recognition_releases_a_warm_lens()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new VisualSelectionStarted(SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new StartMusicRecognition());

        var run = harness.RunAsync();
        await WaitUntilAsync(() => harness.Music.Calls == 1);
        Assert.Equal(1, harness.LensHost.CanceledCalls);
        harness.Overlay.Enqueue(new CancelSession());
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Lasso_during_an_ask_draft_does_not_warm_a_second_browser()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new AskDraftStarted());
        harness.Overlay.Enqueue(new VisualSelectionStarted(SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        _ = Assert.Single(harness.AskedQuestions);
        Assert.Empty(harness.LensImages);
    }

    [Fact]
    public async Task Visual_search_waits_until_selection_overlay_is_gone()
    {
        using var harness = new Harness();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.CloseCompletion = closed.Task;
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.GoogleLens));

        var run = harness.RunAsync();
        try
        {
            Assert.False(run.IsCompleted);
            Assert.Equal(0, harness.Google.Calls);
            Assert.Equal(0, harness.UploadStartedCalls);
            Assert.Equal(0, harness.Overlay.CloseCalls);
        }
        finally { closed.TrySetResult(); }
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.Google.Calls);
        Assert.Equal(1, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Canceling_during_selection_hold_disposes_frame_without_opening_results()
    {
        using var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        var selection = NewSelection();
        harness.Overlay.CloseCompletion = new TaskCompletionSource().Task;
        harness.Overlay.Enqueue(new VisualSelection(selection, SearchProviderIds.GoogleLens));

        var run = harness.RunAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
    }

    [Fact]
    public async Task Text_search_closes_overlay_before_opening_browser()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new SearchSelectedText("a&b", SearchProviderIds.GoogleLens));

        await harness.RunAsync();

        Assert.Equal(["close", "text-open", "close"], harness.Events);
        Assert.Contains("q=a%26b", Assert.Single(harness.Opened));
        Assert.Equal(0, harness.TextHost.Shows);
        Assert.Equal(0, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Text_search_can_open_in_the_built_in_browser_after_the_overlay_closes()
    {
        using var harness = new Harness();
        harness.Service.SetTextSearchInBuiltInBrowser(true).ThrowIfFailed("test update failed");
        harness.Service.SetTextSearchEngine("duckduckgo").ThrowIfFailed("test update failed");
        harness.Overlay.Enqueue(new SearchSelectedText("a&b", SearchProviderIds.GoogleLens));

        await harness.RunAsync();

        Assert.Equal(["duckduckgo.com"], harness.TextHost.Names);
        Assert.Equal([new Uri("https://duckduckgo.com/?q=a%26b")], harness.TextHost.Navigated);
        Assert.Empty(harness.Opened);
        Assert.Equal(1, harness.UploadStartedCalls);
        Assert.Equal("close", harness.Events[0]);
    }

    [Fact]
    public async Task Selecting_text_warms_the_built_in_browser_that_the_search_then_reveals()
    {
        using var harness = new Harness();
        harness.Service.SetTextSearchInBuiltInBrowser(true).ThrowIfFailed("test update failed");
        harness.Overlay.Enqueue(new TextSelectionStarted(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new TextSelectionStarted(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new SearchSelectedText("cat", SearchProviderIds.YandexImages));

        await harness.RunAsync();

        Assert.Equal(["yandex.com"], harness.TextHost.Names);
        Assert.Equal([new Uri("https://yandex.com/search/?text=cat")], harness.TextHost.Navigated);
        Assert.Contains("\"https://yandex.com/\"", Assert.Single(harness.TextHost.Scripts));
        Assert.Equal(["show", "revealed"], harness.TextHost.Events);
        Assert.Equal("close", harness.Events[0]);
        Assert.Equal(1, harness.UploadStartedCalls);
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task Text_selection_warms_nothing_while_results_open_in_the_default_browser()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new TextSelectionStarted(SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync();

        Assert.Equal(0, harness.TextHost.Shows);
    }

    [Fact]
    public async Task Warm_text_search_is_released_when_the_session_ends_or_the_provider_changes()
    {
        using var harness = new Harness();
        harness.Service.SetTextSearchInBuiltInBrowser(true).ThrowIfFailed("test update failed");
        harness.Overlay.Enqueue(new TextSelectionStarted(SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new TextSelectionStarted(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["google.com", "yandex.com"], harness.TextHost.Names);
        Assert.Empty(harness.TextHost.Navigated);
        Assert.DoesNotContain("revealed", harness.TextHost.Events);
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task Screen_translation_result_is_returned_to_matching_overlay_request()
    {
        using var harness = new Harness();
        harness.Overlay.CloseAfterTranslation = true;
        var image = TranslationImage();
        var requestId = Guid.NewGuid();
        harness.Overlay.Enqueue(new ScreenTranslationRequested(requestId, image, "es"));

        await harness.RunAsync();

        Assert.Equal(requestId, harness.Overlay.TranslationResult?.RequestId);
        Assert.Same(image, harness.Overlay.TranslationResult!.Image);
    }

    [Fact]
    public async Task Screen_translation_failure_is_returned_to_the_matching_request()
    {
        var provider = new FailingTranslationProvider();
        using var harness = new Harness(translationProvider: provider);
        harness.Overlay.CloseAfterTranslation = true;
        var requestId = Guid.NewGuid();
        harness.Overlay.Enqueue(new ScreenTranslationRequested(requestId, TranslationImage(), "es"));

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.TranslationFailureCalls);
        Assert.Equal((requestId, TranslationFailure.RateLimited), harness.Overlay.TranslationFailure);
    }

    [Fact]
    public async Task Translation_cancellation_matches_request_and_allows_the_next_request()
    {
        var provider = new FirstCancellationThenSuccessTranslationProvider();
        using var harness = new Harness(translationProvider: provider);
        harness.Overlay.CloseAfterTranslation = true;
        var first = Guid.NewGuid();
        var ignored = Guid.NewGuid();
        var next = Guid.NewGuid();
        harness.Overlay.Enqueue(new ScreenTranslationRequested(first, TranslationImage(), "es"));
        harness.Overlay.Enqueue(new CancelScreenTranslation(ignored));
        harness.Overlay.Enqueue(new ScreenTranslationRequested(ignored, TranslationImage(), "fr"));
        harness.Overlay.Enqueue(new CancelScreenTranslation(first));
        harness.Overlay.Enqueue(new ScreenTranslationRequested(next, TranslationImage(), "de"));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, provider.CanceledCalls);
        Assert.Equal(next, harness.Overlay.TranslationResult?.RequestId);
        Assert.Equal(0, harness.Overlay.TranslationFailureCalls);
    }

    [Fact]
    public async Task Provider_change_applies_to_current_visual_command_and_persists_once()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.YandexImages));

        await harness.RunAsync();

        Assert.Equal(SearchProviderIds.YandexImages, harness.Settings.SearchProviderId);
        Assert.Equal(1, harness.SaveCalls);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(1, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Persistence_failure_keeps_saved_provider_and_allows_visual_search_with_the_current_UI_selection()
    {
        using var harness = new Harness(saveThrows: true);
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.YandexImages));

        await harness.RunAsync();

        Assert.Equal(SearchProviderIds.GoogleLens, harness.Settings.SearchProviderId);
        Assert.Equal(1, harness.Yandex.Calls);
        Assert.Single(harness.Errors);
    }

    [Fact]
    public async Task Provider_change_during_listening_does_not_cancel_recognition()
    {
        using var harness = new Harness();
        harness.Music.Gate = new TaskCompletionSource<MusicRecognitionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync();

        Assert.Equal(SearchProviderIds.YandexImages, harness.Settings.SearchProviderId);
        Assert.Equal(1, harness.SaveCalls);
        Assert.Equal(1, harness.Music.Calls);
        Assert.Equal(0, harness.Simulator.Calls);
        Assert.Equal(1, harness.Music.CanceledCalls);
    }

    [Fact]
    public async Task Debug_scenario_uses_simulator_without_calling_live_recognizer()
    {
        using var harness = new Harness();
        harness.Overlay.CloseAfterResult = true;
        harness.Overlay.Enqueue(new MusicDebugScenarioSelected(MusicDebugScenario.Matched));
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(0, harness.Music.Calls);
        Assert.Equal(1, harness.Simulator.Calls);
        Assert.Equal(MusicDebugScenario.Matched, harness.Simulator.LastScenario);
        Assert.Equal(MusicRecognitionStatus.Matched, harness.Overlay.Result?.Status);
    }

    [Fact]
    public async Task Unknown_saved_provider_opens_with_router_default_without_instantiating_providers()
    {
        using var harness = new Harness("missing");
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync();

        Assert.Equal(SearchProviderIds.GoogleLens, harness.Factory.Options!.InitialProviderId);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Coordinator_opens_a_fresh_overlay_after_the_previous_session_finishes()
    {
        var first = new FakeOverlay();
        var second = new FakeOverlay();
        first.Enqueue(new CancelSession());
        second.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.GoogleLens));
        using var harness = new Harness(overlays: [first, second]);
        var coordinator = harness.CreateCoordinator();

        await coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(1, harness.Factory.Calls);
        Assert.Same(first, Assert.Single(harness.Factory.Opened));

        await coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(2, harness.Factory.Calls);
        Assert.Equal([first, second], harness.Factory.Opened);
        Assert.Equal(1, first.CloseCalls);
        Assert.Equal(1, second.CloseCalls);
        Assert.Equal(1, harness.Google.Calls);
    }

    [Fact]
    public async Task Visual_selection_reports_upload_started_exactly_once()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.GoogleLens));

        await harness.RunAsync();

        Assert.Equal(1, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Music_recognition_shows_listening_and_result_without_reporting_upload()
    {
        using var harness = new Harness();
        harness.Overlay.CloseAfterResult = true;
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.ListeningCalls);
        Assert.Equal(1, harness.Overlay.ResultCalls);
        Assert.Equal(0, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Music_start_and_retry_are_ignored_while_recognition_is_active()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());
        harness.Overlay.Enqueue(new RetryMusicRecognition());
        harness.Overlay.Enqueue(new StartMusicRecognition());
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.Music.Calls);
        Assert.Equal(1, harness.Overlay.ListeningCalls);
        Assert.Equal(1, harness.Music.CanceledCalls);
    }

    [Fact]
    public async Task Ignored_visual_selection_during_music_disposes_its_image()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var selection = NewSelection();
        harness.Overlay.Enqueue(new StartMusicRecognition());
        harness.Overlay.Enqueue(new VisualSelection(selection, SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
        Assert.Equal(0, harness.Google.Calls);
    }

    [Fact]
    public async Task Ready_command_wins_over_ready_music_result()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new StartMusicRecognition());
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.Music.Calls);
        Assert.Equal(0, harness.Overlay.ResultCalls);
    }

    [Fact]
    public async Task Dismissing_music_result_returns_to_selection_in_the_same_overlay()
    {
        using var harness = new Harness();
        harness.Overlay.CommandsAfterResult.Add(new DismissMusicResult());
        harness.Overlay.CommandsAfterResult.Add(
            new VisualSelection(NewSelection(), SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(MusicRecognitionStatus.NoMatch, harness.Overlay.Result?.Status);
        Assert.Equal(1, harness.Overlay.ListeningCalls);
        Assert.Equal(1, harness.Overlay.ResultCalls);
        Assert.Equal(1, harness.Google.Calls);
        Assert.Equal(1, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Closed_command_channel_completes_and_closes_overlay()
    {
        using var harness = new Harness();
        harness.Overlay.CompleteCommands();

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.CloseCalls);
    }

    [Fact]
    public async Task Session_cancellation_cancels_recognition_and_closes_overlay()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());
        using var cancellation = new CancellationTokenSource();

        var workflow = harness.RunAsync(cancellation.Token);
        Assert.True(SpinWait.SpinUntil(() => harness.Music.Calls == 1, TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow);

        Assert.Equal(1, harness.Music.CanceledCalls);
        Assert.Equal(1, harness.Overlay.CloseCalls);
    }

    [Fact]
    public async Task Coordinator_hotkey_during_recognition_cancels_the_session()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());
        var coordinator = harness.CreateCoordinator();

        var session = coordinator.StartFromHotkeyAsync();
        Assert.True(SpinWait.SpinUntil(() => harness.Music.Calls == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(SearchState.Cancelable, coordinator.State);

        await coordinator.StartFromHotkeyAsync();
        await session;

        Assert.Equal(1, harness.Music.CanceledCalls);
        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(SearchState.Idle, coordinator.State);
    }

    [Fact]
    public async Task Coordinator_hotkey_while_music_result_is_shown_cancels_the_session()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new StartMusicRecognition());
        var coordinator = harness.CreateCoordinator();

        var session = coordinator.StartFromHotkeyAsync();
        Assert.True(SpinWait.SpinUntil(() => harness.Overlay.ResultCalls == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(SearchState.Cancelable, coordinator.State);

        await coordinator.StartFromHotkeyAsync();
        await session;

        Assert.Equal(1, harness.Overlay.ListeningCalls);
        Assert.Equal(1, harness.Overlay.ResultCalls);
        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(SearchState.Idle, coordinator.State);
    }

    [Fact]
    public async Task Retry_starts_a_new_recognition_after_the_previous_result()
    {
        using var harness = new Harness();
        harness.Overlay.OnResult = count =>
            harness.Overlay.Enqueue(count == 1 ? new RetryMusicRecognition() : new CancelSession());
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(2, harness.Music.Calls);
        Assert.Equal(2, harness.Overlay.ListeningCalls);
        Assert.Equal(2, harness.Overlay.ResultCalls);
        Assert.Equal(0, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Overlay_result_failure_uses_fallback_and_closes_workflow()
    {
        using var harness = new Harness();
        harness.Music.Outcome = MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch);
        harness.Overlay.ShowResultException = new InvalidOperationException("render failed");
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Single(harness.Messages);
        Assert.Equal(1, harness.Overlay.CloseCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/track/1")]
    public async Task Unsafe_or_missing_music_url_keeps_overlay_open(string? url)
    {
        using var harness = new Harness();
        harness.Music.Outcome = MusicRecognitionOutcome.Matched(Match(url));
        harness.Overlay.CommandsAfterResult.Add(new OpenMusicResult());
        harness.Overlay.CommandsAfterResult.Add(new CancelSession());
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task Safe_music_url_closes_overlay_before_opening()
    {
        using var harness = new Harness();
        harness.Music.Outcome = MusicRecognitionOutcome.Matched(Match("https://www.shazam.com/track/1"));
        harness.Overlay.CommandsAfterResult.Add(new OpenMusicResult());
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.True(harness.Events.IndexOf("close") < harness.Events.IndexOf("open"));
        Assert.Single(harness.Opened);
    }

    [Fact]
    public async Task Recognition_visualization_is_forwarded_to_overlay()
    {
        using var harness = new Harness();
        harness.Music.ReportFrame = true;
        harness.Overlay.CloseAfterResult = true;
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.AudioFrames);
    }

    [Fact]
    public async Task Synchronous_recognizer_failure_still_closes_and_disposes_overlay()
    {
        using var harness = new Harness();
        harness.Music.ThrowSynchronously = true;
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync());

        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(1, harness.Overlay.DisposeCalls);
    }

    [Fact]
    public async Task Faulted_recognition_is_observed_and_overlay_is_disposed()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());

        var run = harness.RunAsync();
        Assert.True(SpinWait.SpinUntil(() => harness.Music.Calls == 1, TimeSpan.FromSeconds(2)));
        harness.Music.Gate.SetException(new InvalidOperationException("recognition failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => run);

        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(1, harness.Overlay.DisposeCalls);
    }

    [Fact]
    public async Task Throwing_cancellation_callback_does_not_skip_cleanup()
    {
        var translation = new FirstCancellationThenSuccessTranslationProvider();
        using var harness = new Harness(translationProvider: translation);
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Music.ThrowFromCancellationCallback = true;
        harness.Trace.FirstGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());
        harness.Overlay.Enqueue(new ScreenTranslationRequested(Guid.NewGuid(), TranslationImage(), "es"));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.TraceMoe));
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.Music.CanceledCalls);
        Assert.Equal(1, translation.CanceledCalls);
        Assert.Equal(1, harness.Trace.CanceledCalls);
        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(1, harness.Overlay.DisposeCalls);
    }

    [Fact]
    public async Task Music_debug_scenario_does_not_leak_into_the_next_session()
    {
        var first = new FakeOverlay();
        var second = new FakeOverlay { CloseAfterResult = true };
        first.Enqueue(new MusicDebugScenarioSelected(MusicDebugScenario.Matched));
        first.Enqueue(new CancelSession());
        second.Enqueue(new StartMusicRecognition());
        using var harness = new Harness(overlays: [first, second]);

        await harness.RunAsync();
        await harness.RunAsync();

        Assert.Equal(1, harness.Music.Calls);
        Assert.Equal(0, harness.Simulator.Calls);
    }

    [Fact]
    public async Task Music_result_does_not_leak_into_the_next_session()
    {
        var first = new FakeOverlay { CloseAfterResult = true };
        var second = new FakeOverlay();
        first.Enqueue(new StartMusicRecognition());
        second.Enqueue(new OpenMusicResult());
        second.Enqueue(new CancelSession());
        using var harness = new Harness(overlays: [first, second]);
        harness.Music.Outcome = MusicRecognitionOutcome.Matched(Match("https://www.shazam.com/track/1"));

        await harness.RunAsync();
        await harness.RunAsync();

        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task Cleanup_closes_overlay_before_waiting_for_music_to_drain()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Music.CompleteGateOnCancellation = false;
        harness.Overlay.Enqueue(new StartMusicRecognition());
        harness.Overlay.Enqueue(new CancelSession());

        var run = harness.RunAsync();
        await harness.Music.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(0, harness.Overlay.DisposeCalls);
        Assert.False(run.IsCompleted);

        harness.Music.Gate.SetResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled));
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, harness.Overlay.DisposeCalls);
    }

    [Fact]
    public async Task Close_failure_does_not_skip_overlay_disposal()
    {
        using var harness = new Harness();
        harness.Overlay.CloseException = new InvalidOperationException("close failed");
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(1, harness.Overlay.DisposeCalls);
    }

    [Fact]
    public async Task Session_factory_failure_cleans_up_the_open_overlay()
    {
        using var harness = new Harness(traceFactoryThrows: true);
        var selection = NewSelection();
        harness.Overlay.Enqueue(new VisualSelection(selection, SearchProviderIds.GoogleLens));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync());

        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(1, harness.Overlay.DisposeCalls);
        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
    }

    private static SelectionOutcome NewSelection() =>
        new(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2));

    private static BitmapSource TranslationImage()
    {
        var image = BitmapSource.Create(8, 4, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 4 * 4], 8 * 4);
        image.Freeze();
        return image;
    }

    private static ShazamRecognition Match(string? url) =>
        new("Track", "Artist", null, null, null, null, url);

    [Fact]
    public async Task Trace_search_keeps_overlay_alive_and_returns_native_result()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe);
        harness.Overlay.CloseCompletion = new TaskCompletionSource().Task;
        var selection = NewSelection();
        harness.Overlay.Enqueue(new VisualSelection(selection, SearchProviderIds.TraceMoe));
        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(harness.Overlay.WidgetResult);
        Assert.Equal(PreparedVisualSearchKind.TraceMoe, harness.Overlay.WidgetResult!.PreparedSearch!.Kind);
        Assert.Equal(new[] { "widget", "close" }, harness.Events);
        Assert.Equal(0, harness.UploadStartedCalls);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
    }

    [Fact]
    public async Task Opening_trace_card_closes_overlay_before_opening_anilist()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe);
        harness.Trace.Match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory,
            "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")));
        harness.Overlay.CommandsAfterResult.Add(new OpenWidgetResult(new Uri(harness.Trace.Match!.AnilistUrl)));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.TraceMoe));
        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "widget", "close", "widget-open", "close" }, harness.Events);
        Assert.Equal(harness.Trace.Match!.AnilistUrl, Assert.Single(harness.Opened));
        Assert.Empty(harness.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trace_open_failure_notifies_once_after_close_and_ends_session(bool throws)
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.CloseAsyncCompletion = closed.Task;
        harness.TraceOpenResult = false;
        harness.TraceOpenThrows = throws;
        harness.Notifier.OnError = () => harness.Events.Add("error");
        harness.Trace.Match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory,
            "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")));
        harness.Overlay.CommandsAfterResult.Add(new OpenWidgetResult(new Uri(harness.Trace.Match!.AnilistUrl)));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.TraceMoe));

        var run = harness.RunAsync();
        try
        {
            await harness.Overlay.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(run.IsCompleted);
            Assert.Equal(0, harness.TraceOpenCalls);
            Assert.Empty(harness.Errors);
            Assert.Equal(new[] { "widget", "close" }, harness.Events);
        }
        finally { closed.TrySetResult(); }
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, harness.TraceOpenCalls);
        Assert.Equal(harness.Trace.Match!.AnilistUrl, Assert.Single(harness.Opened));
        Assert.Equal(TestUiStrings.English.ResultsUrlOpenFailed, Assert.Single(harness.Errors));
        Assert.Equal(new[] { "widget", "close", "widget-open", "error", "close" }, harness.Events);
        Assert.Equal(1, harness.Overlay.DisposeCalls);
    }

    [Fact]
    public async Task Trace_close_failure_does_not_open_url_or_report_open_failure()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe);
        harness.Trace.Match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory,
            "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")));
        harness.Overlay.CloseException = new InvalidOperationException("close failed");
        harness.Overlay.CommandsAfterResult.Add(new OpenWidgetResult(new Uri(harness.Trace.Match!.AnilistUrl)));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.TraceMoe));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync());

        Assert.Equal(0, harness.TraceOpenCalls);
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task Repeated_trace_selection_while_trace_is_active_uses_the_normal_visual_path()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe);
        harness.Trace.FirstGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = NewSelection();
        var second = NewSelection();
        harness.Overlay.Enqueue(new VisualSelection(first, SearchProviderIds.TraceMoe));
        harness.Overlay.Enqueue(new VisualSelection(second, SearchProviderIds.TraceMoe));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, harness.Trace.Calls);
        Assert.Equal(1, harness.UploadStartedCalls);
        Assert.Null(harness.Overlay.WidgetResult);
        Assert.Throws<ObjectDisposedException>(() => _ = first.FrozenFrame);
        Assert.Throws<ObjectDisposedException>(() => _ = second.FrozenFrame);
    }

    [Fact]
    public async Task Canceled_trace_result_is_not_shown()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe);
        harness.Trace.Outcome = VisualSearchPreparationOutcome.Fail(UploadFailure.Canceled);
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.TraceMoe));
        using var cancellation = new CancellationTokenSource();

        var run = harness.RunAsync(cancellation.Token);
        Assert.Null(harness.Overlay.WidgetResult);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Null(harness.Overlay.WidgetResult);
    }

    [Fact]
    public async Task Opening_trace_without_a_match_keeps_the_overlay_open()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe);
        harness.Overlay.CommandsAfterResult.Add(new OpenWidgetResult(new Uri("https://anilist.co/anime/1")));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.TraceMoe));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(harness.Opened);
        Assert.Empty(harness.Errors);
        Assert.Equal(new[] { "widget", "close" }, harness.Events);
    }

    [Fact]
    public async Task Pinterest_search_shows_pins_in_overlay_and_opens_the_chosen_pin()
    {
        using var harness = new Harness(SearchProviderIds.Pinterest);
        harness.Pinterest.Pins = [Pin("11"), Pin("22")];
        harness.Overlay.CommandsAfterResult.Add(new OpenWidgetResult(new Uri(harness.Pinterest.Pins[1].PinUrl)));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.Pinterest));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(PreparedVisualSearchKind.Pinterest, harness.Overlay.WidgetResult!.PreparedSearch!.Kind);
        Assert.Equal(1, harness.Pinterest.Calls);
        Assert.Equal(0, harness.UploadStartedCalls);
        Assert.Equal("https://www.pinterest.com/pin/22/", Assert.Single(harness.Opened));
        Assert.Equal(new[] { "widget", "close", "widget-open", "close" }, harness.Events);
    }

    [Fact]
    public async Task Opening_a_url_the_widget_did_not_offer_keeps_the_overlay_open()
    {
        using var harness = new Harness(SearchProviderIds.Pinterest);
        harness.Pinterest.Pins = [Pin("11")];
        harness.Overlay.CommandsAfterResult.Add(new OpenWidgetResult(new Uri("https://example.com/pin/11/")));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.Pinterest));

        await harness.RunAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(harness.Opened);
        Assert.Equal(new[] { "widget", "close" }, harness.Events);
    }

    private static PinterestPin Pin(string id) =>
        new(id, "", "", null, new Uri("https://i.pinimg.com/474x/" + id + ".jpg"), 474, 474);

    [Fact]
    public async Task Trace_encoding_failure_disposes_selection_and_overlay()
    {
        using var harness = new Harness(SearchProviderIds.TraceMoe, cropThrows: true);
        var selection = NewSelection();
        harness.Overlay.Enqueue(new VisualSelection(selection, SearchProviderIds.TraceMoe));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync());

        Assert.Throws<ObjectDisposedException>(() => _ = selection.FrozenFrame);
        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(1, harness.Overlay.DisposeCalls);
    }

    [Theory]
    [InlineData(SearchProviderIds.GoogleLens)]
    [InlineData(SearchProviderIds.TraceMoe)]
    public async Task Active_session_keeps_its_crop_limit_and_the_next_session_uses_updated_options(string provider)
    {
        var first = new FakeOverlay();
        var second = new FakeOverlay();
        using var harness = new Harness(provider, overlays: [first, second]);
        var run = harness.RunAsync();
        var originalLaunch = harness.Factory.Options!;
        Assert.True(harness.Service.Apply(new SettingsEdits { MaxLongSidePx = 256, PaddingPx = 18,
            LassoMinDiagonalPx = 30 }).Success);
        first.Enqueue(new VisualSelection(NewSelection(), provider));
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { 1600 }, harness.CropLimits);
        Assert.Equal(8, originalLaunch.CaptureOptions.PaddingPx);
        var next = harness.RunAsync();
        second.Enqueue(new VisualSelection(NewSelection(), provider));
        await next.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { 1600, 256 }, harness.CropLimits);
        Assert.Equal(18, harness.Factory.Options!.CaptureOptions.PaddingPx);
        Assert.Equal(30, harness.Factory.Options.CaptureOptions.MinDiagonalPx);
    }

    private sealed class Harness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;
        private readonly string _logDirectory;

        public Harness(
            string providerId = SearchProviderIds.GoogleLens,
            bool saveThrows = false,
            IReadOnlyList<FakeOverlay>? overlays = null,
            IImageTranslationProvider? translationProvider = null,
            bool cropThrows = false,
            bool traceFactoryThrows = false,
            Func<BitmapSource, Task>? saveImage = null)
        {
            _logDirectory = Path.Combine(TestOutputPaths.TempDirectory, "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_logDirectory);
            Log = new PluginLog(_logDirectory);
            Service = TestSettings.Create(SettingsValidator.Normalize(new AppSettings { SearchProviderId = providerId, HideDelayMilliseconds = 0 }, out _), _ =>
            {
                SaveCalls++;
                if (saveThrows) throw new IOException("disk unavailable");
            });
            _router = new VisualSearchProviderRouter(
                [
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
                        () => Google),
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.TraceMoe, "trace.moe"),
                        () => Trace),
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"),
                        () => Yandex),
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.Pinterest, "Pinterest"),
                        () => Pinterest),
                ],
                SearchProviderIds.GoogleLens,
                Log);
            var overlaySessions = overlays ?? [new FakeOverlay()];
            Overlay = overlaySessions[0];
            Factory = new FakeOverlayFactory(overlaySessions);
            Notifier = new FakeNotifier(Errors, Messages);
            var presenterUrls = new UrlOpeningService(_ => true, Notifier, TestUiStrings.English, Log);
            var runtimeNotice = new WebViewRuntimeNotice(() => "1.0", Notifier, presenterUrls, TestUiStrings.English);
            var visualPresenter = new VisualSearchResultPresenter(
                new FakeBrowserHost(),
                presenterUrls,
                runtimeNotice,
                Notifier,
                TestUiStrings.English,
                Log);
            var visualSearch = new VisualSearchWorkflow(
                _router,
                (_, _, max) =>
                {
                    CropLimits.Add(max);
                    if (cropThrows) throw new InvalidOperationException("encoding failed");
                    return [1];
                },
                visualPresenter,
                Notifier,
                TestUiStrings.English,
                Log);
            var musicRecognition = new MusicRecognitionWorkflow(Music, Simulator, _ => { }, Log);
            var musicPresenter = new MusicResultPresenter(
                new UrlOpeningService(url => { Events.Add("open"); Opened.Add(url); return true; },
                    Notifier, TestUiStrings.English, Log),
                Notifier,
                TestUiStrings.English);
            var providerSelection = new ProviderSelectionStore(
                _router,
                Service,
                Notifier,
                TestUiStrings.English,
                Log);
            var textSearch = new TextSearchWorkflow(
                new TextSearchUrlBuilder(),
                () => Service.Snapshot.TextSearchEngineId,
                () => Service.Snapshot.TextSearchInBuiltInBrowser,
                TextHost,
                new UrlOpeningService(url => { Events.Add("text-open"); Opened.Add(url); return true; },
                    Notifier, TestUiStrings.English, Log),
                Notifier,
                TestUiStrings.English,
                Log);
            var screenTranslation = new ScreenTranslationWorkflow(translationProvider ?? new FakeTranslationProvider(), Log);
            var imageAsk = new ImageAskWorkflow(
                (image, question) =>
                {
                    AskedImages.Add(image);
                    AskedQuestions.Add(question);
                    return new FakeBrowserOperation();
                },
                (_, _, _) =>
                {
                    AskCrops++;
                    return [7];
                },
                new VisualSearchResultPresenter(
                    AskHost,
                    presenterUrls,
                    runtimeNotice,
                    Notifier,
                    TestUiStrings.English,
                    Log),
                TestUiStrings.English,
                Log);
            var lensPrewarm = new LensPrewarmWorkflow(
                image =>
                {
                    LensImages.Add(image);
                    return new FakeBrowserOperation();
                },
                (_, _, _) =>
                {
                    LensCrops++;
                    return [9];
                },
                new VisualSearchResultPresenter(
                    LensHost,
                    presenterUrls,
                    runtimeNotice,
                    Notifier,
                    TestUiStrings.English,
                    Log),
                TestUiStrings.English,
                Log);
            Workflow = new OverlaySessionWorkflow(
                Factory,
                visualSearch,
                (overlay, cancellation) => new OverlayMusicSession(
                    overlay, musicRecognition, musicPresenter, Log, cancellation),
                (overlay, cancellation) => new OverlayTranslationSession(
                    overlay, screenTranslation, cancellation),
                (overlay, maxLongSidePx, cancellation) =>
                {
                    if (traceFactoryThrows) throw new InvalidOperationException("trace factory failed");
                    return new OverlayWidgetSession(
                        overlay,
                        visualSearch,
                        CompositionRoot.CreateWidgetVisuals(null).Keys.ToHashSet(),
                        maxLongSidePx,
                        new UrlOpeningService(OpenTrace, Notifier, TestUiStrings.English, Log),
                        cancellation);
                },
                providerSelection,
                TestUiStrings.English,
                Log,
                textSearch,
                saveImage,
                (maxLongSidePx, cancellation) => new OverlayAskSession(imageAsk, maxLongSidePx, cancellation),
                (maxLongSidePx, cancellation) => new OverlayLensSession(lensPrewarm, maxLongSidePx, cancellation),
                cancellation => new OverlayTextSearchSession(textSearch, cancellation));
            AskHost.Revealed = () => CloseCallsWhenRevealed.Add(Overlay.CloseCalls);
            LensHost.Revealed = () => LensRevealedAfterClose.Add(Overlay.CloseCompletion.IsCompleted);
        }

        public FakeBrowserHost LensHost { get; } = new();
        public NavigatingBrowserHost TextHost { get; } = new();
        public List<Task<byte[]>> LensImages { get; } = [];
        public List<bool> LensRevealedAfterClose { get; } = [];
        public int LensCrops { get; private set; }

        public FakeBrowserHost AskHost { get; } = new();
        public List<Task<byte[]>> AskedImages { get; } = [];
        public List<Task<string>> AskedQuestions { get; } = [];
        public List<int> CloseCallsWhenRevealed { get; } = [];
        public int AskCrops { get; private set; }

        public OverlaySessionWorkflow Workflow { get; }
        public SettingsService Service { get; }
        public AppSettings Settings => Service.Snapshot;
        public PluginLog Log { get; }
        public FakeNotifier Notifier { get; }
        public FakeOverlay Overlay { get; }
        public FakeOverlayFactory Factory { get; }
        public FakeTraceProvider Trace { get; } = new();
        public FakePinterestProvider Pinterest { get; } = new();
        public FakeProvider Google { get; } = new();
        public FakeProvider Yandex { get; } = new();
        public FakeMusicRecognizer Music { get; } = new();
        public FakeMusicSimulator Simulator { get; } = new();
        public List<string> Errors { get; } = [];
        public List<string> Messages { get; } = [];
        public List<string> Opened { get; } = [];
        public List<string> Events => Overlay.Events;
        public bool TraceOpenResult { get; set; } = true;
        public bool TraceOpenThrows { get; set; }
        public int TraceOpenCalls { get; private set; }
        public int UploadStartedCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public List<int> CropLimits { get; } = [];

        public Task RunAsync(CancellationToken cancellationToken = default) =>
            Workflow.RunAsync(GetSessionOptions(), () => UploadStartedCalls++, cancellationToken);

        private bool OpenTrace(string url)
        {
            TraceOpenCalls++;
            Events.Add("widget-open");
            Opened.Add(url);
            if (TraceOpenThrows) throw new InvalidOperationException("open failed");
            return TraceOpenResult;
        }

        private SearchSessionOptions GetSessionOptions() => SearchSessionOptions.From(Settings,
            new CircleToSearch.TextRecognition.OcrLanguageCatalog([]), System.Globalization.CultureInfo.CurrentUICulture);

        public SearchCoordinator CreateCoordinator() => new(
            Workflow,
            () => Task.CompletedTask,
            GetSessionOptions,
            Notifier,
            TestUiStrings.English,
            Log);

        public void Dispose() => _router.Dispose();
    }

    private sealed class FakeNotifier(List<string> errors, List<string> messages) : CircleToSearch.Ui.IPluginNotifier
    {
        public Action? OnError { get; set; }
        public void ShowMessage(string title, string message) => messages.Add(message);
        public void ShowMessageWithButton(string title, string message, string button, Action action) =>
            messages.Add(message);
        public void ShowError(string title, string message)
        {
            OnError?.Invoke();
            errors.Add(message);
        }
    }

    private sealed class FakeOverlayFactory : IOverlaySessionFactory
    {
        private readonly Queue<FakeOverlay> _overlays;

        public FakeOverlayFactory(IEnumerable<FakeOverlay> overlays) => _overlays = new Queue<FakeOverlay>(overlays);

        public OverlayLaunchOptions? Options { get; private set; }
        public int Calls { get; private set; }
        public List<FakeOverlay> Opened { get; } = [];

        public Task<IOverlaySession?> OpenAsync(OverlayLaunchOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            Options = options;
            var overlay = _overlays.Dequeue();
            Opened.Add(overlay);
            return Task.FromResult<IOverlaySession?>(overlay);
        }
    }

    private sealed class FakeOverlay : IOverlaySession
    {
        public Task CloseCompletion { get; set; } = Task.CompletedTask;
        public Task CloseAsyncCompletion { get; set; } = Task.CompletedTask;
        public TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitForCloseAsync(CancellationToken cancellationToken) =>
            CloseCompletion.WaitAsync(cancellationToken);
        private readonly Channel<IOverlayCommand> _commands = Channel.CreateUnbounded<IOverlayCommand>();
        public void Enqueue(IOverlayCommand command) => _commands.Writer.TryWrite(command);
        public void CompleteCommands() => _commands.Writer.TryComplete();
        public MusicRecognitionOutcome? Result { get; private set; }
        public bool CloseAfterResult { get; set; }
        public bool CloseAfterTranslation { get; set; }
        public ScreenTranslationResult? TranslationResult { get; private set; }
        public (Guid RequestId, TranslationFailure Failure)? TranslationFailure { get; private set; }
        public int TranslationFailureCalls { get; private set; }
        public Exception? ShowResultException { get; set; }
        public Action<int>? OnResult { get; set; }
        public int ResultCalls { get; private set; }
        public int ListeningCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int AudioFrames { get; private set; }
        public Exception? CloseException { get; set; }
        public List<string> Events { get; } = [];
        public List<IOverlayCommand> CommandsAfterResult { get; } = [];
        public Task<IOverlayCommand> ReadCommandAsync(CancellationToken cancellationToken) =>
            _commands.Reader.ReadAsync(cancellationToken).AsTask();
        public Task ShowListeningAsync(CancellationToken cancellationToken)
        {
            ListeningCalls++;
            Events.Add("listening");
            return Task.CompletedTask;
        }
        public Task ReportAudioAsync(MusicVisualizationFrame frame, CancellationToken cancellationToken)
        {
            AudioFrames++;
            return Task.CompletedTask;
        }
        public VisualSearchPreparationOutcome? WidgetResult { get; private set; }
        public Task ShowWidgetResultAsync(VisualSearchPreparationOutcome outcome, CancellationToken cancellationToken)
        {
            WidgetResult = outcome;
            Events.Add("widget");
            foreach (var command in CommandsAfterResult) Enqueue(command);
            Enqueue(new CancelSession());
            return Task.CompletedTask;
        }
        public Task ShowMusicResultAsync(MusicRecognitionOutcome outcome, CancellationToken cancellationToken)
        {
            if (ShowResultException is not null) throw ShowResultException;
            Result = outcome;
            ResultCalls++;
            Events.Add("result");
            foreach (var command in CommandsAfterResult) Enqueue(command);
            OnResult?.Invoke(ResultCalls);
            if (CloseAfterResult) Enqueue(new CancelSession());
            return Task.CompletedTask;
        }
        public Task CloseAsync()
        {
            CloseCalls++;
            Events.Add("close");
            CloseStarted.TrySetResult();
            if (CloseException is not null) throw CloseException;
            return CloseAsyncCompletion;
        }
        public Task ShowTranslationAsync(ScreenTranslationResult result, CancellationToken cancellationToken)
        {
            TranslationResult = result;
            Events.Add("translation");
            if (CloseAfterTranslation) Enqueue(new CancelSession());
            return Task.CompletedTask;
        }
        public Task ShowTranslationFailureAsync(
            Guid requestId,
            TranslationFailure failure,
            CancellationToken cancellationToken)
        {
            TranslationFailure = (requestId, failure);
            TranslationFailureCalls++;
            Events.Add("translation-failure");
            if (CloseAfterTranslation) Enqueue(new CancelSession());
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            while (_commands.Reader.TryRead(out var command))
                OverlayCommandOwnership.DisposePayload(command);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTraceProvider : IVisualSearchProvider
    {
        public TraceMoeMatch? Match { get; set; }
        public VisualSearchPreparationOutcome? Outcome { get; set; }
        public TaskCompletionSource<VisualSearchPreparationOutcome>? FirstGate { get; set; }
        public int Calls { get; private set; }
        public int CanceledCalls { get; private set; }
        public Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            if (Calls == 1 && FirstGate is not null) return WaitForFirstAsync(cancel);
            return Task.FromResult(Outcome ??
                VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(Match)));
        }

        private async Task<VisualSearchPreparationOutcome> WaitForFirstAsync(CancellationToken cancellationToken)
        {
            try { return await FirstGate!.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                CanceledCalls++;
                throw;
            }
        }
    }

    private sealed class FakePinterestProvider : IVisualSearchProvider
    {
        public IReadOnlyList<PinterestPin> Pins { get; set; } = [];
        public int Calls { get; private set; }
        public Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForPinterest(Pins)));
        }
    }

    private sealed class FakeProvider : IVisualSearchProvider
    {
        public int Calls { get; private set; }
        public Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(VisualSearchPreparationOutcome.Ready(
                PreparedVisualSearch.ForUrl(new Uri("https://example.com/results"), null)));
        }
    }

    private sealed class FakeBrowserOperation : IVisualSearchBrowserOperation
    {
        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
            => Task.FromResult(VisualSearchBrowserOperationStatus.Succeeded);
    }

    private sealed class FakeBrowserHost : ISearchBrowserHost
    {
        public Action? Revealed { get; set; }
        public int CanceledCalls { get; private set; }

        public async Task<SearchBrowserShowResult> ShowAsync(
            SearchProviderDescriptor descriptor,
            PreparedVisualSearch preparedSearch,
            CancellationToken cancel)
        {
            if (preparedSearch.RevealAfter is { } reveal)
            {
                try { await reveal.WaitAsync(cancel); }
                catch (OperationCanceledException)
                {
                    CanceledCalls++;
                    return new SearchBrowserShowResult(SearchBrowserShowStatus.Canceled);
                }
                Revealed?.Invoke();
            }
            return new SearchBrowserShowResult(SearchBrowserShowStatus.Shown);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FakeTranslationProvider : IImageTranslationProvider
    {
        public Task<BitmapSource> TranslateAsync(
            BitmapSource source,
            string target,
            CancellationToken cancellation) => Task.FromResult(source);
    }

    private sealed class FailingTranslationProvider : IImageTranslationProvider
    {
        public Task<BitmapSource> TranslateAsync(
            BitmapSource source,
            string target,
            CancellationToken cancellation) => Task.FromException<BitmapSource>(
                new HttpRequestException("rate limited", null, System.Net.HttpStatusCode.TooManyRequests));
    }

    private sealed class FirstCancellationThenSuccessTranslationProvider : IImageTranslationProvider
    {
        public int Calls { get; private set; }
        public int CanceledCalls { get; private set; }

        public async Task<BitmapSource> TranslateAsync(
            BitmapSource source,
            string target,
            CancellationToken cancellation)
        {
            Calls++;
            if (Calls > 1) return source;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
                throw new InvalidOperationException("The translation wait completed without cancellation.");
            }
            catch (OperationCanceledException)
            {
                CanceledCalls++;
                throw;
            }
        }
    }

    private sealed class FakeMusicRecognizer : IMusicRecognizer
    {
        public int Calls { get; private set; }
        public int CanceledCalls { get; private set; }
        public TaskCompletionSource<MusicRecognitionOutcome>? Gate { get; set; }
        public MusicRecognitionOutcome Outcome { get; set; } =
            MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch);
        public bool ReportFrame { get; set; }
        public bool ThrowSynchronously { get; set; }
        public bool CompleteGateOnCancellation { get; set; } = true;
        public bool ThrowFromCancellationCallback { get; set; }
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MusicRecognitionOutcome> RecognizeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (ThrowSynchronously) throw new InvalidOperationException("recognizer failed");
            if (Gate is null) return Task.FromResult(Outcome);
            if (ThrowFromCancellationCallback)
                cancellationToken.Register(() => throw new InvalidOperationException("cancellation callback failed"));
            cancellationToken.Register(() =>
            {
                CanceledCalls++;
                CancellationObserved.TrySetResult();
                if (CompleteGateOnCancellation)
                    Gate.TrySetResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled));
            });
            return Gate.Task;
        }

        public Task<MusicRecognitionOutcome> RecognizeAsync(
            IMusicVisualizationProgress? progress,
            CancellationToken cancellationToken)
        {
            if (ReportFrame)
                progress?.Report(new MusicVisualizationFrame(TimeSpan.Zero, 0.5, 0.7, false));
            return RecognizeAsync(cancellationToken);
        }
    }

    private sealed class FakeMusicSimulator : IMusicRecognitionSimulator
    {
        public int Calls { get; private set; }
        public MusicDebugScenario? LastScenario { get; private set; }

        public Task<MusicRecognitionOutcome> RecognizeAsync(
            MusicDebugScenario scenario,
            IMusicVisualizationProgress? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastScenario = scenario;
            return Task.FromResult(MusicRecognitionOutcome.Matched(new ShazamRecognition(
                "Track", "Artist", null, null, null, null, null)));
        }
    }
}
