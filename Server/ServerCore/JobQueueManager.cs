using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ServerCore
{
    public interface IJobOwner
    {
        ConcurrentQueue<IJob> JobQueue { get; }
    }

    public class JobQueueManager
    {
        public static JobQueueManager Instance { get; } = new JobQueueManager();

        private List<Thread> _threads = new List<Thread>();
        // TODO : GaemSession 대신 IJobOwner를 사용하도록 변경하면 확장성이 좋아집니다.
        private ConcurrentQueue<IJobOwner> _pendingOwners = new ConcurrentQueue<IJobOwner>();
        private volatile bool _isShuttingDown = false;

        private JobQueueManager() { }

        public void Start(int threadCount)
        {
            _isShuttingDown = false;

            for(int i = 0; i < threadCount; i++)
            {
                Thread t = new Thread(WorkerThread);
                t.Name = $"Job Worker Thread_{i}";
                t.Start();
                _threads.Add( t );
            }

            LogManager.Info($"JobQueueManager started with {threadCount} threads.");
        }

        public void Stop()
        {
            _isShuttingDown=true;

            LogManager.Info("JobQueueManager stopping... Waiting for threads to finish.");

            foreach(Thread t in _threads)
            {
                t.Join();
            }
            _threads.Clear();

            while(_pendingOwners.TryDequeue(out _)) ;

            LogManager.Info( "All Job Threads Stopped." );
        }

        public void Push(IJobOwner jobOwner)
        {
			_pendingOwners.Enqueue(jobOwner);
        }

        private void WorkerThread()
        {
            while(!_isShuttingDown)
            {
                if(_pendingOwners.TryDequeue( out IJobOwner jobOwner ))
                {
                    // IJobOwner 인터페이스를 통해 JobQueue에 접근
                    if(jobOwner.JobQueue.TryDequeue( out IJob job ))
                    {
                        try
                        {
                            job.Execute();
                        }
                        catch(Exception ex)
                        {
                            LogManager.Error( "Job Execution Failed", ex );
                        }
                    }

                    if(0 < jobOwner.JobQueue.Count)
                    {
                        _pendingOwners.Enqueue( jobOwner );
                    }
                }
                else
                {
                    // 작업이 없을 때는 CPU 점유율을 낮추기 위해 잠시 대기.
                    Thread.Sleep( 1 );
                }
            }
        }
    }
}
