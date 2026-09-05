namespace RuinaoSoftwareWpf.Tests;

using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

public sealed class EmotionQuestionFlowTests
{
    [Fact]
    public Task BothQuestions_ShowDuringThinking_AndManualStartWorksForQuestionTwo() => RunSta(() =>
    {
        using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        Assert.True(vm.IsEmotionQuestionWaiting);
        Assert.True(vm.IsEmotionQuestionPromptVisible);
        Assert.Contains("过去两周", vm.EmotionQuestionText);
        Assert.DoesNotContain(fixture.Media.Calls, c => c.Name == "RequestStop");

        for (var question = 1; question <= 2; question++)
        {
            Assert.True(vm.StartEmotionQuestionCommand.CanExecute(null));
            vm.StartEmotionQuestionCommand.Execute(null);
            Assert.True(vm.IsEmotionQuestionAnswering);
            Assert.False(vm.StartEmotionQuestionCommand.CanExecute(null));
            // Repeated clicks must not restart the answer timer.
            fixture.Advance(10_000);
            vm.StartEmotionQuestionCommand.Execute(null);
            fixture.Advance(9_999);
            Assert.False(vm.CanCompleteEmotionQuestionAnswer);
            fixture.Advance(1);
            Assert.True(vm.CanCompleteEmotionQuestionAnswer);
            vm.CompleteEmotionQuestionAnswerCommand.Execute(null);
            if (question == 1)
            {
                Assert.True(vm.IsEmotionQuestionWaiting);
                Assert.Contains("请选一件", vm.EmotionQuestionText);
                Assert.Contains("60", vm.EmotionQuestionStatusText);
                Assert.DoesNotContain(fixture.Media.Calls, c => c.Name == "RequestStop");
            }
        }

        Assert.True(vm.IsSavingStage);
        var stop = Assert.Single(fixture.Media.Calls, c => c.Name == "RequestStop");
        Assert.Equal(CaptureMediaStopReason.Completed, stop.Args[0]);
        Assert.Equal(2, fixture.Events.Count(e => e.Type == "emotion_question_presented"));
        var completed = fixture.Events.Where(e => e.Type == "emotion_question_answer_completed").ToArray();
        Assert.Equal(2, completed.Length);
        Assert.All(completed, e =>
        {
            Assert.Equal(20_000, e.Payload.GetProperty("durationMs").GetInt64());
            Assert.Equal("manual_completed", e.Payload.GetProperty("completionReason").GetString());
            Assert.False(e.Payload.GetProperty("isTest").GetBoolean());
            Assert.True(e.Payload.GetProperty("presentedAtUnixMs").GetInt64() <=
                        e.Payload.GetProperty("startedAtUnixMs").GetInt64());
        });
        Assert.DoesNotContain(fixture.Events, e => e.Type.Contains("rest"));
    });

    [Fact]
    public Task BothQuestions_AutoStartAtSixty_AndTimeoutAtOneHundredTwenty() => RunSta(() =>
    {
        using var fixture = new Fixture();
        for (var question = 1; question <= 2; question++)
        {
            fixture.Advance(59_999);
            Assert.True(fixture.ViewModel.IsEmotionQuestionWaiting);
            fixture.Advance(1);
            Assert.True(fixture.ViewModel.IsEmotionQuestionAnswering);
            Assert.False(fixture.ViewModel.CanCompleteEmotionQuestionAnswer);
            fixture.Advance(119_999);
            Assert.True(fixture.ViewModel.IsEmotionQuestionAnswering);
            fixture.Advance(1);
        }
        Assert.True(fixture.ViewModel.IsSavingStage);
        Assert.Equal(2, fixture.Events.Count(e => e.Type == "emotion_question_answer_started"));
        Assert.All(fixture.Events.Where(e => e.Type == "emotion_question_answer_completed"), e =>
        {
            Assert.Equal("maximum_timeout", e.Payload.GetProperty("completionReason").GetString());
            Assert.Equal(120_000, e.Payload.GetProperty("durationMs").GetInt64());
        });
        Assert.Single(fixture.Media.Calls, c => c.Name == "RequestStop");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task InterruptedRecordingCallback_ReturnsToFaceCheck_AndResetsThinking(bool development) => RunSta(() =>
    {
        using var fixture = new Fixture();
        var vm = fixture.ViewModel;
        fixture.Advance(10_000);
        vm.DiscardCurrentModuleExecution("test interruption");
        Assert.True(vm.IsFaceStep);
        var session = new CaptureMediaSession(7, development ? null : 2, "test", "emotion_question",
            "test", "unused", fixture.Clock.GetUtcNow());
        var completion = new CaptureMediaCompleted(session, CaptureMediaCompletionStatus.Interrupted, null, "test");
        if (development)
        {
            SetField(vm, "activeDevelopmentMediaSessionId", (long?)7);
            Invoke(vm, "ApplyDevelopmentRecordingCompletion", completion);
        }
        else
        {
            Invoke(vm, "ApplyRecordingCompletion", fixture.Attempt, completion);
        }
        Assert.True(vm.IsFaceStep);
        Assert.False(vm.StartEmotionQuestionCommand.CanExecute(null));
        Assert.False(vm.CanCompleteEmotionQuestionAnswer);
        vm.StartCurrentModule();
        Assert.True(vm.IsEmotionQuestionWaiting);
        Assert.Contains("过去两周", vm.EmotionQuestionText);
        fixture.Advance(59_999);
        Assert.True(vm.IsEmotionQuestionWaiting);
    });

    private sealed class Fixture : IDisposable
    {
        public TestClock Clock { get; } = new();
        public RecordingProxy Media { get; }
        public List<(string Type, JsonElement Payload)> Events { get; } = [];
        public AssessmentCaptureViewModel ViewModel { get; }
        public AssessmentModuleRunContext Attempt { get; }

        public Fixture()
        {
            var media = RecordingProxy.Create<ICaptureMediaService>();
            Media = (RecordingProxy)(object)media;
            Media.Result = (name, _) => name == "get_IsCapturing" ? true : null;
            var recorder = RecordingProxy.Create<IModuleEventRecorder>();
            ((RecordingProxy)(object)recorder).Result = (name, args) =>
            {
                if (name == "Enqueue")
                    Events.Add(((string)args[0]!, JsonSerializer.SerializeToElement(args[2])));
                return null;
            };
            var coordinator = new AssessmentWorkbenchCoordinator();
            ViewModel = new AssessmentCaptureViewModel(media,
                RecordingProxy.Create<ICaptureFormRecordService>(),
                RecordingProxy.Create<ICameraCaptureService>(),
                new AppLocalizationService(),
                RecordingProxy.Create<IUserDialogService>(), recorder,
                RecordingProxy.Create<IUnifiedSessionService>(),
                RecordingProxy.Create<IAssessmentModule>(),
                RecordingProxy.Create<IPatientService>(),
                RecordingProxy.Create<IToastService>(), coordinator, Clock);
            coordinator.CurrentModuleIndex = ViewModel.ModuleProgressItems
                .Select((m, i) => (m, i)).Single(x => x.m.Code == "emotion_question").i;
            Attempt = new AssessmentModuleRunContext(1, 2, 1, "test", "test",
                AssessmentModuleTypeIds.EmotionQuestion, "emotion_question", "test", 5, Clock.GetUtcNow());
            SetField(ViewModel, "activeModuleAttempt", Attempt);
            ViewModel.CompleteDemo();
            ViewModel.BeginFaceCheck();
            ViewModel.StartCurrentModule();
        }

        public void Advance(int milliseconds)
        {
            Clock.Milliseconds += milliseconds;
            Invoke(ViewModel, "AdvanceEmotionQuestion");
        }

        public void Dispose() => Invoke(ViewModel, "StopModuleExecutionTimers");
    }

    public class RecordingProxy : DispatchProxy
    {
        public List<(string Name, object?[] Args)> Calls { get; } = [];
        public Func<string, object?[], object?>? Result { get; set; }
        public static T Create<T>() where T : class => Create<T, RecordingProxy>();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod!;
            var arguments = args ?? [];
            Calls.Add((method.Name, arguments));
            var value = Result?.Invoke(method.Name, arguments);
            if (value is not null) return value;
            if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
            if (method.ReturnType == typeof(void)) return null;
            return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(Milliseconds);
    }

    private static void SetField(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static Task RunSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
