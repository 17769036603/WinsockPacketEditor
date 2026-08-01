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
        public bool IsPairCombination { get; set; }
        public int PairFirstPosition { get; set; }
        public int PairSecondPosition { get; set; }
        public int PairFirstValueNumber { get; set; }
        public int PairSecondValueNumber { get; set; }
        public int PairFirstValueCount { get; set; }
        public int PairSecondValueCount { get; set; }
        public byte PairFirstValue { get; set; }
        public byte PairSecondValue { get; set; }
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

        public static Socket_ByteSweepResult ExecutePairCombination(
            byte[] baseline,
            int firstPosition,
            int firstLength,
            int firstInterval,
            int secondPosition,
            int secondLength,
            int secondInterval,
            Func<byte[], bool> send,
            CancellationToken cancellationToken,
            Action<Socket_ByteSweepProgress> reportProgress)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (send == null) throw new ArgumentNullException(nameof(send));
            if (firstPosition < 0 || firstPosition >= baseline.Length ||
                secondPosition < 0 || secondPosition >= baseline.Length ||
                firstPosition == secondPosition ||
                firstLength <= 0 || firstLength > 255 ||
                secondLength <= 0 || secondLength > 255 ||
                firstInterval < 0 || secondInterval < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(firstPosition));
            }

            Socket_ByteSweepResult result = new Socket_ByteSweepResult();
            byte[] workingBuffer = (byte[])baseline.Clone();
            byte firstOriginal = baseline[firstPosition];
            byte secondOriginal = baseline[secondPosition];
            DateTime lastProgress = DateTime.MinValue;

            try
            {
                for (int firstValueNumber = 1; firstValueNumber <= firstLength; firstValueNumber++)
                {
                    workingBuffer[firstPosition] = unchecked((byte)(firstOriginal + firstValueNumber));
                    for (int secondValueNumber = 1; secondValueNumber <= secondLength; secondValueNumber++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            result.Cancelled = true;
                            return result;
                        }

                        workingBuffer[secondPosition] = unchecked((byte)(secondOriginal + secondValueNumber));
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
                        if (success) result.Success++; else result.Failure++;
                        DateTime now = DateTime.UtcNow;
                        bool firstSend = firstValueNumber == 1 && secondValueNumber == 1;
                        bool finalSend =
                            firstValueNumber == firstLength &&
                            secondValueNumber == secondLength;
                        if (firstSend || finalSend ||
                            (now - lastProgress).TotalMilliseconds >= 100)
                        {
                            ReportPairProgress(
                                reportProgress,
                                result,
                                firstPosition,
                                secondPosition,
                                firstValueNumber,
                                secondValueNumber,
                                firstLength,
                                secondLength,
                                workingBuffer[firstPosition],
                                workingBuffer[secondPosition]);
                            lastProgress = now;
                        }

                        bool lastSecond = secondValueNumber == secondLength;
                        if (!lastSecond && secondInterval > 0 &&
                            cancellationToken.WaitHandle.WaitOne(secondInterval))
                        {
                            result.Cancelled = true;
                            return result;
                        }
                    }

                    bool lastFirst = firstValueNumber == firstLength;
                    if (!lastFirst && firstInterval > 0 &&
                        cancellationToken.WaitHandle.WaitOne(firstInterval))
                    {
                        result.Cancelled = true;
                        return result;
                    }
                }
            }
            finally
            {
                workingBuffer[firstPosition] = firstOriginal;
                workingBuffer[secondPosition] = secondOriginal;
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

        private static void ReportPairProgress(
            Action<Socket_ByteSweepProgress> reportProgress,
            Socket_ByteSweepResult result,
            int firstPosition,
            int secondPosition,
            int firstValueNumber,
            int secondValueNumber,
            int firstLength,
            int secondLength,
            byte firstValue,
            byte secondValue)
        {
            if (reportProgress == null) return;
            reportProgress(new Socket_ByteSweepProgress
            {
                IsPairCombination = true,
                PairFirstPosition = firstPosition,
                PairSecondPosition = secondPosition,
                PairFirstValueNumber = firstValueNumber,
                PairSecondValueNumber = secondValueNumber,
                PairFirstValueCount = firstLength,
                PairSecondValueCount = secondLength,
                PairFirstValue = firstValue,
                PairSecondValue = secondValue,
                CurrentValue = secondValue,
                Position = secondPosition,
                TotalSend = result.TotalSend,
                Success = result.Success,
                Failure = result.Failure
            });
        }
    }
}
