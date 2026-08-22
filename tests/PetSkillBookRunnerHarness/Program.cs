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
            Console.WriteLine("PetSkillBookRunner 离线回归测试...");

            // 设置默认银两成本（模拟测试配置）
            ScriptableGameState.DefaultSilverCostPerSlot = 100;
            ScriptableGameState.DefaultSilverCostPerStudy = 100;

            try
            {
                // 场景 1-5: 基础流程
                Test_HappyPath_TwoBooks();
                Test_MissingBookItem();
                Test_EmptyBookList();
                Test_MissingOpenMaterial();
                Test_SilverInsufficient();

                // 场景 6-10: 操作结果和状态变化
                Test_OperationRejected();
                Test_OperationUnknown();
                Test_PetNotParticipant();
                Test_PetIdChanged();
                Test_ProcessIdentityFailed();

                // 场景 11-15: 超时和变化异常
                Test_OpenSlotRefreshTimeout();
                Test_StudyRefreshTimeout();
                Test_ZeroSkillDiff();
                Test_MultipleSlotChanges();

                // 场景 16-19: 锁定和顺序
                Test_LockFailed();
                Test_LockRefreshTimeout();
                Test_LockVerifyFailed();
                Test_LockAfterFalse();
                Test_SequentialTwoBooks();

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
        private static readonly SummonedPetSkillBookRunner.State STATE_NEXT_BOOK = SummonedPetSkillBookRunner.State.NEXT_BOOK;

        private static void Test_HappyPath_TwoBooks()
        {
            Console.WriteLine("\n[场景 1] 完整流程 - 两本技能书...");
            var state = new ScriptableGameState();
            state.Silver = 2000;
            state.SetInventoryItem(50001, 2);

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = true,
                OpenItemId = 1001,
                OpenSlotSilverCost = 100,
                StudySilverCost = 100,
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
            Assert(!slot2.IsLocked, "Happy Path 失败: 插槽 2 应不锁定", "Happy Path");

            Assert(state.GetSilver() == 1600,
                $"Happy Path 失败: 银两不符，期望 1600 实际 {state.GetSilver()}", "Happy Path");

            Assert(operation.TotalOpenSlotSubmissions == 2, "Happy Path 失败: 开槽次数不符", "Happy Path");
            Assert(operation.TotalStudyBookSubmissions == 2, "Happy Path 失败: 学习次数不符", "Happy Path");
            Assert(operation.TotalLockSlotSubmissions == 1, "Happy Path 失败: 锁定次数不符", "Happy Path");

            Console.WriteLine("  Happy Path: 成功");
        }

        private static void Test_MissingBookItem()
        {
            Console.WriteLine("\n[场景 2] 缺少技能书物品...");
            var state = new ScriptableGameState();
            state.Silver = 2000;
            state.SkillCatalog.Clear(); // 清除默认目录，确保技能书不在目录中

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1002, ItemId = 99999, LockAfter = true }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            Assert(runner.IsPaused, "Missing Book: Runner 应暂停", "Missing Book");
            Assert(runner.CurrentState != STATE_COMPLETE, "Missing Book: Runner 不应进入 COMPLETE", "Missing Book");
            Assert(runner.Context.PauseReason.Contains("99999"),
                $"Missing Book: 暂停原因未指向缺失物品 ID: {runner.Context.PauseReason}", "Missing Book");

            Console.WriteLine("  Missing Book: 成功");
        }

        private static void Test_EmptyBookList()
        {
            Console.WriteLine("\n[场景 3] 技能书数量为 0...");
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

            Assert(runner.IsPaused, "Empty Book List: Runner 应暂停", "Empty Book List");
            Assert(runner.CurrentState != STATE_COMPLETE, "Empty Book List: Runner 不应进入 COMPLETE", "Empty Book List");
            Assert(runner.Context.PauseReason.Contains("空"),
                "Empty Book List: 未显示书单为空", "Empty Book List");

            Console.WriteLine("  Empty Book List: 成功");
        }

        private static void Test_MissingOpenMaterial()
        {
            Console.WriteLine("\n[场景 4] 缺少开格材料...");
            var state = new ScriptableGameState();
            state.Silver = 2000;
            // 清除默认背包中的所有物品，然后只添加需要的
            state.InventoryItems.Clear();
            state.SkillCatalog.Clear();
            // 添加技能书40002到目录
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 1002, ItemId = 40002, Name = "技能B", Type = "普通", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = true,
                OpenItemId = 1001, // 但背包中没有此物品
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1002, ItemId = 40002, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            Assert(runner.IsPaused, "Missing Open Material: Runner 应暂停", "Missing Open Material");
            Assert(runner.CurrentState != STATE_COMPLETE, "Missing Open Material: Runner 不应进入 COMPLETE", "Missing Open Material");

            Console.WriteLine("  Missing Open Material: 成功");
        }

        private static void Test_SilverInsufficient()
        {
            Console.WriteLine("\n[场景 5] 银两不足...");
            var state = new ScriptableGameState();
            state.Silver = 50; // 仅50银，明显不足
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = false,
                OpenItemId = 1001,
                StudySilverCost = 100,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(5000);

            Assert(runner.IsPaused, "Silver Insufficient: Runner 应暂停", "Silver Insufficient");
            Assert(runner.CurrentState != STATE_COMPLETE, "Silver Insufficient: Runner 不应进入 COMPLETE", "Silver Insufficient");
            Assert(runner.Context.PauseReason.Contains("银两不足"),
                $"Silver Insufficient: 未显示银两不足: {runner.Context.PauseReason}", "Silver Insufficient");

            Console.WriteLine("  Silver Insufficient: 成功");
        }

        private static void Test_OperationRejected()
        {
            Console.WriteLine("\n[场景 6] 操作被拒绝...");
            var state = new ScriptableGameState();
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

            Assert(runner.IsPaused, "Operation Rejected: Runner 应暂停", "Operation Rejected");
            Assert(runner.CurrentState != STATE_COMPLETE, "Operation Rejected: Runner 不应进入 COMPLETE", "Operation Rejected");
            Assert(runner.Context.PauseReason.Contains("失败"),
                "Operation Rejected: 未显示操作失败", "Operation Rejected");

            Console.WriteLine("  Operation Rejected: 成功");
        }

        private static void Test_OperationUnknown()
        {
            Console.WriteLine("\n[场景 7] 操作结果未知...");
            var state = new ScriptableGameState();
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

            Assert(runner.IsPaused, "Operation Unknown: Runner 应暂停", "Operation Unknown");
            Assert(runner.CurrentState != STATE_COMPLETE, "Operation Unknown: Runner 不应进入 COMPLETE", "Operation Unknown");

            Console.WriteLine("  Operation Unknown: 成功");
        }

        private static void Test_PetNotParticipant()
        {
            Console.WriteLine("\n[场景 8] 宠物不在参战...");
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

            Assert(runner.IsPaused, "Pet Not Participant: Runner 应暂停", "Pet Not Participant");
            Assert(runner.CurrentState != STATE_COMPLETE, "Pet Not Participant: Runner 不应进入 COMPLETE", "Pet Not Participant");
            Assert(runner.Context.PauseReason.Contains("参战"),
                "Pet Not Participant: 未显示参战状态", "Pet Not Participant");

            Console.WriteLine("  Pet Not Participant: 成功");
        }

        private static void Test_PetIdChanged()
        {
            Console.WriteLine("\n[场景 9] 宠物 ID 变化...");
            var state = new ScriptableGameState();
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

            Assert(runner.IsPaused, "Pet Id Changed: Runner 应暂停", "Pet Id Changed");
            Assert(runner.CurrentState != STATE_COMPLETE, "Pet Id Changed: Runner 不应进入 COMPLETE", "Pet Id Changed");
            Assert(runner.Context.PauseReason.Contains("变化"),
                "Pet Id Changed: 未显示ID变化", "Pet Id Changed");

            Console.WriteLine("  Pet Id Changed: 成功");
        }

        private static void Test_ProcessIdentityFailed()
        {
            Console.WriteLine("\n[场景 10] 进程身份验证失败...");
            var state = new ScriptableGameState();
            var readOnly = new ScriptableTestIdentityAdapter(state);
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

            Assert(runner.IsPaused, "Process Identity Failed: Runner 应暂停", "Process Identity Failed");
            Assert(runner.CurrentState != STATE_COMPLETE, "Process Identity Failed: Runner 不应进入 COMPLETE", "Process Identity Failed");

            Console.WriteLine("  Process Identity Failed: 成功");
        }

        private static void Test_OpenSlotRefreshTimeout()
        {
            Console.WriteLine("\n[场景 11] 开格刷新超时...");
            var state = new ScriptableGameState();
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
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

            Assert(runner.IsPaused, "Open Slot Refresh Timeout: Runner 应暂停", "Open Slot Refresh Timeout");
            Assert(runner.CurrentState != STATE_COMPLETE, "Open Slot Refresh Timeout: Runner 不应进入 COMPLETE", "Open Slot Refresh Timeout");
            Assert(runner.Context.PauseReason.Contains("超时"),
                "Open Slot Refresh Timeout: 未显示超时", "Open Slot Refresh Timeout");

            Console.WriteLine("  Open Slot Refresh Timeout: 成功");
        }

        private static void Test_StudyRefreshTimeout()
        {
            Console.WriteLine("\n[场景 12] 打书刷新超时...");
            var state = new ScriptableGameState();
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

            Assert(runner.IsPaused, "Study Refresh Timeout: Runner 应暂停", "Study Refresh Timeout");
            Assert(runner.CurrentState != STATE_COMPLETE, "Study Refresh Timeout: Runner 不应进入 COMPLETE", "Study Refresh Timeout");

            Console.WriteLine("  Study Refresh Timeout: 成功");
        }

        private static void Test_ZeroSkillDiff()
        {
            Console.WriteLine("\n[场景 13] 技能差异为 0...");
            var state = new ScriptableGameState();
            state.Silver = 2000;
            // 已有技能1001在槽位0
            state.Slots[0].SkillId = 1001;
            state.Slots[0].IsOpen = true;
            state.Slots[0].IsLocked = false;

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

            Assert(runner.IsPaused, "Zero Skill Diff: Runner 应暂停", "Zero Skill Diff");
            Assert(runner.CurrentState != STATE_COMPLETE, "Zero Skill Diff: Runner 不应进入 COMPLETE", "Zero Skill Diff");
            Assert(runner.Context.PauseReason.Contains("数量不正确") || runner.Context.PauseReason.Contains("0"),
                "Zero Skill Diff: 未显示技能差异为0", "Zero Skill Diff");

            Console.WriteLine("  Zero Skill Diff: 成功");
        }

        private static void Test_MultipleSlotChanges()
        {
            Console.WriteLine("\n[场景 14] 多个技能格发生变化...");
            var state = new ScriptableGameState();
            state.Silver = 2000;
            state.SkillCatalog.Clear();
            // 添加两个技能书
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 1002, ItemId = 40002, Name = "技能B", Type = "普通", Level = 1, IsKnown = false });
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = true,
                OpenItemId = 1001,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1002, ItemId = 40002, LockAfter = false },
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(15000);

            Assert(runner.IsPaused, "Multiple Slot Changes: Runner 应暂停", "Multiple Slot Changes");
            Assert(runner.CurrentState != STATE_COMPLETE, "Multiple Slot Changes: Runner 不应进入 COMPLETE", "Multiple Slot Changes");

            Console.WriteLine("  Multiple Slot Changes: 成功");
        }

        private static void Test_LockFailed()
        {
            Console.WriteLine("\n[场景 15] 锁格失败...");
            var state = new ScriptableGameState();
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

            Assert(runner.IsPaused, "Lock Failed: Runner 应暂停", "Lock Failed");
            Assert(runner.CurrentState != STATE_COMPLETE, "Lock Failed: Runner 不应进入 COMPLETE", "Lock Failed");
            Assert(runner.Context.PauseReason.Contains("锁定"),
                "Lock Failed: 未显示锁定失败", "Lock Failed");

            Console.WriteLine("  Lock Failed: 成功");
        }

        private static void Test_LockRefreshTimeout()
        {
            Console.WriteLine("\n[场景 16] 锁格刷新超时...");
            var state = new ScriptableGameState();
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SimulateRefreshTimeout = true;
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

            Assert(runner.IsPaused, "Lock Refresh Timeout: Runner 应暂停", "Lock Refresh Timeout");
            Assert(runner.CurrentState != STATE_COMPLETE, "Lock Refresh Timeout: Runner 不应进入 COMPLETE", "Lock Refresh Timeout");

            Console.WriteLine("  Lock Refresh Timeout: 成功");
        }

        private static void Test_LockVerifyFailed()
        {
            Console.WriteLine("\n[场景 17] 锁格验证失败...");
            var state = new ScriptableGameState();
            state.Silver = 2000;
            state.SetInventoryItem(50001, 1);
            state.SkillCatalog.Clear();
            state.SkillCatalog.Add(new ScriptedSkillEntry { SkillId = 2001, ItemId = 50001, Name = "技能C", Type = "高级", Level = 1, IsKnown = false });

            var operation = new ScriptableTestOperationAdapter(state);
            var readOnly = new ScriptableTestLockVerifyFailAdapter(state, operation);

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

            Assert(runner.IsPaused, "Lock Verify Failed: Runner 应暂停", "Lock Verify Failed");
            Assert(runner.CurrentState != STATE_COMPLETE, "Lock Verify Failed: Runner 不应进入 COMPLETE", "Lock Verify Failed");
            Assert(runner.Context.PauseReason.Contains("锁定未生效"),
                "Lock Verify Failed: 未显示锁定未生效", "Lock Verify Failed");

            Console.WriteLine("  Lock Verify Failed: 成功");
        }

        private static void Test_LockAfterFalse()
        {
            Console.WriteLine("\n[场景 18] LockAfter=false 测试...");
            var state = new ScriptableGameState();
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
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            var task = runner.StartAsync();
            task.Wait(10000);

            Assert(!runner.IsPaused, "LockAfter False: Runner 应该完成", "LockAfter False");
            Assert(runner.CurrentState == STATE_COMPLETE, "LockAfter False: 应该进入 COMPLETE", "LockAfter False");
            Assert(operation.TotalLockSlotSubmissions == 0, "LockAfter False: 不应该提交锁定操作", "LockAfter False");

            Console.WriteLine("  LockAfter False: 成功");
        }

        private static void Test_SequentialTwoBooks()
        {
            Console.WriteLine("\n[场景 19] 两本技能书按顺序执行...");
            var state = new ScriptableGameState();
            state.Silver = 3000;
            state.SetInventoryItem(50001, 2);

            var readOnly = new ScriptableTestReadOnlyAdapter(state);
            var operation = new ScriptableTestOperationAdapter(state);

            var preset = new SummonedPetSkillBookPreset
            {
                OpenAllSlots = true,
                OpenItemId = 1001,
                OpenSlotSilverCost = 100,
                StudySilverCost = 100,
                Books = new List<SkillBookEntry>
                {
                    new SkillBookEntry { SkillId = 1002, ItemId = 40002, LockAfter = true },
                    new SkillBookEntry { SkillId = 2001, ItemId = 50001, LockAfter = false }
                }
            };

            var runner = new SummonedPetSkillBookRunner(preset, readOnly, operation);
            runner.OnLog += (s, m) => Console.WriteLine($"  [{s}] {m}");

            // 监控状态日志顺序
            var stateLogOrder = new List<SummonedPetSkillBookRunner.State>();
            runner.OnStateChanged += s => stateLogOrder.Add(s);

            var task = runner.StartAsync();
            task.Wait(30000);

            Assert(!runner.IsPaused, "Sequential: Runner 应该完成", "Sequential");
            Assert(runner.Context.CurrentBookIndex == 2, "Sequential: 应该完成两本书", "Sequential");
            Assert(runner.CurrentState == STATE_COMPLETE, "Sequential: 应该进入 COMPLETE", "Sequential");

            // 验证 NEXT_BOOK 在 COMPLETE 之前
            var completeIndex = stateLogOrder.IndexOf(STATE_COMPLETE);
            var nextBookIndex = stateLogOrder.IndexOf(STATE_NEXT_BOOK);
            Assert(nextBookIndex >= 0 && completeIndex >= 0 && nextBookIndex < completeIndex,
                "Sequential: NEXT_BOOK 应该在 COMPLETE 之前", "Sequential");

            Console.WriteLine("  Sequential Two Books: 成功");
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

    /// <summary>
    /// 测试专用的 Identity 适配器，模拟身份验证失败
    /// </summary>
    internal class ScriptableTestIdentityAdapter : IPetSkillBookReadOnlyAdapter
    {
        private readonly ScriptableGameState _state;

        public ScriptableTestIdentityAdapter(ScriptableGameState sharedState = null)
        {
            _state = sharedState ?? new ScriptableGameState();
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
                StateVersion = _state.StateVersion,
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
            // 模拟身份验证失败
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 测试专用的 Read Adapter，模拟宠物 ID 在特定次数后发生变化
    /// </summary>
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
            // 在读取宠物第 3 次后返回变化后的 ID
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

    /// <summary>
    /// 测试专用的 Lock Verify Fail Adapter
    /// 在锁定操作成功后，返回未锁定的状态以触发验证失败
    /// </summary>
    internal class ScriptableTestLockVerifyFailAdapter : IPetSkillBookReadOnlyAdapter
    {
        private readonly ScriptableGameState _state;
        private readonly ScriptableTestOperationAdapter _operationAdapter;
        private int _lockAttempts = 0;

        public ScriptableTestLockVerifyFailAdapter(ScriptableGameState sharedState, ScriptableTestOperationAdapter operationAdapter)
        {
            _state = sharedState;
            _operationAdapter = operationAdapter;
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
                    SkillId = s.SkillId
                };

                // 模拟锁定验证失败：在第一次读取状态后，返回未锁定的槽位
                if (_lockAttempts > 0 && _state.CurrentPetId == 1001 && s.SlotIndex == 1)
                {
                    slotCopy.IsLocked = false; // 故意不锁定
                }
                else
                {
                    slotCopy.IsLocked = s.IsLocked;
                }

                slotsCopy.Add(slotCopy);
            }

            _lockAttempts++;

            return Task.FromResult(new PetStateSnapshot
            {
                PetId = _state.CurrentPetId,
                StateVersion = _state.StateVersion,
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
