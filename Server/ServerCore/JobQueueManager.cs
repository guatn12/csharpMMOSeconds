using System;
using System.Collections.Generic;
using System.Threading;

namespace ServerCore
{
    public class JobQueueManager
    {
        public static JobQueueManager Instance { get; } = new JobQueueManager();

        private readonly PriorityQueue<GameJob, JobPriority> _jobQueue = new PriorityQueue<GameJob, JobPriority>();
        private readonly object _lock = new object();

        private readonly SemaphoreSlim _jobSemaphore = new SemaphoreSlim(0);

        private Thread _workerThread;
        private bool _isRunning = false;

        private JobQueueManager() { }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _workerThread = new Thread(ProcessJobs);
            _workerThread.Name = "JobQueue Worker";
            _workerThread.IsBackground = true;
            _workerThread.Start();
            LogManager.Info("JobQueueManager started.");
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _jobSemaphore.Release(1); 
            _workerThread.Join();
            LogManager.Info("JobQueueManager stopped.");
        }

        public void Enqueue(GameJob job)
        {
            lock (_lock)
            {
                _jobQueue.Enqueue(job, job.Priority);
            }
            _jobSemaphore.Release();
            LogManager.Debug("Job enqueued. Priority: {Priority}", job.Priority);
        }

        private void ProcessJobs()
        {
            while (_isRunning)
            {
                _jobSemaphore.Wait();

                if (!_isRunning) break;

                GameJob jobToProcess;
                lock (_lock)
                {
                    if(!_jobQueue.TryDequeue(out jobToProcess, out _))
                    {
                        continue; // 큐가 비어있을 수 있음 (스레드 종료 신호 등)
                    }
                }
                
                try
                {
                    LogManager.Debug("Processing job. Priority: {Priority}", jobToProcess.Priority);
                    jobToProcess.Action.Invoke();
                }
                catch (Exception ex)
                {
                    LogManager.Error(ex, "An unhandled exception occurred while processing a job.");
                }
            }
            LogManager.Info("JobQueueManager worker thread finished.");
        }
    }
}
