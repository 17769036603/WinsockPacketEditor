using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib;

namespace ShimenTaskFlowHarness
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                TestFourNonWhitelistTasksBecomeCancel();
                TestWhitelistTaskRemainsSubmit();
                TestMultipleWhitelistTasksRemainRejected();
                TestEmptyTaskListRemainsNone();
                TestFlowDispatchesExactlyOneCancelAction();
                Console.WriteLine("ShimenTaskFlowHarness: PASS (5 tests; no Socket)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ShimenTaskFlowHarness: FAIL: " + ex.Message);
                return 1;
            }
        }

        private static void TestFourNonWhitelistTasksBecomeCancel()
        {
            ShimenTaskFlowDecision decision = ShimenTaskFlowEngine.Decide(
                CreateSnapshot(300004, 200002, 600003, 400024));
            AssertEqual(ShimenTaskFlowAction.Cancel, decision.Action,
                "four non-whitelist tasks must form Cancel");
            AssertEqual(300004, decision.Task.TaskId,
                "the first read task must remain the cancel anchor");
            AssertContains(decision.Reason, "发送取消任务封包",
                "cancel reason must describe the packet action");
        }

        private static void TestWhitelistTaskRemainsSubmit()
        {
            ShimenTaskFlowDecision decision = ShimenTaskFlowEngine.Decide(
                CreateSnapshot(400003));
            AssertEqual(ShimenTaskFlowAction.Submit, decision.Action,
                "one whitelist task must remain Submit");
            AssertEqual(400003, decision.PresetEntry.TaskId,
                "Submit must retain the exact whitelist binding");
        }

        private static void TestMultipleWhitelistTasksRemainRejected()
        {
            ShimenTaskFlowDecision decision = ShimenTaskFlowEngine.Decide(
                CreateSnapshot(400003, 400013));
            AssertEqual(ShimenTaskFlowAction.Rejected, decision.Action,
                "multiple whitelist tasks must remain Rejected");
        }

        private static void TestEmptyTaskListRemainsNone()
        {
            ShimenTaskFlowDecision decision = ShimenTaskFlowEngine.Decide(
                CreateSnapshot());
            AssertEqual(ShimenTaskFlowAction.None, decision.Action,
                "an empty task list must remain None");
        }

        private static void TestFlowDispatchesExactlyOneCancelAction()
        {
            ShimenTaskMemorySnapshot snapshot = CreateSnapshot(
                300004,
                200002,
                600003,
                400024);
            RecordingReader reader = new RecordingReader(snapshot);
            RecordingSender sender = new RecordingSender();
            ShimenTaskFlowResult result = new ShimenTaskFlowRunner().Run(
                reader,
                sender,
                true,
                CancellationToken.None,
                Stopwatch.StartNew());

            Assert(result.Succeeded, "flow must complete the offline cancel dispatch");
            Assert(result.SendAttempted, "flow must attempt the cancel dispatch");
            Assert(result.ActionPerformed, "offline sender must report the action performed");
            AssertEqual(1, sender.SendCount, "flow must dispatch exactly once");
            AssertEqual(ShimenTaskPacketAction.Cancel, sender.LastAction,
                "flow must dispatch Cancel, not Submit or Reclaim");
            AssertEqual(300004, sender.LastTask.TaskId,
                "flow must preserve the cancel anchor");
        }

        private static ShimenTaskMemorySnapshot CreateSnapshot(params int[] taskIds)
        {
            ShimenTaskMemorySnapshot snapshot = new ShimenTaskMemorySnapshot
            {
                IsValid = true,
                ReadOnly = true,
                ActionAuthorized = false,
                OcrUsed = false
            };
            for (int index = 0; index < taskIds.Length; index++)
            {
                snapshot.Tasks.Add(new ShimenTaskMemoryTask
                {
                    Slot = index + 1,
                    TaskId = taskIds[index],
                    CanGiveUpKnown = true,
                    CanGiveUp = false
                });
            }

            return snapshot;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + ": expected " + expected + ", got " + actual);
            }
        }

        private static void AssertContains(string value, string expected, string message)
        {
            if (value == null || value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class RecordingReader : IShimenTaskMemoryReader
        {
            private readonly ShimenTaskMemorySnapshot snapshot;

            public RecordingReader(ShimenTaskMemorySnapshot snapshot)
            {
                this.snapshot = snapshot;
            }

            public Task<ShimenTaskMemoryReadResult> ReadSnapshotAsync(
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new ShimenTaskMemoryReadResult
                {
                    Snapshot = this.snapshot
                });
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingSender : IShimenTaskPacketSender
        {
            public int SendCount { get; private set; }
            public ShimenTaskPacketAction LastAction { get; private set; }
            public ShimenTaskMemoryTask LastTask { get; private set; }

            public bool IsConfigured
            {
                get { return true; }
            }

            public ShimenTaskPacketSendResult Send(
                ShimenTaskPacketAction action,
                ShimenTaskMemoryTask task,
                ShimenTaskPresetEntry presetEntry,
                CancellationToken cancellationToken)
            {
                this.SendCount++;
                this.LastAction = action;
                this.LastTask = task;
                return new ShimenTaskPacketSendResult
                {
                    Succeeded = true,
                    BytesSent = true,
                    SentByteCount = 61
                };
            }
        }
    }
}
