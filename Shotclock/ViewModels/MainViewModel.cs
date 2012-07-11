using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using LibGit2Sharp;
using ReactiveUI;

namespace Shotclock.ViewModels
{
    public interface IMainViewModel : IReactiveNotifyPropertyChanged
    {
        bool IsVersionControlled { get; }
        string RepositoryRoot { get; }

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

        public MainViewModel(IObservable<Unit> gotFocusNotification = null, IObservable<Unit> refreshNotification = null, Func<TimeSpan> getIdleTime = null)
        {
            RepositoryRoot = findRepositoryRoot(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            IsVersionControlled = (!String.IsNullOrEmpty(RepositoryRoot));

            GotFocusNotification = gotFocusNotification ?? Observable.Never<Unit>();

            var fswWatch = createFileChangeWatch(Path.Combine(RepositoryRoot, ".git"))
                .Select(_ => Unit.Default)
                .Multicast(new Subject<Unit>());

            if (refreshNotification != null) {
                RefreshNotification = refreshNotification;
            } else {
                RefreshNotification = fswWatch;
                fswWatch.Subscribe(_ => Console.WriteLine("Changed!"));
                fswWatch.Connect();
            }

            Observable.Merge(GotFocusNotification, RefreshNotification)
                .Where(_ => IsVersionControlled)
                .Select(_ => fetchLatestCommitForHead(RepositoryRoot))
                //.Select(x => x.Author.When)
                .Select(_ => DateTimeOffset.Now)
                .ToProperty(this, x => x.LatestCommit, DateTimeOffset.MinValue);

            var shouldUpdateClock = Observable.Merge(
                gotFocusNotification,
                this.WhenAny(x => x.LatestCommit, _ => Unit.Default),
                Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1.0), RxApp.DeferredScheduler).Select(_ => Unit.Default)
            ).Where(_ => IsVersionControlled);

            shouldUpdateClock
                .Select(_ => getIdleTime != null ? getIdleTime() : this.getIdleTime())
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

        IObservable<string> createFileChangeWatch(string target)
        {
            return Observable.Create<string>(subj => {
                var fsw = new FileSystemWatcher(target);

                var anyEvent = Observable.Merge(
                    Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(x => fsw.Changed += x, x => fsw.Changed -= x).Select(x => x.EventArgs.FullPath),
                    Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(x => fsw.Created += x, x => fsw.Created -= x).Select(x => x.EventArgs.FullPath),
                    Observable.FromEventPattern<FileSystemEventHandler, FileSystemEventArgs>(x => fsw.Deleted += x, x => fsw.Deleted -= x).Select(x => x.EventArgs.FullPath),
                    Observable.FromEventPattern<RenamedEventHandler, RenamedEventArgs>(x => fsw.Renamed += x, x => fsw.Renamed -= x).Select(x => x.EventArgs.FullPath));

                fsw.EnableRaisingEvents = true;

                return new CompositeDisposable(anyEvent.Subscribe(subj), fsw);
            }).Throttle(TimeSpan.FromMilliseconds(1200), RxApp.TaskpoolScheduler).ObserveOn(RxApp.DeferredScheduler);
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
