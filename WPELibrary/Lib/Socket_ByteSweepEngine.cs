using System;
using System.Threading;

namespace WPELibrary.Lib
{
    public sealed class Socket_ByteSweepProgress
    {
        public int Position { get; set; }
        public byte OriginalValue { get; set; }
        public byte CurrentValue { get; set; }
        public int ByteNumber { get; set; }
        public int ByteCount { get; set; }
        public int ValueNumber { get; set; }
        public long TotalSend { get; set; }
        public long Success { get; set; }
        public long Failure { get; set; }
    }

    public sealed class Socket_ByteSweepResult
    {
        public bool Cancelled { get; set; }
        public long TotalSend { get; set; }
        public long Success { get; set; }
        public long Failure { get; set; }
    }

    public static class Socket_ByteSweepEngine
    {
        public static Socket_ByteSweepResult Execute(
            byte[] baseline,
            int start,
            int length,
            int interval,
            Func<byte[], bool> send,
            CancellationToken cancellationToken,
            Action<Socket_ByteSweepProgress> reportProgress)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException(nameof(baseline));
            }

            if (send == null)
            {
                throw new ArgumentNullException(nameof(send));
            }

            if (start < 0 || length <= 0 || start >= baseline.Length || length > baseline.Length - start)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "递进范围超出封包长度。");
            }

            if (interval < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            Socket_ByteSweepResult result = new Socket_ByteSweepResult();
            byte[] workingBuffer = (byte[])baseline.Clone();
            int end = start + length;
            DateTime lastProgress = DateTime.MinValue;

            for (int position = start; position < end; position++)
            {
                byte originalValue = baseline[position];
                int byteNumber = position - start + 1;
                try
                {
                    for (int valueNumber = 1; valueNumber <= 255; valueNumber++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            result.Cancelled = true;
                            return result;
                        }

                        byte currentValue = unchecked((byte)(originalValue + valueNumber));
                        workingBuffer[position] = currentValue;

                        bool success = false;
                        try
                        {
                            success = send(workingBuffer);
                        }
                        catch (Exception ex)
                        {
                            Socket_Operation.DoLog(nameof(Socket_ByteSweepEngine), ex.Message);
                        }

                        result.TotalSend++;
                        if (success)
                        {
                            result.Success++;
                        }
                        else
                        {
                            result.Failure++;
                        }

                        DateTime now = DateTime.UtcNow;
                        if (valueNumber == 1 ||
                            valueNumber == 255 ||
                            (now - lastProgress).TotalMilliseconds >= 100)
                        {
                            Report(reportProgress, result, position, originalValue, currentValue, byteNumber, length, valueNumber);
                            lastProgress = now;
                        }

                        bool lastSend = position == end - 1 && valueNumber == 255;
                        if (!lastSend && interval > 0 && cancellationToken.WaitHandle.WaitOne(interval))
                        {
                            result.Cancelled = true;
                            return result;
                        }
                    }
                }
                finally
                {
                    workingBuffer[position] = originalValue;
                }
            }

            return result;
        }

        private static void Report(
            Action<Socket_ByteSweepProgress> reportProgress,
            Socket_ByteSweepResult result,
            int position,
            byte originalValue,
            byte currentValue,
            int byteNumber,
            int byteCount,
            int valueNumber)
        {
            if (reportProgress == null)
            {
                return;
            }

            reportProgress(new Socket_ByteSweepProgress
            {
                Position = position,
                OriginalValue = originalValue,
                CurrentValue = currentValue,
                ByteNumber = byteNumber,
                ByteCount = byteCount,
                ValueNumber = valueNumber,
                TotalSend = result.TotalSend,
                Success = result.Success,
                Failure = result.Failure
            });
        }
    }
}
