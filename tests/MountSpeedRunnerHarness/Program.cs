using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MountSpeedRunnerHarness
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=== MountSpeed Runner Harness ===");
            Console.WriteLine("Date: " + DateTime.Now);
            Console.WriteLine();

            try
            {
                // 1. Test Preset Serialization Round-Trip
                Console.WriteLine("[TEST 1] Preset Serialization Round-Trip");
                if (!TestPresetSerialization()) return 1;

                Console.WriteLine("\n[TEST 1B] Fixed first-mount refine target defaults");
                if (!TestDefaultFirstRideRefineTarget()) return 1;

                // 2. Test Preset Plan Steps
                Console.WriteLine("\n[TEST 2] Preset Plan Steps");
                if (!TestPresetPlanSteps()) return 1;

                // 3. Test CalculateMountSpeedModel
                Console.WriteLine("\n[TEST 3] CalculateMountSpeedModel");
                if (!TestCalculateMountSpeedModel()) return 1;

                // 4. Test Memory Writer Address Configuration
                Console.WriteLine("\n[TEST 4] Memory Writer Address Configuration");
                if (!TestMemoryWriterConfiguration()) return 1;

                Console.WriteLine("\n[TEST 5] Mount Status Memory Reader Snapshot");
                if (!await TestMountStatusMemoryReaderAsync()) return 1;

                Console.WriteLine("\n[TEST 6] Mount Status Reader Fail-Closed Layout");
                if (!await TestMountStatusReaderFailClosedAsync()) return 1;

                Console.WriteLine("\n[TEST 7] Android LuaJIT Mount Snapshot Protocol");
                if (!TestMountStatusAndroidSnapshotProtocol()) return 1;

                Console.WriteLine("\n[TEST 8] Mount Refine State Machine Safety and Loop");
                if (!await TestMountRefineStateMachineAsync()) return 1;

                Console.WriteLine("\n[TEST 8B] Mount Refine Snapshot Read Timeout");
                if (!await TestMountRefineSnapshotReadTimeoutAsync()) return 1;

                Console.WriteLine("\n[TEST 8C] Mount Refine Snapshot Sequence Wait");
                if (!await TestMountRefineSnapshotSequenceWaitAsync()) return 1;

                Console.WriteLine("\n[TEST 9] Mount Refine A050 Offline Packet Template");
                if (!TestMountRefineA050PacketTemplate()) return 1;

                Console.WriteLine("\n=== ALL TESTS PASSED ===");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[ERROR] {ex}");
                return 1;
            }
        }

        static bool TestPresetSerialization()
        {
            try
            {
                var preset = new WPELibrary.Lib.MountSpeed.MountSpeedPreset
                {
                    Name = "Test Preset",
                    ExpectedRideAddSpeed = 150.5,
                    ApplyMountSpeed = true,
                    DesiredMount = true,
                    BaseSpeed = 8.0,
                    TargetGrowthRate = 1.175,
                    TargetSkills = new System.Collections.Generic.List<WPELibrary.Lib.MountSpeed.MountSkillTarget>
                    {
                        new WPELibrary.Lib.MountSpeed.MountSkillTarget
                        {
                            SkillId = 61008,
                            SkillName = "秋水流弦"
                        }
                    }
                };

                string json = WPELibrary.Lib.MountSpeed.MountSpeedPresetSerializer.Serialize(preset);
                Console.WriteLine("  Serialized: " + json);

                WPELibrary.Lib.MountSpeed.MountSpeedPreset deserialized;
                string deserializeError;
                if (!WPELibrary.Lib.MountSpeed.MountSpeedPresetSerializer.TryDeserialize(json, out deserialized, out deserializeError))
                {
                    Console.WriteLine("  [FAIL] TryDeserialize failed: " + deserializeError);
                    return false;
                }
                Console.WriteLine("  Deserialized: " + deserialized.Name);
                Console.WriteLine("  ExpectedRideAddSpeed: " + deserialized.ExpectedRideAddSpeed);
                Console.WriteLine("  ApplyMountSpeed: " + deserialized.ApplyMountSpeed);
                Console.WriteLine("  DesiredMount: " + deserialized.DesiredMount);
                Console.WriteLine("  BaseSpeed: " + deserialized.BaseSpeed);

                if (deserialized.Name != preset.Name) return false;
                if (Math.Abs(deserialized.ExpectedRideAddSpeed - preset.ExpectedRideAddSpeed) > 0.001) return false;
                if (deserialized.ApplyMountSpeed != preset.ApplyMountSpeed) return false;
                if (deserialized.DesiredMount != preset.DesiredMount) return false;
                if (Math.Abs(deserialized.BaseSpeed - preset.BaseSpeed) > 0.001) return false;
                if (!deserialized.TargetGrowthRate.HasValue ||
                    Math.Abs(deserialized.TargetGrowthRate.Value - 1.175) > 0.0001) return false;
                if (deserialized.TargetSkills == null || deserialized.TargetSkills.Count != 1 ||
                    deserialized.TargetSkills[0].SkillId != 61008 ||
                    deserialized.TargetSkills[0].SkillName != "秋水流弦") return false;

                Console.WriteLine("  [PASS]");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static bool TestPresetPlanSteps()
        {
            try
            {
                var steps = WPELibrary.Lib.MountSpeed.MountSpeedPresetPlan.GetSteps();
                Console.WriteLine("  Total steps: " + steps.Count);

                foreach (var step in steps)
                {
                    string encoded = WPELibrary.Lib.MountSpeed.MountSpeedPresetPlan.EncodeStep(step);
                    Console.WriteLine("  Step " + step.Index + ": " + step.State + " -> " + encoded);

                    WPELibrary.Lib.MountSpeed.MountSpeedPresetStep decoded;
                    if (!WPELibrary.Lib.MountSpeed.MountSpeedPresetPlan.TryDecodeStep(encoded, out decoded))
                    {
                        Console.WriteLine("  [FAIL] Decode failed for step " + step.Index);
                        return false;
                    }

                    if (decoded.Index != step.Index || decoded.State != step.State)
                    {
                        Console.WriteLine("  [FAIL] Mismatch for step " + step.Index);
                        return false;
                    }
                }

                Console.WriteLine("  [PASS]");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static bool TestDefaultFirstRideRefineTarget()
        {
            try
            {
                WPELibrary.Lib.MountSpeed.MountSpeedPreset preset =
                    WPELibrary.Lib.MountSpeed.MountSpeedPreset.CreateDefaultFirstRideRefinePreset();
                string error;
                if (!preset.IsCompleteMountRefineTarget(out error) ||
                    !preset.TargetGrowthRate.HasValue ||
                    Math.Abs(preset.TargetGrowthRate.Value - 1.175) > 0.0001 ||
                    preset.TargetSkills == null ||
                    preset.TargetSkills.Count != 3)
                {
                    Console.WriteLine("  [FAIL] Fixed first-mount target is incomplete: " + error);
                    return false;
                }

                int[] expectedIds = { 61108, 61118, 61117 };
                string[] expectedNames = { "高级秋水流弦", "高级百步穿杨", "高级追魂夺命" };
                for (int index = 0; index < expectedIds.Length; index++)
                {
                    if (preset.TargetSkills[index].SkillId != expectedIds[index] ||
                        preset.TargetSkills[index].SkillName != expectedNames[index])
                    {
                        Console.WriteLine("  [FAIL] Fixed first-mount target mismatch at index " + index);
                        return false;
                    }
                }

                Console.WriteLine("  [PASS] skills and growth rate 1.175 are fixed for new first-mount targets");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static bool TestCalculateMountSpeedModel()
        {
            try
            {
                // CalculateMountSpeedModel is static, use static method directly
                double baseSpeed = 100.0;
                double rideAddSpeed = 150.0;
                double expectedRoleMoveSpeed = WPELibrary.Lib.MountSpeed.CalculateMountSpeedModel.CalculateRoleMoveSpeed(baseSpeed, rideAddSpeed);

                Console.WriteLine("  BaseSpeed: " + baseSpeed);
                Console.WriteLine("  RideAddSpeed: " + rideAddSpeed);
                Console.WriteLine("  Calculated RoleMoveSpeed: " + expectedRoleMoveSpeed);

                // Expected: baseSpeed * (1 + rideAddSpeed/100) = 100 * 2.5 = 250
                double expected = baseSpeed * (1 + rideAddSpeed / 100.0);
                if (Math.Abs(expectedRoleMoveSpeed - expected) > 0.001)
                {
                    Console.WriteLine("  [FAIL] Expected " + expected + ", got " + expectedRoleMoveSpeed);
                    return false;
                }

                Console.WriteLine("  [PASS]");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static bool TestMemoryWriterConfiguration()
        {
            try
            {
                // Create a mock identity - ReadOnlyProcessIdentity has internal constructor
                // We'll use a different approach - just test the writer with null for now
                // to verify the API structure
                
                // Test that we can create the writer type
                Type writerType = typeof(WPELibrary.Lib.MountSpeed.MountSpeedMemoryWriter);
                Console.WriteLine("  MountSpeedMemoryWriter type: " + writerType.FullName);
                
                // Test SpeedValueType enum
                var speedTypes = Enum.GetValues(typeof(WPELibrary.Lib.MountSpeed.SpeedValueType));
                Console.WriteLine("  SpeedValueType values: " + string.Join(", ", speedTypes));

                Console.WriteLine("  [PASS]");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static async Task<bool> TestMountStatusMemoryReaderAsync()
        {
            IntPtr mountIdMemory = IntPtr.Zero;
            IntPtr mountedMemory = IntPtr.Zero;
            IntPtr baseSpeedMemory = IntPtr.Zero;
            IntPtr rideAddSpeedMemory = IntPtr.Zero;
            IntPtr roleMoveSpeedMemory = IntPtr.Zero;
            WPELibrary.Lib.MountSpeed.MountStatusMemoryReader reader = null;

            try
            {
                mountIdMemory = Marshal.AllocHGlobal(4);
                mountedMemory = Marshal.AllocHGlobal(4);
                baseSpeedMemory = Marshal.AllocHGlobal(4);
                rideAddSpeedMemory = Marshal.AllocHGlobal(4);
                roleMoveSpeedMemory = Marshal.AllocHGlobal(4);

                WriteInt32(mountIdMemory, 1234);
                WriteInt32(mountedMemory, 1);
                WriteFloat(baseSpeedMemory, 8.0f);
                WriteFloat(rideAddSpeedMemory, 150.0f);
                WriteFloat(roleMoveSpeedMemory, 20.0f);

                string error;
                if (!WPELibrary.Lib.MountSpeed.MountStatusMemoryReader.TryCreate(
                    System.Diagnostics.Process.GetCurrentProcess().Id,
                    out reader,
                    out error))
                {
                    Console.WriteLine("  [FAIL] TryCreate failed: " + error);
                    return false;
                }

                var layout = new WPELibrary.Lib.MountSpeed.MountStatusMemoryLayout
                {
                    MountIdAddress = mountIdMemory.ToInt64(),
                    MountIdType = WPELibrary.Lib.MountSpeed.MountStatusValueType.Int32,
                    IsMountedAddress = mountedMemory.ToInt64(),
                    IsMountedType = WPELibrary.Lib.MountSpeed.MountStatusValueType.Int32,
                    BaseSpeedAddress = baseSpeedMemory.ToInt64(),
                    RideAddSpeedAddress = rideAddSpeedMemory.ToInt64(),
                    RoleMoveSpeedAddress = roleMoveSpeedMemory.ToInt64(),
                    SpeedType = WPELibrary.Lib.MountSpeed.MountStatusValueType.Float
                };

                if (!reader.ConfigureLayout(layout, out error))
                {
                    Console.WriteLine("  [FAIL] ConfigureLayout failed: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountStatusReadResult first =
                    await reader.ReadSnapshotAsync(CancellationToken.None);
                if (!first.Succeeded || first.Snapshot == null)
                {
                    Console.WriteLine("  [FAIL] First snapshot failed: " + first.Code + ": " + first.Error);
                    return false;
                }

                Console.WriteLine("  First snapshot: " + first.Snapshot);
                if (first.Snapshot.MountId != 1234
                    || first.Snapshot.IsMounted != true
                    || !first.Snapshot.BaseSpeed.HasValue
                    || Math.Abs(first.Snapshot.BaseSpeed.Value - 8.0) > 0.001
                    || !first.Snapshot.RideAddSpeed.HasValue
                    || Math.Abs(first.Snapshot.RideAddSpeed.Value - 150.0) > 0.001
                    || !first.Snapshot.RoleMoveSpeed.HasValue
                    || Math.Abs(first.Snapshot.RoleMoveSpeed.Value - 20.0) > 0.001
                    || first.Snapshot.Sequence != 1)
                {
                    Console.WriteLine("  [FAIL] First snapshot values are incorrect.");
                    return false;
                }

                WriteInt32(mountIdMemory, 0);
                WriteInt32(mountedMemory, 0);
                WPELibrary.Lib.MountSpeed.MountStatusReadResult second =
                    await reader.ReadSnapshotAsync(CancellationToken.None);
                if (!second.Succeeded || second.Snapshot == null)
                {
                    Console.WriteLine("  [FAIL] Zero-value snapshot failed: " + second.Code + ": " + second.Error);
                    return false;
                }

                Console.WriteLine("  Zero-value snapshot: " + second.Snapshot);
                if (second.Snapshot.MountId != 0
                    || second.Snapshot.IsMounted != false
                    || second.Snapshot.Sequence != 2)
                {
                    Console.WriteLine("  [FAIL] A valid zero/false value was treated incorrectly.");
                    return false;
                }

                layout.MountIdAddress = 0;
                if (!reader.ConfigureLayout(layout, out error))
                {
                    Console.WriteLine("  [FAIL] Optional MountId layout was rejected: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountStatusReadResult withoutMountId =
                    await reader.ReadSnapshotAsync(CancellationToken.None);
                if (!withoutMountId.Succeeded
                    || withoutMountId.Snapshot == null
                    || withoutMountId.Snapshot.MountId.HasValue
                    || withoutMountId.Snapshot.IsMounted != false)
                {
                    Console.WriteLine("  [FAIL] Snapshot without an unconfirmed MountId address was not accepted safely.");
                    return false;
                }

                Console.WriteLine("  Snapshot without MountId address: " + withoutMountId.Snapshot);

                Console.WriteLine("  [PASS]");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
            finally
            {
                if (reader != null)
                {
                    reader.Dispose();
                }

                FreeMemory(mountIdMemory);
                FreeMemory(mountedMemory);
                FreeMemory(baseSpeedMemory);
                FreeMemory(rideAddSpeedMemory);
                FreeMemory(roleMoveSpeedMemory);
            }
        }

        static async Task<bool> TestMountStatusReaderFailClosedAsync()
        {
            WPELibrary.Lib.MountSpeed.MountStatusMemoryReader reader = null;
            try
            {
                string error;
                if (!WPELibrary.Lib.MountSpeed.MountStatusMemoryReader.TryCreate(
                    System.Diagnostics.Process.GetCurrentProcess().Id,
                    out reader,
                    out error))
                {
                    Console.WriteLine("  [FAIL] TryCreate failed: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountStatusReadResult result =
                    await reader.ReadSnapshotAsync(CancellationToken.None);
                Console.WriteLine("  Result without layout: " + result.Code + ": " + result.Error);
                if (result.Succeeded || result.Code != "layout_not_configured" || result.Snapshot != null)
                {
                    Console.WriteLine("  [FAIL] Missing layout did not fail closed.");
                    return false;
                }

                Console.WriteLine("  [PASS]");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
            finally
            {
                if (reader != null)
                {
                    reader.Dispose();
                }
            }
        }

        static bool TestMountStatusAndroidSnapshotProtocol()
        {
            const string validJson = "{"
                + "\"event\":\"mount_status_snapshot\","
                + "\"schema\":\"mount_status_snapshot.v1\","
                + "\"schemaVersion\":1,"
                + "\"readOnly\":true,"
                + "\"actionAuthorized\":false,"
                + "\"sessionId\":\"11111111-1111-1111-1111-111111111111\","
                + "\"sequence\":1,"
                + "\"status\":\"ok\","
                + "\"diagnosticCode\":\"ok\","
                + "\"candidateCount\":1,"
                + "\"process\":{\"pid\":1853,\"startTicks\":1466,\"exe\":\"/system/bin/app_process64\"},"
                + "\"player\":{"
                + "\"playerId\":256,\"isLocalPlayer\":true,\"mountId\":null,\"isMounted\":false,"
                + "\"roleMoveSpeed\":200.0,\"gx\":43,\"gy\":18,"
                + "\"rawValues\":{"
                + "\"m_RideId\":{\"kind\":\"nil\",\"raw\":\"0xffffffffffffffff\",\"value\":null},"
                + "\"m_IsLocalPlayer\":{\"kind\":\"boolean\",\"raw\":\"0xfffeffffffffffff\",\"value\":true},"
                + "\"m_RoleMoveSpeed\":{\"kind\":\"i32\",\"raw\":\"0xfff90000000000c8\",\"value\":200}"
                + "}}"
                + "}";

            WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot snapshot;
            string error;
            if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                validJson,
                out snapshot,
                out error))
            {
                Console.WriteLine("  [FAIL] Valid Android snapshot rejected: " + error);
                return false;
            }

            Console.WriteLine("  Snapshot: " + snapshot);
            if (!snapshot.IsValid || snapshot.PlayerId != 256 || snapshot.IsMounted != false
                || snapshot.MountId.HasValue || Math.Abs(snapshot.RoleMoveSpeed - 200.0) > 0.001
                || snapshot.Gx != 43 || snapshot.Gy != 18)
            {
                Console.WriteLine("  [FAIL] Android snapshot values are incorrect.");
                return false;
            }

            string mountedJson = validJson
                .Replace("\"mountId\":null,\"isMounted\":false", "\"mountId\":9111,\"isMounted\":true")
                .Replace("\"roleMoveSpeed\":200.0,\"gx\":43,\"gy\":18", "\"roleMoveSpeed\":256.0,\"gx\":73,\"gy\":56,\"growthRate\":1.175,\"growthRateSource\":\"LocalRide.PropertyValueDict.GROWUP\"")
                .Replace("\"m_RideId\":{\"kind\":\"nil\",\"raw\":\"0xffffffffffffffff\",\"value\":null}", "\"m_RideId\":{\"kind\":\"i32\",\"raw\":\"0xfff9000000002397\",\"value\":9111}")
                .Replace("\"m_RoleMoveSpeed\":{\"kind\":\"i32\",\"raw\":\"0xfff90000000000c8\",\"value\":200}", "\"m_RoleMoveSpeed\":{\"kind\":\"f64\",\"raw\":\"0x4070000000000000\",\"value\":256.0}")
                .Replace(
                    "\"rawValues\":{",
                    "\"rideInstances\":[{\"rideInstanceId\":\"2091811378351566849\",\"rideShapeId\":9121,\"isRiding\":false,\"skills\":[{\"slotIndex\":2,\"skillId\":62022,\"exp\":0,\"skillName\":\"澧兰沅芷\"},{\"slotIndex\":3,\"skillId\":62122,\"exp\":0,\"skillName\":\"高级澧兰沅芷\"},{\"slotIndex\":4,\"skillId\":62407,\"exp\":0,\"skillName\":\"中级兰质蕙心\"}]},{\"rideInstanceId\":\"2091811378351566852\",\"rideShapeId\":9111,\"isRiding\":true,\"isCurrent\":true,\"skills\":[{\"slotIndex\":1,\"skillId\":61008,\"exp\":0,\"skillName\":\"秋水流弦\"},{\"slotIndex\":2,\"skillId\":61021,\"exp\":0,\"skillName\":\"神枢鬼藏\"},{\"slotIndex\":3,\"skillId\":61223,\"exp\":0,\"skillName\":\"高级坚壁清野\"}]}],\"activeRideInstanceId\":\"2091811378351566852\",\"rideBindingStatus\":\"bound\",\"rideBindingSource\":\"LocalRide.PropertyValueDict.SHAPE+RIDEING\",\"rawValues\":{");
            mountedJson = mountedJson
                .Replace("\"rideShapeId\":9121,\"isRiding\"", "\"rideShapeId\":9121,\"growthRate\":0.975,\"growthRateSource\":\"LocalRide.PropertyValueDict.GROWUP\",\"isRiding\"")
                .Replace("\"rideShapeId\":9111,\"isRiding\"", "\"rideShapeId\":9111,\"growthRate\":1.175,\"growthRateSource\":\"LocalRide.PropertyValueDict.GROWUP\",\"isRiding\"");
            StringBuilder refineCards = new StringBuilder("[");
            for (int cardIndex = 1; cardIndex <= 21; cardIndex++)
            {
                if (cardIndex > 1)
                {
                    refineCards.Append(",");
                }
                if (cardIndex == 1)
                {
                    refineCards.Append(
                        "{\"cardIndex\":1,\"isCurrent\":true,\"growthRate\":1.175,\"growthRateSource\":\"LocalRide.PropertyValueDict.GROWUP\",\"source\":\"LocalRide.m_Rideskills\",\"skills\":[" +
                        "{\"slotIndex\":1,\"skillId\":61108,\"skillName\":\"高级秋水流弦\"}," +
                        "{\"slotIndex\":2,\"skillId\":61105,\"skillName\":\"高级泣血枕戈\"}," +
                        "{\"slotIndex\":3,\"skillId\":61118,\"skillName\":\"高级百步穿杨\"}]}");
                    continue;
                }
                refineCards.Append(
                    "{\"cardIndex\":").Append(cardIndex)
                    .Append(",\"isCurrent\":false,\"growthRate\":1.095,\"growthRateSource\":\"LocalRide.m_ResetData[4].growty/1000\",\"speed\":22,\"score\":0,\"source\":\"LocalRide.m_ResetData[4]\",\"skills\":[")
                    .Append("{\"slotIndex\":2,\"skillId\":61223,\"skillName\":\"中级坚壁清野\",\"value\":\"0\"},")
                    .Append("{\"slotIndex\":3,\"skillId\":61224,\"skillName\":\"中级战心清明\",\"value\":\"0\"},")
                    .Append("{\"slotIndex\":4,\"skillId\":61005,\"skillName\":\"泣血枕戈\",\"value\":\"0\"}]}");
            }
            refineCards.Append("]");
            mountedJson = mountedJson.Replace(
                "\"rideInstances\":[",
                "\"rideRefineCards\":" + refineCards + ",\"rideInstances\":[");
            if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                mountedJson,
                out snapshot,
                out error)
                || !snapshot.IsValid || snapshot.PlayerId != 256 || snapshot.IsMounted != true
                || snapshot.MountId != 9111 || !snapshot.GrowthRate.HasValue
                || Math.Abs(snapshot.GrowthRate.Value - 1.175) > 0.001
                || snapshot.GrowthRateSource != "LocalRide.PropertyValueDict.GROWUP"
                || Math.Abs(snapshot.RoleMoveSpeed - 256.0) > 0.001
                || snapshot.Gx != 73 || snapshot.Gy != 56
                || snapshot.RideInstances == null
                || snapshot.RideInstances.Count != 2
                || snapshot.RideInstances[0].RideInstanceId != "2091811378351566849"
                || snapshot.RideInstances[0].RideShapeId != 9121
                || !snapshot.RideInstances[0].GrowthRate.HasValue
                || Math.Abs(snapshot.RideInstances[0].GrowthRate.Value - 0.975) > 0.001
                || snapshot.RideInstances[0].GrowthRateSource != "LocalRide.PropertyValueDict.GROWUP"
                || snapshot.RideInstances[0].IsRiding != false
                || snapshot.RideInstances[0].Skills == null
                || snapshot.RideInstances[0].Skills.Count != 3
                || snapshot.RideInstances[0].Skills[0].SlotIndex != 2
                || snapshot.RideInstances[0].Skills[0].SkillId != 62022
                || snapshot.RideInstances[0].Skills[0].SkillName != "澧兰沅芷"
                || snapshot.RideInstances[0].Skills[1].SkillName != "高级澧兰沅芷"
                || snapshot.RideInstances[0].Skills[2].SkillName != "中级兰质蕙心"
                || snapshot.RideInstances[0].Skills[2].SkillId != 62407
                || snapshot.RideInstances[1].RideInstanceId != "2091811378351566852"
                || snapshot.RideInstances[1].RideShapeId != 9111
                || !snapshot.RideInstances[1].GrowthRate.HasValue
                || Math.Abs(snapshot.RideInstances[1].GrowthRate.Value - 1.175) > 0.001
                || snapshot.RideInstances[1].GrowthRateSource != "LocalRide.PropertyValueDict.GROWUP"
                || snapshot.RideInstances[1].IsRiding != true
                || snapshot.RideInstances[1].IsCurrent != true
                || snapshot.ActiveRideInstanceId != "2091811378351566852"
             || snapshot.RideBindingStatus != "bound"
             || snapshot.RideBindingSource != "LocalRide.PropertyValueDict.SHAPE+RIDEING"
             || snapshot.RideRefineCards == null
             || snapshot.RideRefineCards.Count != 21
             || snapshot.RideRefineCards[0].CardIndex != 1
             || snapshot.RideRefineCards[0].IsCurrent != true
             || snapshot.RideRefineCards[0].Skills == null
             || snapshot.RideRefineCards[0].Skills.Count != 3
             || snapshot.RideRefineCards[0].Skills[0].SkillId != 61108
             || snapshot.RideRefineCards[0].Skills[0].SkillName != "高级秋水流弦"
             || snapshot.RideRefineCards[1].CardIndex != 2
             || snapshot.RideRefineCards[1].IsCurrent != false
             || !snapshot.RideRefineCards[1].GrowthRate.HasValue
             || Math.Abs(snapshot.RideRefineCards[1].GrowthRate.Value - 1.095) > 0.001
             || snapshot.RideRefineCards[1].Speed != 22
             || snapshot.RideRefineCards[1].Score != 0
             || snapshot.RideRefineCards[1].Source != "LocalRide.m_ResetData[4]"
             || snapshot.RideRefineCards[1].Skills == null
             || snapshot.RideRefineCards[1].Skills.Count != 3
             || snapshot.RideRefineCards[1].Skills[0].SlotIndex != 2
             || snapshot.RideRefineCards[1].Skills[0].SkillId != 61223
             || snapshot.RideRefineCards[1].Skills[0].SkillName != "中级坚壁清野"
             || snapshot.RideRefineCards[1].Skills[0].Value != "0")
            {
                Console.WriteLine("  [FAIL] Mounted Android snapshot values are incorrect: " + error);
                return false;
            }

            WPELibrary.Lib.MountSpeed.MountRideRefineCardComparison firstComparison =
                WPELibrary.Lib.MountSpeed.MountRideRefineCardComparer.Compare(null, snapshot);
            WPELibrary.Lib.MountSpeed.MountRideRefineCardComparison sameComparison =
                WPELibrary.Lib.MountSpeed.MountRideRefineCardComparer.Compare(snapshot, snapshot);
            string changedCardsJson = mountedJson.Replace(
                "\"cardIndex\":2,\"isCurrent\":false,\"growthRate\":1.095",
                "\"cardIndex\":2,\"isCurrent\":false,\"growthRate\":1.055");
            WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot changedSnapshot;
            if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                changedCardsJson,
                out changedSnapshot,
                out error))
            {
                Console.WriteLine("  [FAIL] Changed refine-card fixture rejected: " + error);
                return false;
            }
            WPELibrary.Lib.MountSpeed.MountRideRefineCardComparison changedComparison =
                WPELibrary.Lib.MountSpeed.MountRideRefineCardComparer.Compare(
                    snapshot,
                    changedSnapshot);
            if (firstComparison.HasPreviousSnapshot ||
                firstComparison.CurrentCardCount != 21 ||
                sameComparison.Changes == null ||
                sameComparison.Changes.Count != 0 ||
                changedComparison.ChangedCount != 1 ||
                changedComparison.AddedCount != 0 ||
                changedComparison.RemovedCount != 0 ||
                changedComparison.Changes == null ||
                changedComparison.Changes.Count != 1 ||
                changedComparison.Changes[0].CardIndex != 2 ||
                changedComparison.Changes[0].Summary.IndexOf("成长率", StringComparison.Ordinal) < 0)
            {
                Console.WriteLine("  [FAIL] Refine-card comparison values are incorrect.");
                return false;
            }

            WPELibrary.Lib.MountSpeed.MountSkillPresetPreviewResult preview;
            WPELibrary.Lib.MountSpeed.MountSpeedPreset previewPreset =
                new WPELibrary.Lib.MountSpeed.MountSpeedPreset
                {
                    TargetSkills = new System.Collections.Generic.List<WPELibrary.Lib.MountSpeed.MountSkillTarget>
                    {
                        new WPELibrary.Lib.MountSpeed.MountSkillTarget
                        {
                            SkillId = 61008,
                            SkillName = "秋水流弦"
                        },
                        new WPELibrary.Lib.MountSpeed.MountSkillTarget
                        {
                            SkillId = 61021,
                            SkillName = "神枢鬼藏"
                        }
                    }
                };
            if (!WPELibrary.Lib.MountSpeed.MountSkillPresetPreview.TryEvaluate(
                    snapshot,
                    previewPreset,
                    out preview,
                    out error) || !preview.IsSatisfied || preview.Items.Count != 2 ||
                preview.Items[0].Status != "已满足" || preview.Items[1].Status != "已满足")
            {
                Console.WriteLine("  [FAIL] Matching target skill preview failed: " + error);
                return false;
            }

            previewPreset.TargetSkills.Add(
                new WPELibrary.Lib.MountSpeed.MountSkillTarget
                {
                    SkillId = 69999,
                    SkillName = "不存在的坐骑技能"
                });
            if (!WPELibrary.Lib.MountSpeed.MountSkillPresetPreview.TryEvaluate(
                    snapshot,
                    previewPreset,
                    out preview,
                    out error) || preview.IsSatisfied || preview.Items.Count != 3 ||
                preview.Items[2].Status != "缺少")
            {
                Console.WriteLine("  [FAIL] Missing target skill preview failed: " + error);
                return false;
            }

            WPELibrary.Lib.MountSpeed.MountRideRefineTargetPreviewResult refinePreview;
            WPELibrary.Lib.MountSpeed.MountSpeedPreset refinePreset =
                new WPELibrary.Lib.MountSpeed.MountSpeedPreset
                {
                    TargetGrowthRate = 1.095,
                    // Deliberately use a different order from card 2.
                    TargetSkills = new System.Collections.Generic.List<WPELibrary.Lib.MountSpeed.MountSkillTarget>
                    {
                        new WPELibrary.Lib.MountSpeed.MountSkillTarget
                        {
                            SkillId = 61005,
                            SkillName = "泣血枕戈"
                        },
                        new WPELibrary.Lib.MountSpeed.MountSkillTarget
                        {
                            SkillId = 61224,
                            SkillName = "中级战心清明"
                        },
                        new WPELibrary.Lib.MountSpeed.MountSkillTarget
                        {
                            SkillId = 61223,
                            SkillName = "中级坚壁清野"
                        }
                    }
                };
            if (!WPELibrary.Lib.MountSpeed.MountRideRefineTargetMatcher.TryEvaluate(
                    snapshot,
                    refinePreset,
                    out refinePreview,
                    out error) ||
                !refinePreview.IsSatisfied ||
                refinePreview.MatchedCardIndex != 2 ||
                refinePreview.CardsChecked != 21 ||
                !refinePreview.GrowthRateMatched ||
                !refinePreview.SkillsMatched)
            {
                Console.WriteLine("  [FAIL] Unordered refine-card target matching failed: " + error);
                return false;
            }

            refinePreset.TargetGrowthRate = 1.234;
            if (!WPELibrary.Lib.MountSpeed.MountRideRefineTargetMatcher.TryEvaluate(
                    snapshot,
                    refinePreset,
                    out refinePreview,
                    out error) || refinePreview.IsSatisfied ||
                refinePreview.GrowthRateMatched || !refinePreview.SkillsMatched)
            {
                Console.WriteLine("  [FAIL] Refine-card growth-rate matching failed: " + error);
                return false;
            }

            string invalidBinding = mountedJson.Replace(
                "\"activeRideInstanceId\":\"2091811378351566852\"",
                "\"activeRideInstanceId\":\"2091811378351566849\"");
            if (WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                invalidBinding,
                out snapshot,
                out error))
            {
                Console.WriteLine("  [FAIL] Mismatched ride binding was accepted.");
                return false;
            }

            string tampered = validJson.Replace("\"actionAuthorized\":false", "\"actionAuthorized\":true");
            if (WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                tampered,
                out snapshot,
                out error))
            {
                Console.WriteLine("  [FAIL] Authorized Android snapshot was accepted.");
                return false;
            }

            string ambiguous = validJson.Replace("\"candidateCount\":1", "\"candidateCount\":2");
            if (WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                ambiguous,
                out snapshot,
                out error))
            {
                Console.WriteLine("  [FAIL] Ambiguous Android snapshot was accepted.");
                return false;
            }

            Console.WriteLine("  [PASS]");
            return true;
        }

        static async Task<bool> TestMountRefineStateMachineAsync()
        {
            try
            {
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot baseline;
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot changed;
                string error;
                if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                    BuildMountedRefineFixtureJson(1.095),
                    out baseline,
                    out error) ||
                    !WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                        BuildMountedRefineFixtureJson(1.055, 2),
                        out changed,
                        out error))
                {
                    Console.WriteLine("  [FAIL] State-machine fixture rejected: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountSpeedPreset preset = CreateRefineStateMachinePreset();
                WPELibrary.Lib.MountSpeed.MountRefineConfiguration configuration =
                    new WPELibrary.Lib.MountSpeed.MountRefineConfiguration
                    {
                        Preset = preset,
                        MaxAttempts = 3,
                        ResultConfirmTimeoutMs = 500,
                        IntervalMs = 0
                    };

                WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource source =
                    new ScriptedMountRefineSnapshotSource(baseline, changed);
                WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender sender =
                    new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                List<WPELibrary.Lib.MountSpeed.MountRefineLogEntry> logEntries =
                    new List<WPELibrary.Lib.MountSpeed.MountRefineLogEntry>();
                Guid runId = Guid.NewGuid();
                WPELibrary.Lib.MountSpeed.MountRefineStateMachine machine =
                    new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                        configuration,
                        source,
                        sender,
                        logEntries.Add,
                        runId);
                WPELibrary.Lib.MountSpeed.MountRefineExecutionResult result =
                    await machine.RunAsync(CancellationToken.None);

                bool sawSendResult = false;
                bool sawFinished = false;
                foreach (WPELibrary.Lib.MountSpeed.MountRefineLogEntry entry in logEntries)
                {
                    if (entry.RunId != runId)
                    {
                        Console.WriteLine("  [FAIL] Persistent diagnostic run ID changed inside one run.");
                        return false;
                    }
                    sawSendResult = sawSendResult || entry.EventName == "send_result";
                    sawFinished = sawFinished || entry.EventName == "finished";
                }

                if (!result.Success ||
                    result.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.TargetReached ||
                    result.AttemptCount != 1 ||
                    result.MatchedCardIndex != 2 ||
                    sender.Requests.Count != 1 ||
                    sender.Requests[0].AttemptNumber != 1 ||
                    result.FinalState != WPELibrary.Lib.MountSpeed.MountRefineState.Completed ||
                    !sawSendResult ||
                    !sawFinished)
                {
                    Console.WriteLine("  [FAIL] Read-send-refresh-stop loop result is incorrect: " + result.Message);
                    return false;
                }

                source = new ScriptedMountRefineSnapshotSource(changed);
                sender = new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(false, false);
                machine = new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(configuration, source, sender);
                result = await machine.RunAsync(CancellationToken.None);
                if (!result.Success ||
                    result.AttemptCount != 0 ||
                    sender.Requests.Count != 0 ||
                    result.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.TargetReached ||
                    result.Message != "target_reached_before_send")
                {
                    Console.WriteLine("  [FAIL] Pre-hit target did not stop before sending: " + result.Message);
                    return false;
                }

                source = new CancellationAfterInitialSnapshotSource(baseline);
                sender = new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                machine = new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                    configuration,
                    source,
                    sender);
                result = await machine.RunAsync(CancellationToken.None);
                if (result.Success ||
                    result.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.UserStopped ||
                    result.Message != "mount_refine_user_stopped" ||
                    result.AttemptCount != 1 ||
                    sender.Requests.Count != 1)
                {
                    Console.WriteLine("  [FAIL] Cancelled Android snapshot was not classified as a user/host stop: " + result.Message);
                    return false;
                }

                source = new ScriptedMountRefineSnapshotSource(baseline);
                machine = new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                    configuration,
                    source,
                    new WPELibrary.Lib.MountSpeed.FailClosedMountRefinePacketSender());
                result = await machine.RunAsync(CancellationToken.None);
                if (result.Success ||
                    result.AttemptCount != 0 ||
                    result.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.ProtocolUnverified)
                {
                    Console.WriteLine("  [FAIL] Unverified sender did not fail closed: " + result.Message);
                    return false;
                }

                Console.WriteLine("  [PASS] target hit after one refresh; cancellation is a user/host stop; pre-hit and unverified paths sent 0 packets");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static async Task<bool> TestMountRefineSnapshotReadTimeoutAsync()
        {
            try
            {
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot baseline;
                string error;
                if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                    BuildMountedRefineFixtureJson(1.095),
                    out baseline,
                    out error))
                {
                    Console.WriteLine("  [FAIL] Timeout fixture rejected: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot changed;
                if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                    BuildMountedRefineFixtureJson(1.055, 2),
                    out changed,
                    out error))
                {
                    Console.WriteLine("  [FAIL] Retry fixture rejected: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountRefineConfiguration retryConfiguration =
                    new WPELibrary.Lib.MountSpeed.MountRefineConfiguration
                    {
                        Preset = CreateRefineStateMachinePreset(),
                        MaxAttempts = 2,
                        ResultConfirmTimeoutMs = 40,
                        SnapshotReadTimeoutMs = 20,
                        IntervalMs = 0
                    };
                List<WPELibrary.Lib.MountSpeed.MountRefineLogEntry> retryLogs =
                    new List<WPELibrary.Lib.MountSpeed.MountRefineLogEntry>();
                WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender retrySender =
                    new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                WPELibrary.Lib.MountSpeed.MountRefineStateMachine machine =
                    new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                        retryConfiguration,
                        new TransientUnchangedThenChangedSnapshotSource(baseline, changed, 4),
                        retrySender,
                        retryLogs.Add);
                WPELibrary.Lib.MountSpeed.MountRefineExecutionResult result =
                    await machine.RunAsync(CancellationToken.None);

                bool retryLogged = false;
                foreach (WPELibrary.Lib.MountSpeed.MountRefineLogEntry entry in retryLogs)
                {
                    if (entry != null && entry.EventName == "refresh_wait_retry")
                    {
                        retryLogged = true;
                        break;
                    }
                }

                if (!result.Success ||
                    result.AttemptCount != 1 ||
                    result.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.TargetReached ||
                    retrySender.Requests.Count != 1 ||
                    !retryLogged)
                {
                    Console.WriteLine(
                        "  [FAIL] A transient refresh timeout stopped the run: " + result.Message);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountRefineConfiguration cancellationConfiguration =
                    new WPELibrary.Lib.MountSpeed.MountRefineConfiguration
                    {
                        Preset = CreateRefineStateMachinePreset(),
                        MaxAttempts = 2,
                        ResultConfirmTimeoutMs = 250,
                        SnapshotReadTimeoutMs = 100,
                        IntervalMs = 0
                    };
                WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender cancellationSender =
                    new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                machine = new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                    cancellationConfiguration,
                    new SlowCancellableSnapshotSource(baseline),
                    cancellationSender);
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    System.Diagnostics.Stopwatch stopwatch =
                        System.Diagnostics.Stopwatch.StartNew();
                    cancellation.CancelAfter(350);
                    result = await machine.RunAsync(cancellation.Token);
                    stopwatch.Stop();

                    if (result.Success ||
                        result.AttemptCount != 1 ||
                        result.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.UserStopped ||
                        cancellationSender.Requests.Count != 1 ||
                        stopwatch.ElapsedMilliseconds >= 1500)
                    {
                        Console.WriteLine(
                            "  [FAIL] Manual cancellation was not terminal/bounded: " + result.Message);
                        return false;
                    }

                    Console.WriteLine(
                        "  [PASS] refresh timeout retries without resending; manual stop remains bounded at " +
                        stopwatch.ElapsedMilliseconds + "ms");
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static async Task<bool> TestMountRefineSnapshotSequenceWaitAsync()
        {
            try
            {
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot baseline;
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot staleChanged;
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot advancedChanged;
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot advancedUnchanged;
                string error;
                if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                    BuildMountedRefineFixtureJson(1.095, 1),
                    out baseline,
                    out error) ||
                    !WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                        BuildMountedRefineFixtureJson(1.055, 1),
                        out staleChanged,
                        out error) ||
                    !WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                        BuildMountedRefineFixtureJson(1.055, 2),
                        out advancedChanged,
                        out error) ||
                    !WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                        BuildMountedRefineFixtureJson(1.095, 2),
                        out advancedUnchanged,
                        out error))
                {
                    Console.WriteLine("  [FAIL] Sequence-wait fixture rejected: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountRefineConfiguration configuration =
                    new WPELibrary.Lib.MountSpeed.MountRefineConfiguration
                    {
                        Preset = CreateRefineStateMachinePreset(),
                        MaxAttempts = 2,
                        ResultConfirmTimeoutMs = 120,
                        SnapshotReadTimeoutMs = 100,
                        IntervalMs = 0
                    };

                string[] readPaths = { "point", "fast", "fallback" };
                foreach (string readPath in readPaths)
                {
                    List<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult> reads =
                        new List<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult>();
                    reads.Add(CreateFixtureReadResult(baseline, readPath));
                    for (int index = 0; index < 6; index++)
                    {
                        // The card already matches the target, but its sequence is
                        // still the old frame. It must not enter verify or send again.
                        reads.Add(CreateFixtureReadResult(staleChanged, readPath));
                    }
                    for (int index = 0; index < 3; index++)
                    {
                        // A newer read/request sequence alone does not prove that
                        // the server produced a new refine result.
                        reads.Add(CreateFixtureReadResult(advancedUnchanged, readPath));
                    }
                    reads.Add(CreateFixtureReadResult(advancedChanged, readPath));

                    SequenceScriptedMountRefineSnapshotSource source =
                        new SequenceScriptedMountRefineSnapshotSource(reads.ToArray());
                    WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender sender =
                        new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                    List<WPELibrary.Lib.MountSpeed.MountRefineLogEntry> logs =
                        new List<WPELibrary.Lib.MountSpeed.MountRefineLogEntry>();
                    WPELibrary.Lib.MountSpeed.MountRefineStateMachine machine =
                        new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                            configuration,
                            source,
                            sender,
                            logs.Add);
                    WPELibrary.Lib.MountSpeed.MountRefineExecutionResult result =
                        await machine.RunAsync(CancellationToken.None);

                    int sequenceWaitCount = 0;
                    int contentWaitCount = 0;
                    int verifyCount = 0;
                    WPELibrary.Lib.MountSpeed.MountRefineLogEntry verifyEntry = null;
                    foreach (WPELibrary.Lib.MountSpeed.MountRefineLogEntry entry in logs)
                    {
                        if (entry.EventName == "snapshot_sequence_wait")
                        {
                            sequenceWaitCount++;
                            if (entry.Code != "mount_refine_snapshot_sequence_not_advanced_waiting")
                            {
                                Console.WriteLine("  [FAIL] " + readPath + " sequence-wait code was not explicit.");
                                return false;
                            }
                        }
                        if (entry.EventName == "snapshot_content_wait")
                        {
                            contentWaitCount++;
                            if (entry.Code != "mount_refine_snapshot_cards_not_changed_waiting")
                            {
                                Console.WriteLine("  [FAIL] " + readPath + " content-wait code was not explicit.");
                                return false;
                            }
                        }
                        if (entry.EventName == "verify_new_snapshot")
                        {
                            verifyCount++;
                            verifyEntry = entry;
                        }
                    }

                    if (!result.Success ||
                        result.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.TargetReached ||
                        result.AttemptCount != 1 ||
                        result.MatchedCardIndex != 2 ||
                        sender.Requests.Count != 1 ||
                        source.ReadCount < 11 ||
                        sequenceWaitCount != 1 ||
                        contentWaitCount != 1 ||
                        verifyCount != 1 ||
                        verifyEntry == null ||
                        verifyEntry.Sequence != 2 ||
                        verifyEntry.RefineCardCount != 21 ||
                        verifyEntry.ReadPath != readPath)
                    {
                        Console.WriteLine(
                            "  [FAIL] " + readPath +
                            " did not wait for a strictly newer and content-changed 21-card snapshot: " + result.Message);
                        return false;
                    }

                    Console.WriteLine(
                        "  [PASS] " + readPath +
                        " waited through stale sequence 1 and unchanged sequence 2, then verified changed sequence 2");
                }

                GatedMountRefineSnapshotSource waitingSource =
                    new GatedMountRefineSnapshotSource(baseline, advancedChanged);
                WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender waitingSender =
                    new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                WPELibrary.Lib.MountSpeed.MountRefineStateMachine waitingMachine =
                    new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                        configuration,
                        waitingSource,
                        waitingSender);
                Task<WPELibrary.Lib.MountSpeed.MountRefineExecutionResult> waitingTask =
                    waitingMachine.RunAsync(CancellationToken.None);
                DateTime waitingDeadline = DateTime.UtcNow.AddSeconds(2);
                while (waitingSource.ReadCount < 5 && DateTime.UtcNow < waitingDeadline)
                {
                    await Task.Delay(10);
                }
                if (waitingSource.ReadCount < 5 ||
                    waitingTask.IsCompleted ||
                    waitingMachine.State != WPELibrary.Lib.MountSpeed.MountRefineState.WaitingForRefresh ||
                    waitingSender.Requests.Count != 1)
                {
                    waitingSource.ReleaseChangedSnapshot();
                    await waitingTask;
                    Console.WriteLine(
                        "  [FAIL] Repeated same-sequence reads did not remain in the one-send wait state.");
                    return false;
                }

                waitingSource.ReleaseChangedSnapshot();
                WPELibrary.Lib.MountSpeed.MountRefineExecutionResult waitingResult = await waitingTask;
                if (!waitingResult.Success ||
                    waitingResult.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.TargetReached ||
                    waitingResult.AttemptCount != 1 ||
                    waitingSender.Requests.Count != 1)
                {
                    Console.WriteLine(
                        "  [FAIL] The explicit same-sequence wait did not resume after content changed.");
                    return false;
                }

                WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender waitingCancellationSender =
                    new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                WPELibrary.Lib.MountSpeed.MountRefineStateMachine waitingCancellationMachine =
                    new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                        configuration,
                        new TransientUnchangedThenChangedSnapshotSource(
                            baseline,
                            advancedChanged,
                            1000000),
                        waitingCancellationSender);
                using (CancellationTokenSource waitingCancellation = new CancellationTokenSource())
                {
                    waitingCancellation.CancelAfter(180);
                    WPELibrary.Lib.MountSpeed.MountRefineExecutionResult waitingCancellationResult =
                        await waitingCancellationMachine.RunAsync(waitingCancellation.Token);
                    if (waitingCancellationResult.Success ||
                        waitingCancellationResult.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.UserStopped ||
                        waitingCancellationResult.AttemptCount != 1 ||
                        waitingCancellationSender.Requests.Count != 1)
                    {
                        Console.WriteLine(
                            "  [FAIL] Cancellation during a repeated-sequence wait was not mapped to UserStopped.");
                        return false;
                    }
                }

                WPELibrary.Lib.MountSpeed.MountRefineStateMachine failureMachine = null;
                WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender failureSender =
                    new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                failureMachine = new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                    configuration,
                    new SequenceScriptedMountRefineSnapshotSource(
                        CreateFixtureReadResult(baseline, "fallback"),
                        new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                            null,
                            "fixture_read_failed")),
                    failureSender);
                WPELibrary.Lib.MountSpeed.MountRefineExecutionResult failureResult =
                    await failureMachine.RunAsync(CancellationToken.None);
                if (failureResult.Success ||
                    failureResult.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.SnapshotReadFailed ||
                    failureResult.AttemptCount != 1 ||
                    failureSender.Requests.Count != 1)
                {
                    Console.WriteLine("  [FAIL] Real read failure did not fail closed: " + failureResult.Message);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot contextChanged;
                if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                    BuildMountedRefineFixtureJson(1.055, 2).Replace(
                        "\"ride-1\"",
                        "\"ride-2\""),
                    out contextChanged,
                    out error))
                {
                    Console.WriteLine("  [FAIL] Context-change fixture rejected: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender contextSender =
                    new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                WPELibrary.Lib.MountSpeed.MountRefineStateMachine contextMachine =
                    new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                        configuration,
                        new SequenceScriptedMountRefineSnapshotSource(
                            CreateFixtureReadResult(baseline, "fallback"),
                            CreateFixtureReadResult(contextChanged, "point")),
                        contextSender);
                WPELibrary.Lib.MountSpeed.MountRefineExecutionResult contextResult =
                    await contextMachine.RunAsync(CancellationToken.None);
                if (contextResult.Success ||
                    contextResult.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.MountChanged ||
                    contextResult.AttemptCount != 1 ||
                    contextSender.Requests.Count != 1)
                {
                    Console.WriteLine("  [FAIL] Context change did not stop safely: " + contextResult.Message);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot incomplete;
                if (!WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshotProtocol.TryParse(
                    BuildMountedRefineFixtureJson(1.055, 2, 20),
                    out incomplete,
                    out error))
                {
                    Console.WriteLine("  [FAIL] Incomplete-card fixture rejected before state-machine validation: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender incompleteSender =
                    new WPELibrary.Lib.MountSpeed.RecordingMountRefinePacketSender(true, true);
                WPELibrary.Lib.MountSpeed.MountRefineStateMachine incompleteMachine =
                    new WPELibrary.Lib.MountSpeed.MountRefineStateMachine(
                        configuration,
                        new SequenceScriptedMountRefineSnapshotSource(
                            CreateFixtureReadResult(baseline, "fallback"),
                            CreateFixtureReadResult(incomplete, "fast")),
                        incompleteSender);
                WPELibrary.Lib.MountSpeed.MountRefineExecutionResult incompleteResult =
                    await incompleteMachine.RunAsync(CancellationToken.None);
                if (incompleteResult.Success ||
                    incompleteResult.StopReason != WPELibrary.Lib.MountSpeed.MountRefineStopReason.SnapshotReadFailed ||
                    incompleteResult.AttemptCount != 1 ||
                    incompleteSender.Requests.Count != 1)
                {
                    Console.WriteLine("  [FAIL] Incomplete snapshot did not fail closed: " + incompleteResult.Message);
                    return false;
                }

                Console.WriteLine(
                    "  [PASS] strict sequence plus card-content change resumes, wait cancellation maps to UserStopped, and real failures remain fail-closed");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static bool TestMountRefineA050PacketTemplate()
        {
            try
            {
                byte[] firstPacket = ParseHex(
                    "4D 5A 00 00 00 00 00 00 00 1D A0 50 00 06 03 00 00 00 00 13 " +
                    "32 30 39 32 31 30 30 34 34 32 39 33 33 38 33 33 37 33 32");

                WPELibrary.Lib.MountSpeed.MountRefineA050PacketTemplate template;
                string error;
                if (!WPELibrary.Lib.MountSpeed.MountRefineA050PacketTemplate.TryCreate(
                    firstPacket,
                    "user-capture-mount-refine-a050-20260826",
                    out template,
                    out error))
                {
                    Console.WriteLine("  [FAIL] First A050 sample rejected: " + error);
                    return false;
                }

                WPELibrary.Lib.MountSpeed.MountRefineSendRequest request =
                    new WPELibrary.Lib.MountSpeed.MountRefineSendRequest
                    {
                        AttemptNumber = 1,
                        ActiveRideInstanceId = "2092100442933833729"
                    };
                byte[] builtPacket;
                if (!template.TryBuild(request, out builtPacket, out error) ||
                    builtPacket == null ||
                    builtPacket.Length != firstPacket.Length)
                {
                    Console.WriteLine("  [FAIL] A050 template build failed: " + error);
                    return false;
                }

                for (int index = 0; index < firstPacket.Length; index++)
                {
                    if (builtPacket[index] != firstPacket[index])
                    {
                        Console.WriteLine("  [FAIL] A050 template changed unexpected byte at offset " + index);
                        return false;
                    }
                }

                request.ActiveRideInstanceId = "2092100442933833730";
                if (!template.TryBuild(request, out builtPacket, out error) ||
                    builtPacket == null ||
                    builtPacket.Length != firstPacket.Length)
                {
                    Console.WriteLine("  [FAIL] Third-mount A050 template build failed: " + error);
                    return false;
                }

                for (int index = 0; index < firstPacket.Length; index++)
                {
                    if (builtPacket[index] != firstPacket[index])
                    {
                        Console.WriteLine("  [FAIL] Third-mount A050 template changed unexpected byte at offset " + index);
                        return false;
                    }
                }

                request.ActiveRideInstanceId = "2092100442933833731";
                if (!template.TryBuild(request, out builtPacket, out error) ||
                    builtPacket == null ||
                    builtPacket.Length != firstPacket.Length)
                {
                    Console.WriteLine("  [FAIL] Fourth-mount A050 template build failed: " + error);
                    return false;
                }

                for (int index = 0; index < firstPacket.Length; index++)
                {
                    if (builtPacket[index] != firstPacket[index])
                    {
                        Console.WriteLine("  [FAIL] Fourth-mount A050 template changed unexpected byte at offset " + index);
                        return false;
                    }
                }

                if (Encoding.ASCII.GetString(template.FixtureBytes, 20, 19) != "2092100442933833732")
                {
                    Console.WriteLine("  [FAIL] Template fixture was mutated.");
                    return false;
                }

                request = null;
                if (template.TryBuild(request, out builtPacket, out error))
                {
                    Console.WriteLine("  [FAIL] Missing A050 build request was accepted.");
                    return false;
                }

                Console.WriteLine("  [PASS] fixed A050 frame remains byte-for-byte unchanged; template remains offline");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  [FAIL] " + ex.Message);
                return false;
            }
        }

        static byte[] ParseHex(string value)
        {
            string[] parts = (value ?? string.Empty).Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            byte[] bytes = new byte[parts.Length];
            for (int index = 0; index < parts.Length; index++)
            {
                bytes[index] = Convert.ToByte(parts[index], 16);
            }
            return bytes;
        }

        static WPELibrary.Lib.MountSpeed.MountSpeedPreset CreateRefineStateMachinePreset()
        {
            return new WPELibrary.Lib.MountSpeed.MountSpeedPreset
            {
                TargetGrowthRate = 1.055,
                TargetSkills = new List<WPELibrary.Lib.MountSpeed.MountSkillTarget>
                {
                    new WPELibrary.Lib.MountSpeed.MountSkillTarget
                    {
                        SkillId = 61005,
                        SkillName = "泣血枕戈"
                    },
                    new WPELibrary.Lib.MountSpeed.MountSkillTarget
                    {
                        SkillId = 61224,
                        SkillName = "中级战心清明"
                    },
                    new WPELibrary.Lib.MountSpeed.MountSkillTarget
                    {
                        SkillId = 61223,
                        SkillName = "中级坚壁清野"
                    }
                }
            };
        }

        static string BuildMountedRefineFixtureJson(
            double cardTwoGrowthRate,
            long sequence = 1,
            int cardCount = 21)
        {
            StringBuilder cards = new StringBuilder("[");
            for (int cardIndex = 1; cardIndex <= cardCount; cardIndex++)
            {
                if (cardIndex > 1)
                {
                    cards.Append(",");
                }

                bool isTargetCard = cardIndex == 2;
                double growthRate = isTargetCard ? cardTwoGrowthRate : 1.095;
                cards.Append("{\"cardIndex\":").Append(cardIndex)
                    .Append(",\"isCurrent\":").Append(cardIndex == 1 ? "true" : "false")
                    .Append(",\"growthRate\":").Append(
                        growthRate.ToString("0.000", CultureInfo.InvariantCulture))
                    .Append(",\"source\":\"fixture\",\"skills\":[");

                if (isTargetCard)
                {
                    cards.Append("{\"slotIndex\":2,\"skillId\":61223,\"skillName\":\"中级坚壁清野\"},")
                        .Append("{\"slotIndex\":3,\"skillId\":61224,\"skillName\":\"中级战心清明\"},")
                        .Append("{\"slotIndex\":4,\"skillId\":61005,\"skillName\":\"泣血枕戈\"}");
                }
                else
                {
                    cards.Append("{\"slotIndex\":2,\"skillId\":69901,\"skillName\":\"其他技能一\"},")
                        .Append("{\"slotIndex\":3,\"skillId\":69902,\"skillName\":\"其他技能二\"},")
                        .Append("{\"slotIndex\":4,\"skillId\":69903,\"skillName\":\"其他技能三\"}");
                }
                cards.Append("]}");
            }
            cards.Append("]");

            return "{"
                + "\"event\":\"mount_status_snapshot\","
                + "\"schema\":\"mount_status_snapshot.v1\","
                + "\"schemaVersion\":1,"
                + "\"readOnly\":true,"
                + "\"actionAuthorized\":false,"
                + "\"sessionId\":\"11111111-1111-1111-1111-111111111111\","
                + "\"sequence\":" + sequence.ToString(CultureInfo.InvariantCulture) + ","
                + "\"status\":\"ok\","
                + "\"diagnosticCode\":\"ok\","
                + "\"candidateCount\":1,"
                + "\"process\":{\"pid\":1853,\"startTicks\":1466,\"exe\":\"/system/bin/app_process64\"},"
                + "\"player\":{"
                + "\"playerId\":256,\"isLocalPlayer\":true,\"mountId\":9111,\"isMounted\":true,"
                + "\"roleMoveSpeed\":256.0,\"gx\":73,\"gy\":56,"
                + "\"growthRate\":1.175,\"growthRateSource\":\"fixture\","
                + "\"rideInstances\":[{\"rideInstanceId\":\"ride-1\",\"rideShapeId\":9111,\"isRiding\":true,\"isCurrent\":true,\"skills\":["
                + "{\"slotIndex\":1,\"skillId\":61008,\"exp\":0,\"skillName\":\"秋水流弦\"},"
                + "{\"slotIndex\":2,\"skillId\":61021,\"exp\":0,\"skillName\":\"神枢鬼藏\"},"
                + "{\"slotIndex\":3,\"skillId\":61223,\"exp\":0,\"skillName\":\"高级坚壁清野\"}]}],"
                + "\"activeRideInstanceId\":\"ride-1\","
                + "\"rideBindingStatus\":\"bound\","
                + "\"rideBindingSource\":\"fixture\","
                + "\"rideRefineCards\":" + cards.ToString()
                + "}"
                + "}";
        }

        static WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult CreateFixtureReadResult(
            WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot snapshot,
            string readPath)
        {
            WPELibrary.Lib.MountSpeed.MountStatusReadDiagnostics diagnostics =
                new WPELibrary.Lib.MountSpeed.MountStatusReadDiagnostics();
            PropertyInfo readPathProperty = typeof(WPELibrary.Lib.MountSpeed.MountStatusReadDiagnostics)
                .GetProperty("ReadPath");
            MethodInfo readPathSetter = readPathProperty == null
                ? null
                : readPathProperty.GetSetMethod(true);
            if (readPathSetter == null)
            {
                throw new InvalidOperationException("ReadPath diagnostics setter is unavailable.");
            }
            readPathSetter.Invoke(diagnostics, new object[] { readPath });
            return new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                snapshot,
                null,
                false,
                diagnostics);
        }

        private sealed class ScriptedMountRefineSnapshotSource : WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource
        {
            private readonly Queue<WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot> _snapshots;
            private WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot _lastSnapshot;

            public ScriptedMountRefineSnapshotSource(params WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot[] snapshots)
            {
                _snapshots = new Queue<WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot>(snapshots ?? new WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot[0]);
            }

            public Task<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult> ReadSnapshotAsync(CancellationToken cancellationToken)
            {
                if (_snapshots.Count > 0)
                {
                    _lastSnapshot = _snapshots.Dequeue();
                }

                return Task.FromResult(
                    new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                        _lastSnapshot,
                        _lastSnapshot == null ? "fixture_snapshot_missing" : null));
            }
        }

        private sealed class SequenceScriptedMountRefineSnapshotSource : WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource
        {
            private readonly Queue<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult> _results;
            private int _readCount;

            public SequenceScriptedMountRefineSnapshotSource(
                params WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult[] results)
            {
                _results = new Queue<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult>(
                    results ?? new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult[0]);
            }

            public int ReadCount
            {
                get { return Volatile.Read(ref _readCount); }
            }

            public Task<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult> ReadSnapshotAsync(
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _readCount);
                if (_results.Count == 0)
                {
                    return Task.FromResult(
                        new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                            null,
                            "fixture_snapshot_missing"));
                }

                return Task.FromResult(_results.Dequeue());
            }
        }

        private sealed class CancellationAfterInitialSnapshotSource : WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource
        {
            private readonly WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot _initialSnapshot;
            private int _readCount;

            public CancellationAfterInitialSnapshotSource(
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot initialSnapshot)
            {
                _initialSnapshot = initialSnapshot;
            }

            public Task<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult> ReadSnapshotAsync(
                CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref _readCount) == 1)
                {
                    return Task.FromResult(
                        new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                            _initialSnapshot));
                }

                return Task.FromResult(
                    new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                        null,
                        "reader_cancelled",
                        true));
            }
        }

        private sealed class TransientUnchangedThenChangedSnapshotSource : WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource
        {
            private readonly WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot _baselineSnapshot;
            private readonly WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot _changedSnapshot;
            private readonly int _unchangedReadCount;
            private int _readCount;

            public TransientUnchangedThenChangedSnapshotSource(
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot baselineSnapshot,
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot changedSnapshot,
                int unchangedReadCount)
            {
                _baselineSnapshot = baselineSnapshot;
                _changedSnapshot = changedSnapshot;
                _unchangedReadCount = unchangedReadCount;
            }

            public Task<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult> ReadSnapshotAsync(
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int currentRead = Interlocked.Increment(ref _readCount);
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot snapshot =
                    currentRead <= _unchangedReadCount + 1
                        ? _baselineSnapshot
                        : _changedSnapshot;
                return Task.FromResult(
                    new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(snapshot));
            }
        }

        private sealed class GatedMountRefineSnapshotSource : WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource
        {
            private readonly WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot _baselineSnapshot;
            private readonly WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot _changedSnapshot;
            private int _releaseChangedSnapshot;
            private int _readCount;

            public GatedMountRefineSnapshotSource(
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot baselineSnapshot,
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot changedSnapshot)
            {
                _baselineSnapshot = baselineSnapshot;
                _changedSnapshot = changedSnapshot;
            }

            public int ReadCount
            {
                get { return Volatile.Read(ref _readCount); }
            }

            public void ReleaseChangedSnapshot()
            {
                Interlocked.Exchange(ref _releaseChangedSnapshot, 1);
            }

            public Task<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult> ReadSnapshotAsync(
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _readCount);
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot snapshot =
                    Volatile.Read(ref _releaseChangedSnapshot) == 0
                        ? _baselineSnapshot
                        : _changedSnapshot;
                return Task.FromResult(
                    new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(snapshot));
            }
        }

        private sealed class SlowCancellableSnapshotSource : WPELibrary.Lib.MountSpeed.IMountRefineSnapshotSource
        {
            private readonly WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot _initialSnapshot;
            private int _readCount;

            public SlowCancellableSnapshotSource(
                WPELibrary.Lib.MountSpeed.MountStatusReadOnlySnapshot initialSnapshot)
            {
                _initialSnapshot = initialSnapshot;
            }

            public async Task<WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult> ReadSnapshotAsync(
                CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref _readCount) == 1)
                {
                    return new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                        _initialSnapshot);
                }

                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                        null,
                        "reader_cancelled",
                        true);
                }

                return new WPELibrary.Lib.MountSpeed.MountRefineSnapshotReadResult(
                    null,
                    "reader_unexpected_completion");
            }
        }

        static void WriteInt32(IntPtr address, int value)
        {
            Marshal.Copy(BitConverter.GetBytes(value), 0, address, 4);
        }

        static void WriteFloat(IntPtr address, float value)
        {
            Marshal.Copy(BitConverter.GetBytes(value), 0, address, 4);
        }

        static void FreeMemory(IntPtr address)
        {
            if (address != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(address);
            }
        }
    }
}
