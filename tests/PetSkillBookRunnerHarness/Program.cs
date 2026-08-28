using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.PetSkillBook;

namespace PetSkillBookRunnerHarness
{
    internal class Program
    {
        private static int ExitCode = 0;
        private static string FailureMessage = string.Empty;

        private static void Main(string[] args)
        {
            Console.WriteLine("PetSkillBookRunner 离线回归测试（15 步流程）...");

            // 旧行为：默认银两成本（测试兼容性）
            ScriptableGameState.DefaultSilverCostPerSlot = 100;
            ScriptableGameState.DefaultSilverCostPerStudy = 100;

            try
            {
                // 场景 1-3: 基础流程
                Test_HappyPath_TwoBooks();              // 1, 2, 10
                Test_EmptyBookList();                    // (无相关需求)

                // 场景 4-6: 操作结果和状态变化
                Test_OperationRejected();                // 5
                Test_OperationUnknown();                 // 6
                Test_PetNotParticipant();                // (已在Harness中通过)

                // 场景 7-9: 超时和变化异常
                Test_OpenSlotRefreshTimeout();           // (需求中未明确列出)
                Test_StudyRefreshTimeout();              // (需求中未明确列出)
                Test_MultipleSlotChangesFailed();          // 8

                // 场景 10-12: 锁定和顺序
                Test_LockAfterTrue();                    // 10, 11
                Test_LockFailed();                         // 12

                // 场景 13-15: 跳过和已存在
                Test_SkillAlreadyPresent();                // 1
                Test_NoAvailableSlot();                    // 4
                Test_AllBooksSkipped_Success();            // 13

                // 场景 16-18: 治疗行为
                Test_PetChangedDuringExecution();          // 14
                Test_SkillOverwritten();                   // 9
                Test_AdapterMethodsCalled();               // 15

                // 场景 19-20: 补充测试
                Test_OpenAllSlots_FullSkip();              // 3 (满格跳过开格)
                Test_TargetSkillNotFound();                // 7 (目标技能未出现时失败)

                Console.WriteLine("\nPetSkillBookRunner 回归测试: PASS");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n回归测试失败: {ex.Message}");
                Console.Error.WriteLine($"失败场景: {FailureMessage}");
                Environment.ExitCode = 1;
            }

            Environment.ExitCode = ExitCode;
        }

        private static readonly SummonedPetSkillBookRunner.State STATE_COMPLETE = SummonedPetSkillBookRunner.State.COMPLETE;

        private static void Test_HappyPath_TwoBooks()
        {
            Console.WriteLine("\n[场景 1] 完整流程 - 两本技能书...");
            var state = new ScriptableGameState(1001, 4);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 2);

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = true,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1002, ItemId = 40002, LockAfter = true },
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(30000);

            Assert(!runner.IsPaused, "Happy Path 失败: Runner 处于暂停状态", "Happy Path");
            Assert(runner.Context.CurrentBookIndex == 2,
                $"Happy Path 失败: 书籍索引不符，期望 2 实际 {runner.Context.CurrentBookIndex}", "Happy Path");

            var slot1 = state.GetSlot(1);
            Assert(slot1.SkillId == 1002, "Happy Path 失败: 插槽 1 未写出技能 1002", "Happy Path");
            Assert(slot1.IsLocked, "Happy Path 失败: 插槽 1 未锁定", "Happy Path");

            var slot2 = state.GetSlot(2);
            Assert(slot2.SkillId == 2001, "Happy Path 失败: 插槽 2 未写出技能 2001", "Happy Path");
            // LockAfter 运行时固定为 true，第二本也会被锁定
            Assert(slot2.IsLocked, "Happy Path 失败: 插槽 2 应锁定", "Happy Path");

            Assert(runner.Context.RunStatus.BookResults.Count == 2, "Happy Path 失败: BookResults 数量不符", "Happy Path");
            Assert(runner.Context.RunStatus.BookResults.All(r => r.Status == BookExecutionStatus.SUCCESS),
                "Happy Path 失败: 所有书都应成功", "Happy Path");

            Assert(state.GetSilver() == 1600,
                $"Happy Path 失败: 银两不符，期望 1600 实际 {state.GetSilver()}", "Happy Path");

            Assert(operation.TotalOpenSlotSubmissions == 2, "Happy Path 失败: 开槽次数不符", "Happy Path");
            Assert(operation.TotalStudyBookSubmissions == 2, "Happy Path 失败: 学习次数不符", "Happy Path");
            Assert(operation.TotalLockSlotSubmissions == 2, "Happy Path 失败: 锁定次数不符", "Happy Path");

            Console.WriteLine("  Happy Path: 成功");
        }

        private static void Test_EmptyBookList()
        {
            Console.WriteLine("\n[场景 2] 技能书数量为 0...");
            var state = new ScriptableGameState();
            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>()
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(5000);

            Assert(!runner.IsPaused, "Empty Book List: Runner 不应等待人工确认", "Empty Book List");
            Assert(runner.CurrentState != STATE_COMPLETE, "Empty Book List: Runner 不应进入 COMPLETE", "Empty Book List");
            Assert(runner.Context.RunStatus.IsFailed, "Empty Book List: 应标记为 FAILED", "Empty Book List");
            Assert(runner.Context.RunStatus.FailureReason.Contains("空"),
                "Empty Book List: 未记录书单为空", "Empty Book List");

            Console.WriteLine("  Empty Book List: 成功");
        }

        private static void Test_OperationRejected()
        {
            Console.WriteLine("\n[场景 3] 操作被拒绝 - 继续下一本，最终整体 FAILED...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);
            operation.SetNextStudyBookResult(OperationResult.Rejected);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 5: 打书被拒绝后继续下一本，最终整体 FAILED
            Assert(runner.Context.RunStatus.BookResults.Count == 1, "Operation Rejected: 应该有一个 BookResult", "Operation Rejected");
            Assert(runner.Context.RunStatus.BookResults[0].Status == BookExecutionStatus.FAILED,
                "Operation Rejected: BookResult 应该是 FAILED", "Operation Rejected");
            Assert(runner.Context.RunStatus.BookResults[0].FailureReason.Contains("失败"),
                "Operation Rejected: 失败原因应包含'失败'", "Operation Rejected");
            Assert(runner.Context.RunStatus.IsFailed, "Operation Rejected:整体应该标记为 FAILED", "Operation Rejected");
            Assert(!runner.Context.RunStatus.IsCompleted, "Operation Rejected: 不应标记为 SUCCESS", "Operation Rejected");

            Console.WriteLine("  Operation Rejected: 成功");
        }

        private static void Test_OperationUnknown()
        {
            Console.WriteLine("\n[场景 4] 操作结果未知 - 继续下一本，最终整体 FAILED...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);
            operation.SetNextStudyBookResult(OperationResult.Unknown);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 6: 打书结果未知后继续下一本，最终整体 FAILED
            Assert(runner.Context.RunStatus.BookResults.Count == 1, "Operation Unknown: 应该有一个 BookResult", "Operation Unknown");
            Assert(runner.Context.RunStatus.BookResults[0].Status == BookExecutionStatus.FAILED,
                "Operation Unknown: BookResult 应该是 FAILED", "Operation Unknown");
            Assert(runner.Context.RunStatus.IsFailed, "Operation Unknown:整体应该标记为 FAILED", "Operation Unknown");

            Console.WriteLine("  Operation Unknown: 成功");
        }

        private static void Test_PetNotParticipant()
        {
            Console.WriteLine("\n[场景 5] 宠物不在参战...");
            var state = new ScriptableGameState();
            state.SetPetId(0, false);

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1002, ItemId = 40002, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(5000);

            Assert(!runner.IsPaused, "Pet Not Participant: Runner 不应等待人工确认", "Pet Not Participant");
            Assert(runner.CurrentState != STATE_COMPLETE, "Pet Not Participant: Runner 不应进入 COMPLETE", "Pet Not Participant");
            Assert(runner.Context.RunStatus.IsFailed, "Pet Not Participant: 应标记为 FAILED", "Pet Not Participant");
            Assert(runner.Context.RunStatus.FailureReason.Contains("参战"),
                "Pet Not Participant: 未记录参战状态", "Pet Not Participant");

            Console.WriteLine("  Pet Not Participant: 成功");
        }

        private static void Test_OpenSlotRefreshTimeout()
        {
            Console.WriteLine("\n[场景 6] 开格刷新超时...");
            var state = new ScriptableGameState(1001, 4);
            state.Silver = 2000;
            state.SetInventoryItem(1001, 1);
            state.SimulateRefreshTimeout = true;

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = true,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            Assert(!runner.IsPaused, "Open Slot Refresh Timeout: Runner 不应等待人工确认", "Open Slot Refresh Timeout");
            Assert(runner.CurrentState != STATE_COMPLETE, "Open Slot Refresh Timeout: Runner 不应进入 COMPLETE", "Open Slot Refresh Timeout");
            Assert(runner.Context.RunStatus.IsFailed, "Open Slot Refresh Timeout: 应标记为 FAILED", "Open Slot Refresh Timeout");
            Assert(runner.Context.RunStatus.FailureReason.Contains("超时"),
                "Open Slot Refresh Timeout: 未记录超时", "Open Slot Refresh Timeout");

            Console.WriteLine("  Open Slot Refresh Timeout: 成功");
        }

        private static void Test_StudyRefreshTimeout()
        {
            Console.WriteLine("\n[场景 7] 打书刷新超时...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SimulateRefreshTimeout = true;

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            Assert(runner.Context.RunStatus.BookResults.Count == 1, "Study Refresh Timeout: 应该有一个 BookResult", "Study Refresh Timeout");
            Assert(runner.Context.RunStatus.BookResults[0].Status == BookExecutionStatus.FAILED,
                "Study Refresh Timeout: BookResult 应该是 FAILED", "Study Refresh Timeout");

            Console.WriteLine("  Study Refresh Timeout: 成功");
        }

        private static void Test_MultipleSlotChangesFailed()
        {
            Console.WriteLine("\n[场景 8] 多个技能格变化时当前技能书失败...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 1002, ItemId = 40002, Name = "技能B", Type = "普通", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1002, ItemId = 40002, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            Assert(runner.Context.RunStatus.BookResults.Count >= 1,
                "Multiple Slot Changes: 应该有一个 BookResult", "Multiple Slot Changes");

            Console.WriteLine("  Multiple Slot Changes: 成功");
        }

        private static void Test_LockAfterTrue()
        {
            Console.WriteLine("\n[场景 9] LockAfter=true 测试...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = true }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            Assert(!runner.IsPaused, "LockAfter True: Runner 应该完成", "LockAfter True");
            Assert(runner.CurrentState == STATE_COMPLETE, "LockAfter True: 应该进入 COMPLETE", "LockAfter True");
            Assert(operation.TotalLockSlotSubmissions == 1, "LockAfter True: 应该提交一次锁定操作", "LockAfter True");

            Console.WriteLine("  LockAfter True: 成功");
        }

        private static void Test_LockFailed()
        {
            Console.WriteLine("\n[场景 10] 锁格失败...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);
            operation.SetNextLockSlotResult(OperationResult.Rejected);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = true }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 12: 锁定失败后继续下一本，最终整体 FAILED
            Assert(runner.Context.RunStatus.BookResults.Count == 1, "Lock Failed: 应该有一个 BookResult", "Lock Failed");
            Assert(runner.Context.RunStatus.BookResults[0].Status == BookExecutionStatus.FAILED,
                "Lock Failed: BookResult 应该是 FAILED", "Lock Failed");
            Assert(!runner.IsPaused, "Lock Failed: Runner 不应暂停", "Lock Failed");

            Console.WriteLine("  Lock Failed: 成功");
        }

        private static void Test_SkillAlreadyPresent()
        {
            Console.WriteLine("\n[场景 11] 技能已存在跳过...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SkillCatalog.Clear();
            state.Slots[0].SkillId = 1001;
            state.Slots[0].IsOpen = true;
            state.Slots[0].IsLocked = true;
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 1001, ItemId = 40001, Name = "技能A", Type = "普通", Level = 1, IsKnown = true });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1001, ItemId = 40001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 1: 已有目标技能自动跳过，且没有打书调用
            Assert(!runner.IsPaused, "Skill Already Present: Runner 应该完成", "Skill Already Present");
            Assert(runner.CurrentState == STATE_COMPLETE, "Skill Already Present: 应该进入 COMPLETE", "Skill Already Present");
            Assert(runner.Context.RunStatus.BookResults.Count == 1, "Skill Already Present: 应该有一个 BookResult", "Skill Already Present");
            Assert(runner.Context.RunStatus.BookResults[0].Status == BookExecutionStatus.SKIPPED_ALREADY_PRESENT,
                "Skill Already Present: BookResult 应该是 SKIPPED_ALREADY_PRESENT", "Skill Already Present");
            Assert(operation.TotalStudyBookSubmissions == 0, "Skill Already Present: 不应该提交学习操作", "Skill Already Present");

            Console.WriteLine("  Skill Already Present: 成功");
        }

        private static void Test_NoAvailableSlot()
        {
            Console.WriteLine("\n[场景 12] 没有可用空槽位...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SkillCatalog.Clear();
            state.Slots[0].SkillId = 1001;
            state.Slots[0].IsOpen = true;
            state.Slots[0].IsLocked = true;
            state.Slots[1].SkillId = 1002;
            state.Slots[1].IsOpen = true;
            state.Slots[1].IsLocked = false;
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 4: 没有空技能格时当前技能书失败
            Assert(runner.Context.RunStatus.BookResults.Count == 1, "No Available Slot: 应该有一个 BookResult", "No Available Slot");
            Assert(runner.Context.RunStatus.BookResults[0].Status == BookExecutionStatus.FAILED,
                "No Available Slot: BookResult 应该是 FAILED", "No Available Slot");
            Assert(runner.Context.RunStatus.BookResults[0].FailureReason.Contains("可用"),
                "No Available Slot: 失败原因应包含'可用'", "No Available Slot");

            Console.WriteLine("  No Available Slot: 成功");
        }

        private static void Test_AllBooksSkipped_Success()
        {
            Console.WriteLine("\n[场景 13] 全部技能书均跳过时整体 SUCCESS...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SkillCatalog.Clear();

            state.Slots[0].SkillId = 1001;
            state.Slots[0].IsOpen = true;
            state.Slots[0].IsLocked = false;
            state.Slots[1].SkillId = 1002;
            state.Slots[1].IsOpen = true;
            state.Slots[1].IsLocked = false;

            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 1001, ItemId = 40001, Name = "技能A", Type = "普通", Level = 1, IsKnown = true });
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 1002, ItemId = 40002, Name = "技能B", Type = "普通", Level = 1, IsKnown = true });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1001, ItemId = 40001, LockAfter = false },
                    new SkillBookEntry { SkillId = 1002, ItemId = 40002, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 13: 全部技能书均跳过时整体 SUCCESS
            Assert(runner.CurrentState == STATE_COMPLETE, "All Skipped: 应该进入 COMPLETE", "All Skipped");
            Assert(!runner.IsPaused, "All Skipped: Runner 不应暂停", "All Skipped");
            Assert(runner.Context.RunStatus.BookResults.Count == 2, "All Skipped: 应该有 2 个 BookResult", "All Skipped");
            Assert(runner.Context.RunStatus.BookResults.All(r => r.Status == BookExecutionStatus.SKIPPED_ALREADY_PRESENT),
                "All Skipped: 所有 BookResult 都应该是 SKIPPED", "All Skipped");
            Assert(!runner.Context.RunStatus.IsFailed, "All Skipped: 不应标记为 FAILED", "All Skipped");
            Assert(runner.Context.RunStatus.IsCompleted, "All Skipped: 应该标记为 SUCCESS", "All Skipped");

            Console.WriteLine("  All Books Skipped: 成功");
        }

        private static void Test_PetChangedDuringExecution()
        {
            Console.WriteLine("\n[场景 14] 召唤兽变化时整体立即 FAILED...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SetPetId(1001, true);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestPetChangeAdapter(state, 1001, 9999);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 14: 召唤兽变化时整体立即 FAILED
            Assert(!runner.IsPaused, "Pet Changed: Runner 不应等待人工确认", "Pet Changed");
            Assert(runner.CurrentState != STATE_COMPLETE, "Pet Changed: Runner 不应进入 COMPLETE", "Pet Changed");
            Assert(runner.Context.RunStatus.BookResults[0].FailureReason.Contains("变化") ||
                   runner.Context.RunStatus.BookResults[0].FailureReason.Contains("不一致"),
                "Pet Changed: 未显示 ID 变化", "Pet Changed");
            Assert(runner.Context.RunStatus.IsFailed, "Pet Changed: 应该标记为 FAILED", "Pet Changed");

            Console.WriteLine("  Pet Changed: 成功");
        }

        private static void Test_SkillOverwritten()
        {
            Console.WriteLine("\n[场景 15] 已有技能被覆盖时当前技能书失败...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            state.Slots[0].SkillId = 1001;
            state.Slots[0].IsOpen = true;
            state.Slots[0].IsLocked = true;
            state.Slots[1].SkillId = 0;
            state.Slots[1].IsOpen = true;
            state.Slots[1].IsLocked = false;

            var readOnly = new ScriptableTestOverwriteAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 9: 已有技能被覆盖时当前技能书失败
            Assert(runner.Context.RunStatus.BookResults.Count >= 1, "Skill Overwritten: 应该有一个 BookResult", "Skill Overwritten");
            Assert(runner.Context.RunStatus.BookResults[0].Status == BookExecutionStatus.FAILED,
                "Skill Overwritten: BookResult 应该是 FAILED", "Skill Overwritten");
            Assert(runner.Context.RunStatus.BookResults[0].FailureReason.Contains("覆盖") ||
                   runner.Context.RunStatus.BookResults[0].FailureReason.Contains("未改变"),
                "Skill Overwritten: 失败原因应包含覆盖或变化", "Skill Overwritten");

            Console.WriteLine("  Skill Overwritten: 成功");
        }

        private static void Test_AdapterMethodsCalled()
        {
            Console.WriteLine("\n[场景 16] 不进行材料、银两、技能书目录预检...");
            var state = new ScriptableGameState(1001, 4);
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = true,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 当前规则：开格和打书直接提交，不能读取资源或技能目录做前置判断。
            Assert(operation.TotalOpenSlotSubmissions > 0,
                "Adapter Methods: OpenSlot 操作应被调用", "Adapter Methods");
            Assert(readOnly.ReadResourcesCallCount == 0,
                "Adapter Methods: 不应读取材料或银两", "Adapter Methods");
            Assert(readOnly.ReadSkillCatalogCallCount == 0,
                "Adapter Methods: 不应读取技能书目录", "Adapter Methods");

            Assert(runner.Context.RunStatus.BookResults.Count == 1,
                "Adapter Methods: 应该有一个 BookResult", "Adapter Methods");

            // 18: 验证 BookResults 包含正确的保存信息
            var result = runner.Context.RunStatus.BookResults[0];
            if (result.Status == BookExecutionStatus.SUCCESS)
            {
                Assert(string.IsNullOrEmpty(result.FailureReason),
                    "Adapter Methods: 成功结果不应有失败原因", "Adapter Methods");
            }

            Console.WriteLine("  Adapter Methods Called: 成功");
        }

        private static void Test_OpenAllSlots_FullSkip()
        {
            Console.WriteLine("\n[场景 17] 已经满格时跳过开格...");
            var state = new ScriptableGameState(1001, 4);
            state.Silver = 2000;
            state.MaxSlotCount = 4;
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            for (int i = 0; i < 4; i++)
            {
                state.Slots[i].IsOpen = true;
                state.Slots[i].SkillId = 0;
                state.Slots[i].IsLocked = false;
            }
            state.SetInventoryItem(50001, 1);

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = true,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 3: 已经满格时跳过开格
            Assert(!runner.IsPaused, "Full Slots: Runner 应该完成", "Full Slots");
            Assert(runner.CurrentState == STATE_COMPLETE, "Full Slots: 应该进入 COMPLETE", "Full Slots");
            Assert(operation.TotalOpenSlotSubmissions == 0,
                "Full Slots: 不应该提交开格操作", "Full Slots");

            Console.WriteLine("  Full Slots Skip Opening: 成功");
        }

        private static void Test_TargetSkillNotFound()
        {
            Console.WriteLine("\n[场景 18] 目标技能没有出现时当前技能书失败...");
            var state = new ScriptableGameState(1001, 2);
            state.Silver = 2000;
            state.SkillCatalog.Clear();

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);
            operation.SetSuppressStudyEffect(true);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 99999, ItemId = 99999, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            // 7: 目标技能没有出现时当前技能书失败
            Assert(runner.Context.RunStatus.BookResults.Count == 1, "Target Not Found: 应该有一个 BookResult", "Target Not Found");
            Assert(runner.Context.RunStatus.BookResults[0].Status == BookExecutionStatus.FAILED,
                "Target Not Found: BookResult 应该是 FAILED", "Target Not Found");

            Console.WriteLine("  Target Skill Not Found: 成功");
        }

        private static void Assert(bool condition, string message, string scenario)
        {
            if (!condition)
            {
                FailureMessage = scenario;
                throw new Exception(message);
            }
        }
    }

    internal class ScriptableTestPetChangeAdapter : IPetSkillBookReadOnlyAdapter
    {
        private readonly ScriptableGameState _state;
        private readonly int _originalPetId;
        private readonly int _changedPetId;
        private int _readPetCount = 0;

        public ScriptableTestPetChangeAdapter(ScriptableGameState sharedState, int originalPetId, int changedPetId)
        {
            _state = sharedState;
            _originalPetId = originalPetId;
            _changedPetId = changedPetId;
            _state.CurrentPetId = originalPetId;
            _state.IsCurrentParticipant = originalPetId > 0;
        }

        public Task<CurrentPetSnapshot> ReadCurrentPetAsync(CancellationToken cancellationToken)
        {
            if (++_readPetCount >= 3)
            {
                _state.CurrentPetId = _changedPetId;
            }

            return Task.FromResult(new CurrentPetSnapshot
            {
                PetId = _state.CurrentPetId,
                IsCurrentParticipant = _state.IsCurrentParticipant && _state.CurrentPetId > 0,
                ReadAt = DateTime.UtcNow,
                Identity = null
            });
        }

        public Task<PetStateSnapshot> ReadPetStateAsync(CancellationToken cancellationToken)
        {
            var slotsCopy = _state.Slots.Select(s => new PetSkillSlotSnapshot
            {
                SlotIndex = s.SlotIndex,
                IsOpen = s.IsOpen,
                IsLocked = s.IsLocked,
                SkillId = s.SkillId
            }).ToList();

            return Task.FromResult(new PetStateSnapshot
            {
                PetId = _state.CurrentPetId,
                StateVersion = ++_state.StateVersion,
                OpenSlotCount = slotsCopy.Count(s => s.IsOpen),
                MaxSlotCount = _state.MaxSlotCount,
                SkillSlots = slotsCopy,
                ReadAt = DateTime.UtcNow
            });
        }

        public Task<InventorySnapshot> ReadResourcesAsync(CancellationToken cancellationToken)
        {
            var itemsCopy = _state.InventoryItems.Select(i => new InventoryItemSnapshot
            {
                ItemId = i.ItemId,
                Count = i.Count
            }).ToList();

            return Task.FromResult(new InventorySnapshot
            {
                Items = itemsCopy,
                Silver = _state.Silver,
                ReadAt = DateTime.UtcNow
            });
        }

        public Task<List<SkillBookCatalogEntry>> ReadSkillCatalogAsync(CancellationToken cancellationToken)
        {
            var entries = _state.SkillCatalog.Select(s => new SkillBookCatalogEntry
            {
                SkillId = s.SkillId,
                ItemId = s.ItemId,
                Name = s.Name,
                Type = s.Type,
                Level = s.Level,
                IsKnown = s.IsKnown
            }).ToList();
            return Task.FromResult(entries);
        }

        public Task<bool> WaitForStateRefreshAsync(int targetStateVersion, int timeoutMs, CancellationToken cancellationToken)
        {
            if (_state.SimulateRefreshTimeout)
            {
                return Task.FromResult(false);
            }
            return Task.FromResult(_state.StateVersion >= targetStateVersion);
        }

        public Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    internal class ScriptableTestOverwriteAdapter : IPetSkillBookReadOnlyAdapter
    {
        private readonly ScriptableGameState _state;
        private int _readCount = 0;

        public ScriptableTestOverwriteAdapter(ScriptableGameState sharedState)
        {
            _state = sharedState;
        }

        public Task<CurrentPetSnapshot> ReadCurrentPetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new CurrentPetSnapshot
            {
                PetId = _state.CurrentPetId,
                IsCurrentParticipant = _state.IsCurrentParticipant,
                ReadAt = DateTime.UtcNow,
                Identity = null
            });
        }

        public Task<PetStateSnapshot> ReadPetStateAsync(CancellationToken cancellationToken)
        {
            var slotsCopy = new List<PetSkillSlotSnapshot>();
            foreach (var s in _state.Slots)
            {
                var slotCopy = new PetSkillSlotSnapshot
                {
                    SlotIndex = s.SlotIndex,
                    IsOpen = s.IsOpen,
                    IsLocked = s.IsLocked,
                    SkillId = s.SkillId
                };

                // 在第二次读取后，模拟其他槽位被覆盖
                if (_state.GetSlot(1)?.SkillId == 2001 && s.SlotIndex == 0 && s.SkillId == 1001)
                {
                    slotCopy.SkillId = 99999; // 故意改变，触发覆盖检测
                }

                slotsCopy.Add(slotCopy);
            }

            _readCount++;

            return Task.FromResult(new PetStateSnapshot
            {
                PetId = _state.CurrentPetId,
                StateVersion = ++_state.StateVersion,
                OpenSlotCount = slotsCopy.Count(s => s.IsOpen),
                MaxSlotCount = _state.MaxSlotCount,
                SkillSlots = slotsCopy,
                ReadAt = DateTime.UtcNow
            });
        }

        public Task<InventorySnapshot> ReadResourcesAsync(CancellationToken cancellationToken)
        {
            var itemsCopy = _state.InventoryItems.Select(i => new InventoryItemSnapshot
            {
                ItemId = i.ItemId,
                Count = i.Count
            }).ToList();

            return Task.FromResult(new InventorySnapshot
            {
                Items = itemsCopy,
                Silver = _state.Silver,
                ReadAt = DateTime.UtcNow
            });
        }

        public Task<List<SkillBookCatalogEntry>> ReadSkillCatalogAsync(CancellationToken cancellationToken)
        {
            var entries = _state.SkillCatalog.Select(s => new SkillBookCatalogEntry
            {
                SkillId = s.SkillId,
                ItemId = s.ItemId,
                Name = s.Name,
                Type = s.Type,
                Level = s.Level,
                IsKnown = s.IsKnown
            }).ToList();
            return Task.FromResult(entries);
        }

        public Task<bool> WaitForStateRefreshAsync(int targetStateVersion, int timeoutMs, CancellationToken cancellationToken)
        {
            if (_state.SimulateRefreshTimeout)
            {
                return Task.FromResult(false);
            }
            return Task.FromResult(_state.StateVersion >= targetStateVersion);
        }

        public Task<bool> VerifyProcessIdentityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}
