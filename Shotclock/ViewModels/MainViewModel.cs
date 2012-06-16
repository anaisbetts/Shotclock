using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LibGit2Sharp;
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
        public bool IsVersionControlled { get; protected set; }
        public string RepositoryRoot { get; protected set; }
    
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
            RepositoryRoot = findRepositoryRoot();
            IsVersionControlled = (!String.IsNullOrEmpty(RepositoryRoot));

            GotFocusNotification = gotFocusNotification ?? Observable.Never<Unit>();
            RefreshNotification = refreshNotification ?? 
                (IsVersionControlled ? createFileChangeWatch(RepositoryRoot) : Observable.Never<Unit>());

            Observable.Merge(gotFocusNotification, refreshNotification)
                .Where(_ => IsVersionControlled)
                .Select(_ => fetchLatestCommitForHead(RepositoryRoot))
                .Select(x => x.Author.When)
                .ToProperty(this, x => LatestCommit, DateTimeOffset.MinValue);

            var shouldUpdateClock = Observable.Merge(
                gotFocusNotification,
                this.WhenAny(x => x.LatestCommit, _ => Unit.Default),
                Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1.0)).Select(_ => Unit.Default)
            ).Where(_ => IsVersionControlled);

            shouldUpdateClock
                .Select(_ => getIdleTime())
                .Buffer(2, 1).Where(x => x[1] < TimeSpan.FromSeconds(5.0) && x[0] > TimeSpan.FromSeconds(30.0))
                .Select(_ => RxApp.DeferredScheduler.Now)
                .StartWith(RxApp.DeferredScheduler.Now)
                .ToProperty(this, x => x.EarliestActiveTime);

            shouldUpdateClock
                .Select(_ => RxApp.DeferredScheduler.Now - (EarliestActiveTime > LatestCommit ? EarliestActiveTime : LatestCommit))
                .ToProperty(this, x => x.CommitAge);
        }

        TimeSpan getIdleTime()
        {
            var idleTime = new LASTINPUTINFO { cbSize = LASTINPUTINFO.SizeOf };
            GetLastInputInfo(ref idleTime);

            var currentTicks = GetTickCount();

            long deltaTicks;
            if (idleTime.dwTime > currentTicks) {
                deltaTicks = (((long)currentTicks + UInt32.MaxValue) - idleTime.dwTime);
            } else {
                deltaTicks = currentTicks - idleTime.dwTime;
            }

            return new TimeSpan(deltaTicks);
        }

        Commit fetchLatestCommitForHead(string repositoryRoot)
        {
            if (String.IsNullOrEmpty(repositoryRoot)) {
                return null;
            }

            var repo = default(Repository);
            try {
                repo = new Repository(repositoryRoot);
                return repo.Commits.FirstOrDefault();
            } catch (Exception ex) {
                this.Log().ErrorException("Couldn't open repo", ex);
                return null;
            } finally {
                if (repo != null) {
                    repo.Dispose();
                }
            }
        }

        IObservable<Unit> createFileChangeWatch(string repositoryRoot)
        {
            return Observable.Never<Unit>();
        }

        string findRepositoryRoot(string rootDirectory = null)
        {
            rootDirectory = rootDirectory ?? RepositoryRoot;

            if (Directory.Exists(Path.Combine(rootDirectory, ".git"))) {
                return rootDirectory;
            }

            var di = new DirectoryInfo(rootDirectory);
            return (di.Parent.Exists ? findRepositoryRoot(di.Parent.FullName) : null);
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("kernel32.dll")]
        static extern UInt32 GetTickCount();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO
    {
        public static readonly UInt32 SizeOf = (UInt32)Marshal.SizeOf(typeof(LASTINPUTINFO));
        public UInt32 cbSize;
        public UInt32 dwTime;
    }
}
