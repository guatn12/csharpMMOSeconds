using ServerCore;
using Microsoft.Extensions.Logging;

namespace Server.Room
{
    /// <summary>
    /// IRoom 확장 메서드 - 인프라 관심사를 인터페이스에서 분리
    /// </summary>
    public static class RoomExtensions
    {
        /// <summary>
        /// 스레드 안전한 Job 추가 및 WorkerManager 통지
        /// BaseRoom 구현체는 최적화된 방법을 사용하고, 다른 구현체는 기본 방법을 사용
        /// </summary>
        /// <param name="room">대상 룸</param>
        /// <param name="job">추가할 Job</param>
        /// <returns>Job이 성공적으로 추가되었는지 여부</returns>
        public static bool TryEnqueueJobSafely(this IRoom room, IJob job)
        {
            if (job == null || room == null)
                return false;

            // BaseRoom 구현체인 경우 최적화된 메서드 사용
            if (room is BaseRoom baseRoom)
            {
                return baseRoom.TryEnqueueJobSafely(job);
            }

            // 다른 IRoom 구현체를 위한 폴백 로직
            // Race Condition 가능성은 있지만 기본적인 동작은 보장
            try
            {
                room.JobQueue.Enqueue(job);
                _ = JobQueueManager.Instance.PushAsync(room);
                return true;
            }
            catch
            {
                // 예외 발생 시 실패 처리
                return false;
            }
        }
    }
}