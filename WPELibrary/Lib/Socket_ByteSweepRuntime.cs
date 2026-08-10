using System;
using System.ComponentModel;
using System.Threading;

namespace WPELibrary.Lib
{
    public enum Socket_ByteSweepRuntimeState
    {
        Idle,
        Starting,
        Running,
        Pausing,
        Paused,
        Stopping,
        Completed,
        Cancelled,
        Faulted
    }

    public sealed class Socket_ByteSweepRuntimeSnapshot
    {
        public Guid JobId { get; set; }
        public Guid PresetId { get; set; }
        public string Revision { get; set; }
        public Socket_ByteSweepRuntimeState State { get; set; }
        public string PresetName { get; set; }
        public string Mode { get; set; }
        public int CurrentLoop { get; set; }
        public int LoopCount { get; set; }
        public int Position { get; set; }
        public int ByteNumber { get; set; }
        public int ByteCount { get; set; }
        public int ValueNumber { get; set; }
        public byte OriginalValue { get; set; }
        public byte CurrentValue { get; set; }
        public bool IsPairCombination { get; set; }
        public int PairFirstPosition { get; set; }
        public int PairSecondPosition { get; set; }
        public int PairFirstValueNumber { get; set; }
        public int PairSecondValueNumber { get; set; }
        public int PairFirstValueCount { get; set; }
        public int PairSecondValueCount { get; set; }
        public byte PairFirstValue { get; set; }
        public byte PairSecondValue { get; set; }
        public long PlannedTotal { get; set; }
        public long TotalSend { get; set; }
        public long Success { get; set; }
        public long Failure { get; set; }
        public string Detail { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public sealed class Socket_ByteSweepLogEntry : INotifyPropertyChanged
    {
        private string detail;
        private string progress;
        private long totalSend;
        private long success;
        private long failure;

        public DateTime Timestamp { get; private set; }
        public string Time { get { return Timestamp.ToString("HH:mm:ss.fff"); } }
        public string State { get; set; }
        public string PresetName { get; private set; }
        public string Mode { get; private set; }
        public string Progress
        {
            get { return this.progress; }
            set { this.SetValue(ref this.progress, value, "Progress"); }
        }
        public long TotalSend
        {
            get { return this.totalSend; }
            set { this.SetValue(ref this.totalSend, value, "TotalSend"); }
        }
        public long Success
        {
            get { return this.success; }
            set { this.SetValue(ref this.success, value, "Success"); }
        }
        public long Failure
        {
            get { return this.failure; }
            set { this.SetValue(ref this.failure, value, "Failure"); }
        }
        public string Detail
        {
            get { return this.detail; }
            set { this.SetValue(ref this.detail, value, "Detail"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public static Socket_ByteSweepLogEntry Create(
            Socket_ByteSweepRuntimeSnapshot snapshot,
            string state,
            string progress,
            string detail)
        {
            return new Socket_ByteSweepLogEntry
            {
                Timestamp = snapshot == null ? DateTime.Now : snapshot.Timestamp,
                State = state ?? string.Empty,
                PresetName = snapshot == null ? string.Empty : snapshot.PresetName,
                Mode = snapshot == null ? string.Empty : snapshot.Mode,
                Progress = progress ?? string.Empty,
                TotalSend = snapshot == null ? 0 : snapshot.TotalSend,
                Success = snapshot == null ? 0 : snapshot.Success,
                Failure = snapshot == null ? 0 : snapshot.Failure,
                Detail = detail ?? string.Empty
            };
        }

        private void SetValue<T>(ref T field, T value, string propertyName)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class Socket_ByteSweepRuntime
    {
        private readonly object syncRoot = new object();
        private CancellationTokenSource cancellationSource;
        private readonly ManualResetEventSlim pauseGate = new ManualResetEventSlim(true);
        private Socket_ByteSweepRuntimeSnapshot snapshot = new Socket_ByteSweepRuntimeSnapshot
        {
            State = Socket_ByteSweepRuntimeState.Idle,
            Timestamp = DateTime.Now
        };

        public static Socket_ByteSweepRuntime Current { get; } = new Socket_ByteSweepRuntime();

        public event Action<Socket_ByteSweepRuntimeSnapshot> StateChanged;
        public event Action<Socket_ByteSweepRuntimeSnapshot> ProgressChanged;
        public event Action<Socket_ByteSweepLogEntry> LogAdded;

        public bool IsBusy
        {
            get
            {
                lock (this.syncRoot)
                {
                    return this.snapshot.State == Socket_ByteSweepRuntimeState.Starting ||
                        this.snapshot.State == Socket_ByteSweepRuntimeState.Running ||
                        this.snapshot.State == Socket_ByteSweepRuntimeState.Pausing ||
                        this.snapshot.State == Socket_ByteSweepRuntimeState.Paused ||
                        this.snapshot.State == Socket_ByteSweepRuntimeState.Stopping;
                }
            }
        }

        public Socket_ByteSweepRuntimeSnapshot GetSnapshot()
        {
            lock (this.syncRoot)
            {
                return Clone(this.snapshot);
            }
        }

        public bool TryStart(
            Guid presetId,
            string presetName,
            string mode,
            int loopCount,
            long plannedTotal,
            out Guid jobId,
            out CancellationTokenSource jobCancellation)
        {
            jobId = Guid.Empty;
            jobCancellation = null;
            Socket_ByteSweepRuntimeSnapshot started;
            lock (this.syncRoot)
            {
                if (this.IsBusy)
                {
                    return false;
                }

                jobId = Guid.NewGuid();
                jobCancellation = new CancellationTokenSource();
                this.cancellationSource = jobCancellation;
                this.pauseGate.Set();
                this.snapshot = new Socket_ByteSweepRuntimeSnapshot
                {
                    JobId = jobId,
                    PresetId = presetId,
                    State = Socket_ByteSweepRuntimeState.Starting,
                    PresetName = presetName ?? string.Empty,
                    Mode = mode ?? string.Empty,
                    LoopCount = Math.Max(1, loopCount),
                    PlannedTotal = Math.Max(0, plannedTotal),
                    Timestamp = DateTime.Now
                };
                started = Clone(this.snapshot);
            }

            this.PublishState(started, "开始递进");
            return true;
        }

        public bool MarkRunning(Guid jobId)
        {
            Socket_ByteSweepRuntimeSnapshot current;
            lock (this.syncRoot)
            {
                if (this.snapshot.JobId != jobId || !this.IsBusy)
                {
                    return false;
                }

                // Pause/stop may win the race while the caller is still
                // starting the worker. Do not overwrite that decision.
                if (this.snapshot.State == Socket_ByteSweepRuntimeState.Paused ||
                    this.snapshot.State == Socket_ByteSweepRuntimeState.Stopping)
                {
                    return true;
                }
                if (this.snapshot.State == Socket_ByteSweepRuntimeState.Running)
                {
                    return true;
                }
                if (this.snapshot.State != Socket_ByteSweepRuntimeState.Starting)
                {
                    return false;
                }

                this.snapshot.State = Socket_ByteSweepRuntimeState.Running;
                this.pauseGate.Set();
                this.snapshot.Timestamp = DateTime.Now;
                current = Clone(this.snapshot);
            }

            this.PublishState(current, "递进运行中");
            return true;
        }

        public bool SetRevision(Guid jobId, string revision)
        {
            lock (this.syncRoot)
            {
                if (this.snapshot.JobId != jobId)
                {
                    return false;
                }
                this.snapshot.Revision = revision ?? string.Empty;
                this.snapshot.Timestamp = DateTime.Now;
                return true;
            }
        }

        public bool RequestPause(Guid jobId)
        {
            Socket_ByteSweepRuntimeSnapshot current;
            lock (this.syncRoot)
            {
                if (this.snapshot.JobId != jobId ||
                    (this.snapshot.State != Socket_ByteSweepRuntimeState.Running &&
                     this.snapshot.State != Socket_ByteSweepRuntimeState.Starting))
                {
                    return false;
                }

                this.pauseGate.Reset();
                this.snapshot.State = Socket_ByteSweepRuntimeState.Paused;
                this.snapshot.Timestamp = DateTime.Now;
                current = Clone(this.snapshot);
            }

            this.PublishState(current, "递进已暂停");
            return true;
        }

        public bool Resume(Guid jobId)
        {
            Socket_ByteSweepRuntimeSnapshot current;
            lock (this.syncRoot)
            {
                if (this.snapshot.JobId != jobId ||
                    this.snapshot.State != Socket_ByteSweepRuntimeState.Paused)
                {
                    return false;
                }

                this.pauseGate.Set();
                this.snapshot.State = Socket_ByteSweepRuntimeState.Running;
                this.snapshot.Timestamp = DateTime.Now;
                current = Clone(this.snapshot);
            }

            this.PublishState(current, "递进继续运行");
            return true;
        }

        public void WaitIfPaused(Guid jobId, CancellationToken token)
        {
            lock (this.syncRoot)
            {
                if (this.snapshot.JobId != jobId)
                {
                    return;
                }
            }
            this.pauseGate.Wait(token);
        }

        public bool RequestStop(Guid jobId)
        {
            Socket_ByteSweepRuntimeSnapshot current;
            lock (this.syncRoot)
            {
                if (this.snapshot.JobId != jobId || !this.IsBusy)
                {
                    return false;
                }

                this.snapshot.State = Socket_ByteSweepRuntimeState.Stopping;
                this.snapshot.Timestamp = DateTime.Now;
                this.pauseGate.Set();
                if (this.cancellationSource != null)
                {
                    this.cancellationSource.Cancel();
                }
                current = Clone(this.snapshot);
            }

            this.PublishState(current, "正在停止递进");
            return true;
        }

        public bool RequestStop()
        {
            Guid jobId;
            lock (this.syncRoot)
            {
                jobId = this.snapshot.JobId;
            }
            return jobId != Guid.Empty && this.RequestStop(jobId);
        }

        public bool PublishProgress(
            Guid jobId,
            Guid presetId,
            string presetName,
            string mode,
            Socket_ByteSweepProgress progress,
            int currentLoop,
            int loopCount,
            long plannedTotal)
        {
            Socket_ByteSweepRuntimeSnapshot current;
            bool presetChanged;
            lock (this.syncRoot)
            {
                if (this.snapshot.JobId != jobId || !this.IsBusy || progress == null)
                {
                    return false;
                }

                presetChanged = this.snapshot.PresetId != Guid.Empty && this.snapshot.PresetId != presetId;
                this.snapshot.PresetId = presetId;
                this.snapshot.PresetName = presetName ?? string.Empty;
                this.snapshot.Mode = mode ?? string.Empty;
                this.snapshot.CurrentLoop = currentLoop;
                this.snapshot.LoopCount = Math.Max(1, loopCount);
                this.snapshot.PlannedTotal = Math.Max(0, plannedTotal);
                this.snapshot.Position = progress.Position;
                this.snapshot.ByteNumber = progress.ByteNumber;
                this.snapshot.ByteCount = progress.ByteCount;
                this.snapshot.ValueNumber = progress.ValueNumber;
                this.snapshot.OriginalValue = progress.OriginalValue;
                this.snapshot.CurrentValue = progress.CurrentValue;
                this.snapshot.IsPairCombination = progress.IsPairCombination;
                this.snapshot.PairFirstPosition = progress.PairFirstPosition;
                this.snapshot.PairSecondPosition = progress.PairSecondPosition;
                this.snapshot.PairFirstValueNumber = progress.PairFirstValueNumber;
                this.snapshot.PairSecondValueNumber = progress.PairSecondValueNumber;
                this.snapshot.PairFirstValueCount = progress.PairFirstValueCount;
                this.snapshot.PairSecondValueCount = progress.PairSecondValueCount;
                this.snapshot.PairFirstValue = progress.PairFirstValue;
                this.snapshot.PairSecondValue = progress.PairSecondValue;
                this.snapshot.TotalSend = progress.TotalSend;
                this.snapshot.Success = progress.Success;
                this.snapshot.Failure = progress.Failure;
                this.snapshot.Timestamp = DateTime.Now;
                current = Clone(this.snapshot);
            }

            this.ProgressChanged?.Invoke(current);
            if (presetChanged)
            {
                this.LogAdded?.Invoke(Socket_ByteSweepLogEntry.Create(
                    current,
                    Socket_ByteSweepRuntimeState.Running.ToString(),
                    string.Empty,
                    "PresetSwitched"));
            }
            return true;
        }

        public void Finish(Guid jobId, bool cancelled, Exception error, string detail)
        {
            Socket_ByteSweepRuntimeSnapshot current;
            CancellationTokenSource completedCancellation;
            lock (this.syncRoot)
            {
                if (this.snapshot.JobId != jobId)
                {
                    return;
                }

                this.snapshot.State = error != null
                    ? Socket_ByteSweepRuntimeState.Faulted
                    : (cancelled ? Socket_ByteSweepRuntimeState.Cancelled : Socket_ByteSweepRuntimeState.Completed);
                this.snapshot.Detail = detail ?? (error == null ? string.Empty : error.Message);
                this.snapshot.Timestamp = DateTime.Now;
                this.pauseGate.Set();
                current = Clone(this.snapshot);
                completedCancellation = this.cancellationSource;
                this.cancellationSource = null;
            }

            this.PublishState(current, current.Detail);
            if (completedCancellation != null)
            {
                completedCancellation.Dispose();
            }
        }

        private void PublishState(Socket_ByteSweepRuntimeSnapshot value, string detail)
        {
            this.StateChanged?.Invoke(value);
            this.LogAdded?.Invoke(Socket_ByteSweepLogEntry.Create(
                value,
                value.State.ToString(),
                string.Empty,
                detail));
        }

        private static Socket_ByteSweepRuntimeSnapshot Clone(Socket_ByteSweepRuntimeSnapshot value)
        {
            return new Socket_ByteSweepRuntimeSnapshot
            {
                JobId = value.JobId,
                PresetId = value.PresetId,
                Revision = value.Revision,
                State = value.State,
                PresetName = value.PresetName,
                Mode = value.Mode,
                CurrentLoop = value.CurrentLoop,
                LoopCount = value.LoopCount,
                Position = value.Position,
                ByteNumber = value.ByteNumber,
                ByteCount = value.ByteCount,
                ValueNumber = value.ValueNumber,
                OriginalValue = value.OriginalValue,
                CurrentValue = value.CurrentValue,
                IsPairCombination = value.IsPairCombination,
                PairFirstPosition = value.PairFirstPosition,
                PairSecondPosition = value.PairSecondPosition,
                PairFirstValueNumber = value.PairFirstValueNumber,
                PairSecondValueNumber = value.PairSecondValueNumber,
                PairFirstValueCount = value.PairFirstValueCount,
                PairSecondValueCount = value.PairSecondValueCount,
                PairFirstValue = value.PairFirstValue,
                PairSecondValue = value.PairSecondValue,
                PlannedTotal = value.PlannedTotal,
                TotalSend = value.TotalSend,
                Success = value.Success,
                Failure = value.Failure,
                Detail = value.Detail,
                Timestamp = value.Timestamp
            };
        }
    }
}
