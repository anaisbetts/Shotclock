using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using ReactiveUI;

namespace Shotclock.ViewModels
{
    public interface IMainViewModel : IReactiveNotifyPropertyChanged
    {
        bool IsVersionControlled { get; }
        string RepositoryRoot { get;  }

        IObservable<Unit> RefreshNotification { get; }
        IObservable<Unit> GotFocusNotification { get; }
            
        DateTimeOffset LatestCommit { get; }
        DateTimeOffset EarliestActiveTime { get; }
        TimeSpan CommitAge { get; }
    }

    public class MainViewModel : ReactiveObject, IMainViewModel
    {
        ObservableAsPropertyHelper<bool> _IsVersionControlled;
        public bool IsVersionControlled {
            get { return _IsVersionControlled.Value; }
        }

        ObservableAsPropertyHelper<string> _RepositoryRoot;
        public string RepositoryRoot {
            get { return _RepositoryRoot.Value; }
        }

        public IObservable<Unit> RefreshNotification { get; protected set; }
        public IObservable<Unit> GotFocusNotification { get; protected set; }

        ObservableAsPropertyHelper<DateTimeOffset> _LatestCommit;
        public DateTimeOffset LatestCommit {
            get { return _LatestCommit.Value; }
        }

        ObservableAsPropertyHelper<DateTimeOffset> _EarliestActiveTime;
        public DateTimeOffset EarliestActiveTime {
            get { return _EarliestActiveTime.Value; }
        }

        ObservableAsPropertyHelper<TimeSpan> _CommitAge;
        public TimeSpan CommitAge {
            get { return _CommitAge.Value; }
        }

        public MainViewModel(IObservable<Unit> gotFocusNotification = null, IObservable<Unit> refreshNotification = null)
        {
            var shouldUpdateClock = Observable.Merge(
                gotFocusNotification,
                Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)).Select(_ => Unit.Default));

            shouldUpdateClock
                .Select(_ => RxApp.DeferredScheduler.Now - (EarliestActiveTime > LatestCommit ? EarliestActiveTime : LatestCommit))
                .ToProperty(this, x => x.CommitAge);
        }
    }
}
