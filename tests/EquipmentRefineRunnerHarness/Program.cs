using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WPELibrary.Lib.EquipmentRefine;

namespace EquipmentRefineRunnerHarness
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                TestReaderFailsClosedWithoutVerifiedMapping();
                TestReaderUsesExactVerifiedFields();
                TestPacketPatcherAndRecordingSender();
                TestCaptured8008FrameAndAsciiEquipmentId();
                TestManualCaptureBindingAndHostSeam();
                TestStateMachineCompletesAfterChangedSnapshot();
                TestPresetPlanAndJsonRoundTrip();
                TestResponseEvaluatorReorderedCardAndKOfN();
                TestResponseEvaluatorStopsOnIncompleteOrSequenceFailure();
                TestStateMachineResponseModeStopsAfterOneSend();
                TestResultPresenterAndExplicitStopReasonMapper();
                TestEquipmentRefineResultFormatterDetails();
                TestRunnerPresetToStateMachinePath();
                TestRunnerResponseCardAndCancel();
                TestPresetEditorModelValidation();
                TestPresetDefaultHasFiveRuleRows();
                TestBagEquipmentOptionsUseVerifiedSnapshot();
                TestBagReaderBridgeCommandContract();
                TestResponseModeStopsBeforeSendWhenCurrentSnapshotMatches();
                TestResponseRejectsDuplicateOrMissingCardIndexes();
                TestUnconfiguredAndFailedResponseStopWithoutFollowup();
                TestHostQueuedResponseTwoRounds();
                TestHostDefaultStartIsFailClosed();
                TestWornTargetRequestIdentityFailClosed();
                TestWornTargetBeatsBackpackCandidate();
                TestJsonFixtureAdapterAndFailures();
                TestPresetRequiresWornSlotSelection();
                TestScreenshotTwentyCardLabels();
                TestScreenshotLabelsAssociatedWith7XlsOperation();
                TestParsedInventoryRequiresExplicitWornFlag();
                TestResponseModeRefreshesTargetBeforeNextRound();
                TestConfirmedRawFrameBoundaries();
                TestHistoricalResidentSnapshotRejected();
                TestResidentJsonlSyntheticReplay();
                TestMemoryResultModeQueuedResidentSource();
                TestMemoryResultModeFailClosedBoundaries();
                TestMemoryResultModeCancelStopsWait();
                TestResponseSourceTimeoutAndCancellationFailClosed();
                TestBagTargetSelectionAndIdentityBinding();
                TestBagNameDecodeAndPresetBinding();
                TestBagPresetEditorAndPlanRoundTrip();
                TestBagResidentCandidateAssociation();
                TestResidentUnknownPropertyKeyPreservesRawFields();
                TestBagRawTargetMetadataFailClosed();
                TestBagHostMemoryHitStopsWithoutFollowup();
                TestBagMemoryResultSessionMismatchStops();
                TestBagResponseCardPathPreservesIdentity();
                TestPresetRuleContractAndCloneCompleteness();
                TestRefineAttributeCatalog();
                Console.WriteLine("EquipmentRefineRunnerHarness: PASS (49 tests)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("EquipmentRefineRunnerHarness: FAIL");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static void TestReaderFailsClosedWithoutVerifiedMapping()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineAttributeReader reader = new EquipmentRefineAttributeReader(null);
            EquipmentAttributesSnapshot snapshot = reader.ReadAttributesAsync(
                slot,
                slot.BaseAttributeHash,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert(!snapshot.IsValid, "missing verified map must be invalid");
            Assert(snapshot.ReadError == "verified_attribute_mapping_missing", "missing map error mismatch");
        }

        private static void TestReaderUsesExactVerifiedFields()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineAttributeReader reader = new EquipmentRefineAttributeReader(
                new Dictionary<string, TargetAttribute>
                {
                    { "refine_attack", TargetAttribute.AttackPower }
                });
            EquipmentAttributesSnapshot snapshot = reader.ReadAttributesAsync(
                slot,
                slot.BaseAttributeHash,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert(snapshot.IsValid, "verified field should produce a valid snapshot");
            Assert(snapshot.GetValue(TargetAttribute.AttackPower) == 5, "attack value mismatch");
            Assert(!string.IsNullOrWhiteSpace(snapshot.RefineAttributeHash), "refine hash missing");

            EquipmentRefineDetector.EquipmentInventory inventory = CreateInventory(slot, "snap-1", 1, 5);
            Assert(EquipmentRefineDetector.FindBySlotIndex(inventory, 1) == slot, "slot_1 parsing failed");
        }

        private static void TestPacketPatcherAndRecordingSender()
        {
            EquipmentRefineExecutor.RefinePacketTemplate template = CreateTemplate();
            EquipmentRefineExecutor.RefineRequest request = new EquipmentRefineExecutor.RefineRequest
            {
                SlotIndex = 1,
                EquipmentId = "258",
                Type = EquipmentRefineExecutor.RefineType.Superior
            };

            byte[] packet;
            string error;
            Assert(EquipmentRefineExecutor.TryBuildRefinePacket(template, request, out packet, out error), error);
            Assert(packet.SequenceEqual(new byte[] { 0x01, 0x02, 0x01, 0xAA }), "packet field patch mismatch");

            EquipmentRefineExecutor.RecordingPacketSender sender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineExecutor executor = new EquipmentRefineExecutor(template, sender);
            EquipmentRefineExecutor.RefineSendResult sendResult = executor.SendRefinePacketAsync(
                request,
                -1,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert(sendResult == EquipmentRefineExecutor.RefineSendResult.Accepted, "recording send was not accepted");
            Assert(sender.SentPackets.Count == 1, "recording sender did not record one packet");
        }

        private static void TestStateMachineCompletesAfterChangedSnapshot()
        {
            EquipmentRefineDetector.EquipmentSlot before = CreateSlot(5);
            EquipmentRefineDetector.EquipmentSlot after = CreateSlot(12);
            EquipmentRefineDetector.EquipmentInventory initial = CreateInventory(before, "snap-1", 1, 5);
            EquipmentRefineDetector.EquipmentInventory changed = CreateInventory(after, "snap-2", 2, 12);

            EquipmentRefineExecutor.RecordingPacketSender sender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineExecutor executor = new EquipmentRefineExecutor(CreateTemplate(), sender);
            EquipmentRefineStateMachine.RefineConfiguration configuration =
                new EquipmentRefineStateMachine.RefineConfiguration
                {
                    Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1", MemberIdentity = "item-1", EquipmentId = "258" },
                    VerifiedAttributeFields = new Dictionary<string, TargetAttribute>
                    {
                        { "refine_attack", TargetAttribute.AttackPower }
                    },
                    ActiveRules = new List<RefineRule>
                    {
                        new RefineRule
                        {
                            Attribute = TargetAttribute.AttackPower,
                            Operator = AttributeOperator.GreaterThanOrEqual,
                            TargetValue = 10
                        }
                    },
                    MaxAttempts = 2,
                    RequiredMatches = 1,
                    ResponseCardMode = false,
                    IntervalMs = 0,
                    ResultConfirmTimeoutMs = 250,
                    SkipTargetReached = true
                };

            EquipmentRefineStateMachine machine = new EquipmentRefineStateMachine(
                configuration,
                executor,
                token => Task.FromResult(sender.SentPackets.Count == 0 ? initial : changed));
            EquipmentRefineStateMachine.ExecutionResult result = machine.RunAsync(CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert(result.Success, "state machine did not complete successfully: " + result.Message);
            Assert(result.StopReason == EquipmentRefineStateMachine.StopReason.TargetReached, "stop reason mismatch");
            Assert(result.AttemptCount == 1, "attempt count mismatch");
            Assert(result.Steps.Count >= 4, "state history missing");
            Assert(result.FinalAttributes != null && result.FinalAttributes.GetValue(TargetAttribute.AttackPower) == 12,
                "final snapshot mismatch");
        }

        private static void TestCaptured8008FrameAndAsciiEquipmentId()
        {
            byte[] captured = new byte[]
            {
                0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x22, 0x80, 0x08, 0x00, 0x00, 0x00, 0x05,
                0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x0B, 0xBB,
                0x13, 0x32, 0x30, 0x39, 0x31, 0x38, 0x31, 0x31,
                0x33, 0x37, 0x38, 0x34, 0x32, 0x32, 0x38, 0x37,
                0x30, 0x30, 0x31, 0x36
            };

            Assert(captured.Length == 44, "captured 8008 frame length mismatch");
            Assert(captured[0] == 0x4D && captured[1] == 0x5A, "captured MZ header mismatch");
            Assert(ReadUInt16BigEndian(captured, 10) == 0x8008, "captured protocol mismatch");
            Assert(ReadUInt16BigEndian(captured, 8) == 0x22, "captured body length mismatch");
            Assert(ReadInt32BigEndian(captured, 12) == 5, "captured first observed value mismatch");
            Assert(ReadInt32BigEndian(captured, 16) == 4, "captured second observed value mismatch");
            Assert(ReadInt32BigEndian(captured, 20) == 3003, "captured third observed value mismatch");
            Assert(captured[24] == 19, "captured equipment id length mismatch");
            Assert(Encoding.ASCII.GetString(captured, 25, 19) == "2091811378422870016",
                "captured equipment id mismatch");

            EquipmentRefineExecutor.RefinePacketTemplate template =
                new EquipmentRefineExecutor.RefinePacketTemplate
                {
                    ProtocolId = 0x8008,
                    FixtureBytes = captured,
                    ProtocolVerified = true,
                    EvidenceId = "manual-capture-20260824-8008-ascii-id",
                    Fields = new List<EquipmentRefineExecutor.RefinePacketField>
                    {
                        new EquipmentRefineExecutor.RefinePacketField
                        {
                            Name = "bagPos",
                            Offset = 12,
                            Length = 4,
                            Source = EquipmentRefineExecutor.PacketFieldSource.SlotIndex,
                            ByteOrder = EquipmentRefineExecutor.PacketByteOrder.BigEndian,
                            Verified = true
                        },
                        new EquipmentRefineExecutor.RefinePacketField
                        {
                            Name = "typeCode",
                            Offset = 16,
                            Length = 4,
                            Source = EquipmentRefineExecutor.PacketFieldSource.TypeCode,
                            ByteOrder = EquipmentRefineExecutor.PacketByteOrder.BigEndian,
                            Verified = true
                        },
                        new EquipmentRefineExecutor.RefinePacketField
                        {
                            Name = "operationCode",
                            Offset = 20,
                            Length = 4,
                            Source = EquipmentRefineExecutor.PacketFieldSource.OperationCode,
                            ByteOrder = EquipmentRefineExecutor.PacketByteOrder.BigEndian,
                            Verified = true
                        },
                        new EquipmentRefineExecutor.RefinePacketField
                        {
                            Name = "equipmentIdAscii",
                            Offset = 25,
                            Length = 19,
                            Source = EquipmentRefineExecutor.PacketFieldSource.EquipmentIdAscii,
                            Verified = true
                        }
                    }
                };

            EquipmentRefineExecutor.RefineRequest request =
                new EquipmentRefineExecutor.RefineRequest
                {
                    SlotIndex = 5,
                    EquipmentId = "2091811378422870016",
                    TypeCode = 4,
                    OperationCode = 3003
                };

            byte[] patched;
            string error;
            Assert(EquipmentRefineExecutor.TryBuildRefinePacket(template, request, out patched, out error), error);
            Assert(ReadUInt16BigEndian(patched, 10) == 0x8008, "patched protocol changed");
            Assert(Encoding.ASCII.GetString(patched, 25, 19) == request.EquipmentId,
                "ASCII equipment id patch mismatch");
            Assert(ReadInt32BigEndian(patched, 12) == 5 &&
                ReadInt32BigEndian(patched, 16) == 4 &&
                ReadInt32BigEndian(patched, 20) == 3003,
                "unmapped observed values changed");

            byte[] secondCaptured = new byte[]
            {
                0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x22, 0x80, 0x08, 0x00, 0x00, 0x00, 0x04,
                0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x0B, 0xBB,
                0x13, 0x32, 0x30, 0x39, 0x31, 0x38, 0x31, 0x31,
                0x33, 0x37, 0x38, 0x34, 0x31, 0x38, 0x36, 0x37,
                0x35, 0x37, 0x31, 0x35
            };
            EquipmentRefineExecutor.RefineRequest secondRequest =
                new EquipmentRefineExecutor.RefineRequest
                {
                    SlotIndex = 4,
                    EquipmentId = "2091811378418675715",
                    TypeCode = 4,
                    OperationCode = 3003
                };
            Assert(EquipmentRefineExecutor.TryBuildRefinePacket(
                template,
                secondRequest,
                out patched,
                out error), error);
            Assert(patched.SequenceEqual(secondCaptured),
                "second captured slot/equipment packet did not reproduce");

            byte[] thirdCaptured = new byte[]
            {
                0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x22, 0x80, 0x08, 0x00, 0x00, 0x00, 0x05,
                0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x0B, 0xB9,
                0x13, 0x32, 0x30, 0x39, 0x31, 0x38, 0x31, 0x31,
                0x33, 0x37, 0x38, 0x34, 0x32, 0x32, 0x38, 0x37,
                0x30, 0x30, 0x31, 0x36
            };
            EquipmentRefineExecutor.RefineRequest thirdRequest =
                new EquipmentRefineExecutor.RefineRequest
                {
                    SlotIndex = 5,
                    EquipmentId = "2091811378422870016",
                    TypeCode = 4,
                    OperationCode = 3001
                };
            Assert(EquipmentRefineExecutor.TryBuildRefinePacket(
                template,
                thirdRequest,
                out patched,
                out error), error);
            Assert(patched.SequenceEqual(thirdCaptured),
                "third captured operation packet did not reproduce");

            byte[] replacementCaptured = new byte[]
            {
                0x4D, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x22, 0x80, 0x08, 0x00, 0x00, 0x00, 0x05,
                0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x01,
                0x13, 0x32, 0x30, 0x39, 0x31, 0x38, 0x31, 0x31,
                0x33, 0x37, 0x38, 0x34, 0x32, 0x32, 0x38, 0x37,
                0x30, 0x30, 0x31, 0x36
            };
            EquipmentRefineExecutor.RefineRequest replacementRequest =
                new EquipmentRefineExecutor.RefineRequest
                {
                    SlotIndex = 5,
                    EquipmentId = "2091811378422870016",
                    TypeCode = 16,
                    OperationCode = 1
                };
            Assert(EquipmentRefineExecutor.TryBuildRefinePacket(
                template,
                replacementRequest,
                out patched,
                out error), error);
            Assert(patched.SequenceEqual(replacementCaptured),
                "replacement-attribute packet did not reproduce");
        }

        private static void TestManualCaptureBindingAndHostSeam()
        {
            string equipmentId = "2092100442958999552";
            byte[] bytes = new byte[44];
            bytes[0] = 0x4D;
            bytes[1] = 0x5A;
            bytes[9] = 0x22;
            bytes[10] = 0x80;
            bytes[11] = 0x08;
            bytes[24] = 0x13;
            Encoding.ASCII.GetBytes(equipmentId).CopyTo(bytes, 25);

            EquipmentRefineManualCapturePacket captured = new EquipmentRefineManualCapturePacket
            {
                PacketTimeUtc = DateTime.UtcNow,
                PacketSocket = 17,
                IsOutbound = true,
                PacketType = "WS2_Send",
                PacketFrom = "127.0.0.1:1000",
                PacketTo = "127.0.0.1:2000",
                Bytes = bytes
            };
            EquipmentRefineManualCaptureBinding binding;
            string error;
            Assert(EquipmentRefineManualCaptureBinding.TryCreate(captured, out binding, out error), error);
            Assert(binding.EquipmentId == equipmentId && binding.RawFrame.SequenceEqual(bytes) &&
                binding.PacketSocket == 17, "manual capture did not preserve ID, raw frame, or socket");
            EquipmentRefineManualCaptureBinding validBinding = binding;

            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                BagTargetMode = true,
                BagTarget = new EquipmentRefineDetector.BagTargetSelector
                {
                    Slot = "slot_1",
                    MemberIdentity = "bag-member-1"
                },
                Rules = new List<RefineRule>
                {
                    new RefineRule
                    {
                        Attribute = TargetAttribute.AttackPower,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 10,
                        Enabled = true
                    }
                },
                RequiredMatches = 1,
                MaxAttempts = 1,
                IntervalMs = 0
            };
            Assert(preset.IsValid(out error), error);

            EquipmentRefineManualCapturePacket stale = new EquipmentRefineManualCapturePacket
            {
                PacketTimeUtc = captured.PacketTimeUtc.AddSeconds(-1),
                PacketSocket = captured.PacketSocket,
                IsOutbound = true,
                PacketType = captured.PacketType,
                PacketFrom = captured.PacketFrom,
                PacketTo = captured.PacketTo,
                Bytes = bytes
            };
            EquipmentRefineManualCaptureSource source = new EquipmentRefineManualCaptureSource(
                () => new[] { stale, captured },
                1);
            EquipmentRefineHost host = new EquipmentRefineHost(
                preset,
                new EquipmentRefineExecutor(),
                token => Task.FromResult<EquipmentRefineDetector.EquipmentInventory>(null),
                null,
                null,
                null,
                null);
            EquipmentRefineManualCaptureBinding.CaptureResult result =
                host.CaptureAndBindManualRefineAsync(
                    source,
                    captured.PacketTimeUtc.AddMilliseconds(-1),
                    100,
                    CancellationToken.None).GetAwaiter().GetResult();
            Assert(result.Success && result.Binding != null && result.Binding.EquipmentId == equipmentId,
                "host manual capture seam did not bind the observed ID");

            EquipmentRefineManualCapturePacket inbound = new EquipmentRefineManualCapturePacket
            {
                PacketTimeUtc = DateTime.UtcNow,
                PacketSocket = 17,
                IsOutbound = false,
                PacketTo = "127.0.0.1:2000",
                Bytes = bytes
            };
            Assert(!EquipmentRefineManualCaptureBinding.TryCreate(inbound, out binding, out error) &&
                error == "manual_capture_direction_invalid",
                "manual capture must reject inbound packets");

            preset.BagTarget.ItemId = "different-id";
            Assert(!EquipmentRefineManualCaptureBinding.TryBindPreset(preset, validBinding, out error) &&
                error == "manual_capture_equipment_id_mismatch",
                "manual capture must reject an existing conflicting item identity");
        }

        private static void TestResponseEvaluatorReorderedCardAndKOfN()
        {
            List<RefineRule> rules = new List<RefineRule>
            {
                new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10 },
                new RefineRule { Attribute = TargetAttribute.HitRate, Operator = AttributeOperator.Equal, TargetValue = 19 },
                new RefineRule { Attribute = TargetAttribute.DefensePower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 99 }
            };
            RefineResponseEnvelope response = CompleteResponse(7, new AttributeValue
            {
                Type = TargetAttribute.HitRate,
                CurrentValue = 19,
                RawValue = 19
            }, new AttributeValue
            {
                Type = TargetAttribute.AttackPower,
                CurrentValue = 12,
                RawValue = 12
            });
            RefineResponseResult result = RefineResponseEvaluator.Evaluate(response, rules, 2, "equip-1", 6);
            Assert(result.Success, "reordered card should reach K-of-N");
            Assert(result.MatchedCount == 2 && result.MatchedRuleIndexes.SequenceEqual(new[] { 0, 1 }), "matched rules mismatch");
            Assert(!result.Replaced, "response evaluator must never replace attributes");
        }

        private static void TestResponseEvaluatorStopsOnIncompleteOrSequenceFailure()
        {
            List<RefineRule> rules = new List<RefineRule>
            {
                new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 1 }
            };
            RefineResponseEnvelope incomplete = CompleteResponse(7, new AttributeValue
            {
                Type = TargetAttribute.AttackPower, CurrentValue = 10, RawValue = 10
            });
            incomplete.Cards.RemoveAt(19);
            RefineResponseResult incompleteResult = RefineResponseEvaluator.Evaluate(incomplete, rules, 1, "equip-1", 6);
            Assert(incompleteResult.Reason == "response_cards_incomplete", "incomplete cards must fail closed");
            RefineResponseResult staleResult = RefineResponseEvaluator.Evaluate(CompleteResponse(6), rules, 1, "equip-1", 6);
            Assert(staleResult.Reason == "response_sequence_unstable_or_unchanged", "stale sequence must fail closed");
            RefineResponseResult equalBaselineResult = RefineResponseEvaluator.Evaluate(CompleteResponse(1), rules, 1, "equip-1", 1);
            Assert(equalBaselineResult.Reason == "response_sequence_unstable_or_unchanged", "equal pre-send baseline must fail closed");
        }

        private static void TestStateMachineResponseModeStopsAfterOneSend()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineDetector.EquipmentInventory inventory = CreateInventory(slot, "snap-1", 1, 5);
            EquipmentRefineExecutor.RecordingPacketSender sender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineStateMachine.RefineConfiguration configuration =
                new EquipmentRefineStateMachine.RefineConfiguration
                {
                    Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1" },
                    VerifiedAttributeFields = new Dictionary<string, TargetAttribute>
                    {
                        { "refine_attack", TargetAttribute.AttackPower }
                    },
                    ActiveRules = new List<RefineRule>
                    {
                        new RefineRule
                        {
                            Attribute = TargetAttribute.AttackPower,
                            Operator = AttributeOperator.GreaterThanOrEqual,
                            TargetValue = 10
                        }
                    },
                    RequiredMatches = 1,
                    ResponseCardMode = true,
                    TypeCode = 4,
                    OperationCode = 3003,
                    MaxAttempts = 3,
                    IntervalMs = 0,
                    ResultConfirmTimeoutMs = 250
                };
            EquipmentRefineStateMachine machine = new EquipmentRefineStateMachine(
                configuration,
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(inventory));
            machine.ConfigureResponseAdapter(
                new FixtureResponseAdapter(slot.MemberIdentity),
                token => Task.FromResult(new byte[] { 0x90, 0x35 }));
            EquipmentRefineStateMachine.ExecutionResult result = machine.RunAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.Success, "response-mode state machine did not complete");
            Assert(sender.SentPackets.Count == 1, "response-mode hit must block follow-up sends");
            Assert(result.MatchedResponseCard != null && result.MatchedResponseCard.CardIndex == 1,
                "matched response card was not retained");
            Assert(result.MatchedCount == 1 && result.RequiredMatches == 1 && !result.Replaced,
                "response result metadata mismatch");
        }

        private static void TestResultPresenterAndExplicitStopReasonMapper()
        {
            StringEquipmentRefineResultPresenter presenter = new StringEquipmentRefineResultPresenter();
            EquipmentRefineStateMachine.ExecutionResult result = new EquipmentRefineStateMachine.ExecutionResult
            {
                FinalState = EquipmentRefineStateMachine.RefinementState.Completed,
                StopReason = EquipmentRefineStateMachine.StopReason.TargetReached,
                MatchedCount = 2,
                RequiredMatches = 2,
                MatchedRuleIndexes = new List<int> { 0, 2 },
                Replaced = false,
                MatchedResponseCard = new RefineResponseCard
                {
                    CardIndex = 3,
                    IsComplete = true
                }
            };
            result.MatchedResponseCard.Attributes.Add(new AttributeValue
            {
                Type = TargetAttribute.AttackPower,
                Name = "攻击力",
                CurrentValue = 12
            });
            presenter.Present(result);
            Assert(presenter.LastMessage.Contains("卡片3") && presenter.LastMessage.Contains("命中=2/2") &&
                presenter.LastMessage.Contains("攻击力=12") && presenter.LastMessage.Contains("命中规则=0,2") &&
                presenter.LastMessage.Contains("未替换属性"), "result presenter output incomplete");

            ExplicitRefineStopReasonMapper mapper = new ExplicitRefineStopReasonMapper(
                new Dictionary<string, string> { { "materials_insufficient", "材料不足" } });
            Assert(mapper.Map("materials_insufficient") == "材料不足", "explicit resource mapping failed");
            Assert(mapper.Map("unknown_code").Contains("未映射"), "unknown resource code must remain explicit");

            EquipmentRefineStateMachine failedMachine = new EquipmentRefineStateMachine();
            failedMachine.ConfigureResultPresenter(presenter);
            failedMachine.ConfigureStopReasonMapper(mapper);
            EquipmentRefineStateMachine.ExecutionResult failed = failedMachine.RunAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(failed.FinalState == EquipmentRefineStateMachine.RefinementState.Failed,
                "invalid configuration should fail");
            Assert(presenter.LastMessage.Contains("停止=") && presenter.LastMessage.Contains("未映射"),
                "mapped stop presentation was not invoked");

            string legacy = "EquipmentRefine|1|旧预设|3|0|1:2:10:1|0|3000|5000|1|0||c2xvdF8x||";
            EquipmentRefinePreset legacyDecoded = EquipmentRefinePresetPlan.DecodePresetData(legacy);
            Assert(legacyDecoded != null && legacyDecoded.RequiredMatches == 1 &&
                legacyDecoded.Target != null && legacyDecoded.Target.Slot == "slot_1",
                "legacy preset layout no longer decodes");
        }

        private static void TestEquipmentRefineResultFormatterDetails()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Rules = new List<RefineRule>
                {
                    new RefineRule
                    {
                        Attribute = TargetAttribute.RootBone,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 5,
                        Enabled = true
                    },
                    new RefineRule
                    {
                        Attribute = TargetAttribute.IgnoreResistSeal,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 20,
                        Enabled = true
                    }
                }
            };
            EquipmentRefineStateMachine.ExecutionResult result = new EquipmentRefineStateMachine.ExecutionResult
            {
                Success = true,
                StopReason = EquipmentRefineStateMachine.StopReason.TargetReached,
                AttemptCount = 1,
                MatchedRuleIndexes = new List<int> { 0, 1 },
                MatchedResponseCard = new RefineResponseCard
                {
                    CardIndex = 7,
                    IsComplete = true
                }
            };
            result.MatchedResponseCard.Attributes.Add(new AttributeValue
            {
                Type = TargetAttribute.RootBone,
                Name = "根骨",
                CurrentValue = 5,
                RawValue = 5,
                RawValueText = "5"
            });
            result.MatchedResponseCard.Attributes.Add(new AttributeValue
            {
                Type = TargetAttribute.IgnoreResistSeal,
                Name = "忽视抗封印",
                CurrentValue = 20,
                RawValue = 20,
                RawValueText = "20"
            });

            string message = EquipmentRefineResultFormatter.Format(result, preset);
            Assert(message.Contains("第 7 张卡片") &&
                message.Contains("根骨=5") &&
                message.Contains("忽视抗封印=2.0%") &&
                message.Contains("本次发送 1 次"),
                "equipment refine result formatter lost card index or matched Chinese properties");
        }

        private static void TestRunnerPresetToStateMachinePath()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1", MemberIdentity = "item-1", EquipmentId = "258" },
                Rules = new List<RefineRule>
                {
                    new RefineRule
                    {
                        Attribute = TargetAttribute.AttackPower,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 10,
                        Enabled = true
                    }
                },
                RequiredMatches = 1,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute>
                {
                    { "refine_attack", TargetAttribute.AttackPower }
                }
            };
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineExecutor.RecordingPacketSender sender =
                new EquipmentRefineExecutor.RecordingPacketSender(false);
            StringEquipmentRefineResultPresenter presenter = new StringEquipmentRefineResultPresenter();
            EquipmentRefineRunner runner = new EquipmentRefineRunner(
                preset,
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null,
                null,
                null,
                presenter,
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult result = runner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.StopReason == EquipmentRefineStateMachine.StopReason.ProtocolUnverified &&
                result.Message == "response_card_adapter_unconfigured",
                "runner must stop before send when response-card adapter is missing");
            Assert(sender.SentPackets.Count == 0, "unauthorized runner must not send");
            Assert(presenter.LastMessage.Contains("停止=") && presenter.LastMessage.Contains("未映射") &&
                presenter.LastMessage.Contains("response_card_adapter_unconfigured"),
                "runner did not surface mapped stop result");
        }

        private static void TestRunnerResponseCardAndCancel()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1", MemberIdentity = "item-1", EquipmentId = "258" },
                Rules = new List<RefineRule>
                {
                    new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true },
                    new RefineRule { Attribute = TargetAttribute.Speed, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true }
                },
                RequiredMatches = 1,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute> { { "refine_attack", TargetAttribute.AttackPower } }
            };
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            StringEquipmentRefineResultPresenter presenter = new StringEquipmentRefineResultPresenter();
            EquipmentRefineRunner runner = new EquipmentRefineRunner(
                preset,
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                new FixtureResponseAdapter(slot.MemberIdentity),
                token => Task.FromResult(new byte[] { 0x90, 0x35 }),
                null,
                presenter,
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string> { { "materials_insufficient", "材料不足" } }));
            EquipmentRefineStateMachine.ExecutionResult result = runner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.Success && result.MatchedResponseCard.CardIndex == 1 && result.MatchedCount == 1 &&
                result.RequiredMatches == 1 && !result.Replaced && sender.SentPackets.Count == 1,
                "runner response-card success path mismatch");
            Assert(presenter.LastMessage.Contains("卡片1") && presenter.LastMessage.Contains("未替换属性"),
                "runner presenter did not retain card details");

            EquipmentRefineExecutor.RecordingPacketSender cancelSender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner cancelRunner = new EquipmentRefineRunner(
                preset,
                new EquipmentRefineExecutor(CreateTemplate(), cancelSender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                new FixtureResponseAdapter(slot.MemberIdentity),
                async token => { await Task.Delay(Timeout.Infinite, token); return null; },
                null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            Task<EquipmentRefineStateMachine.ExecutionResult> pending = cancelRunner.StartAsync(CancellationToken.None);
            awaitBrief();
            cancelRunner.Cancel();
            EquipmentRefineStateMachine.ExecutionResult cancelled = pending.GetAwaiter().GetResult();
            Assert(cancelled.StopReason == EquipmentRefineStateMachine.StopReason.UserStopped &&
                cancelSender.SentPackets.Count == 1, "cancel must stop response wait without extra sends");
        }

        private static void TestPresetEditorModelValidation()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_3", MemberIdentity = "item-1", EquipmentId = "258" },
                Rules = new List<RefineRule>()
            };
            EquipmentRefinePresetEditorModel editor = new EquipmentRefinePresetEditorModel(preset);
            editor.SetTargetMemberIdentity("item-editor");
            editor.SetTargetEquipmentId("equipment-editor");
            string emptyRulesError;
            Assert(!preset.IsValid(out emptyRulesError) && emptyRulesError.Contains("至少需要一条"),
                "empty enabled-rule set must fail closed");
            editor.AddRule(TargetAttribute.AttackPower, AttributeOperator.GreaterThanOrEqual, 12, true);
            editor.AddRule(TargetAttribute.Speed, AttributeOperator.Equal, 7, true);
            editor.SetMemoryResultMode(true);
            Assert(editor.SetRequiredMatches(2), "editor K should accept 2 of 2");
            EquipmentRefinePreset saved;
            string error;
            Assert(editor.TryValidateAndSave(out saved, out error), error);
            Assert(saved.Target.Slot == "slot_3" && saved.Target.MemberIdentity == "item-editor" &&
                saved.Target.EquipmentId == "equipment-editor" && saved.Rules.Count == 2 && saved.RequiredMatches == 2,
                "editor did not preserve target identity/rules/K");
            Assert(saved.MemoryResultMode, "editor did not preserve memory result mode");
            Assert(!editor.SetRequiredMatches(3), "editor must reject K>N");
            bool rejected = false;
            try { editor.AddRule(TargetAttribute.AttackPower, AttributeOperator.GreaterThan, 1, true); }
            catch (ArgumentException) { rejected = true; }
            Assert(rejected, "editor must reject unsupported operator");

            EquipmentRefinePreset duplicate = saved.Clone();
            duplicate.Rules.Add(new RefineRule
            {
                Attribute = duplicate.Rules[0].Attribute,
                Operator = duplicate.Rules[0].Operator,
                TargetValue = duplicate.Rules[0].TargetValue,
                Enabled = true
            });
            Assert(!duplicate.IsValid(out error) && error.Contains("重复"),
                "duplicate enabled rules must fail closed");

            EquipmentRefinePreset unknown = saved.Clone();
            unknown.Rules[0].Attribute = TargetAttribute.Unknown;
            Assert(!unknown.IsValid(out error) && error.Contains("未知"),
                "unknown target attributes must fail closed");
        }

        private static void TestPresetDefaultHasFiveRuleRows()
        {
            EquipmentRefinePreset preset = EquipmentRefinePreset.CreateDefault();
            Assert(preset.Rules != null && preset.Rules.Count == 5,
                "default refine preset must contain five rule rows");
            Assert(preset.Rules[0].Enabled &&
                preset.Rules.Skip(1).All(rule => rule != null && !rule.Enabled),
                "only the first default rule should be enabled");
            Assert(preset.Rules.Select(rule => rule.Attribute).Distinct().Count() == 5,
                "default rule rows must not duplicate attributes");
        }

        private static void TestBagEquipmentOptionsUseVerifiedSnapshot()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateBagSlot(
                "bag-item-ui-1",
                "bag-type-ui-1",
                "测试武器");
            slot.EquipmentNameStatus = "decoded";
            slot.XianqiTier = 2;
            slot.XianqiTierLabel = "二阶仙器";
            slot.XianqiTierStatus = "decoded";
            EquipmentRefineDetector.EquipmentInventory inventory =
                CreateBagInventory(slot, "bag-ui-snap-1", 1);
            IList<EquipmentRefineBagOption> options =
                EquipmentRefinePresetEditor.BuildBagEquipmentOptions(inventory);
            Assert(options.Count == 1 && options[0].DisplayName.Contains("测试武器") &&
                options[0].DisplayName.Contains("二阶仙器") &&
                options[0].Slot == "slot_1" &&
                options[0].MemberIdentity == "bag-member-1" &&
                options[0].ItemId == "bag-item-ui-1" &&
                options[0].ItemTypeId == "bag-type-ui-1",
                "verified bag snapshot should provide name, tier and stable identity");

            inventory.BagMode = false;
            Assert(EquipmentRefinePresetEditor.BuildBagEquipmentOptions(inventory).Count == 0,
                "unverified/non-bag snapshot must not populate equipment options");
        }

        private static void TestBagReaderBridgeCommandContract()
        {
            EquipmentInventoryAndroidSnapshotReader reader =
                new EquipmentInventoryAndroidSnapshotReader(
                    "adb.exe",
                    "emulator-5554",
                    "com.gdoo.yzqcxy",
                    "python.exe",
                    "C:\\reader\\equipment_inventory_resident.py",
                    "/data/local/tmp/equipment-streamd",
                    "piaomiao.eqtest2",
                    28772);
            string command = reader.BuildCommandLine(
                "C:\\temp\\equipment.state.json",
                "C:\\temp\\equipment.jsonl");

            Assert(command.Contains("--inventory-only"),
                "bag reader must use inventory-only mode");
            Assert(command.Contains("--max-polls 1") &&
                command.Contains("--max-session-attempts 1"),
                "bag reader must be bounded to one read session");
            Assert(command.Contains("--package com.gdoo.yzqcxy") &&
                command.Contains("--serial emulator-5554"),
                "bag reader must bind the configured package and device");
            Assert(command.Contains("--remote-binary /data/local/tmp/equipment-streamd") &&
                command.Contains("--socket-name piaomiao.eqtest2") &&
                command.Contains("--port 28772"),
                "bag reader must preserve the explicit native transport settings");
            Assert(!command.Contains("--bag-slot") &&
                !command.Contains("--bag-member-identity"),
                "inventory listing must not guess or bind a bag target");
        }

        private static void TestResponseModeStopsBeforeSendWhenCurrentSnapshotMatches()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(12);
            EquipmentRefineDetector.EquipmentInventory inventory = CreateInventory(slot, "snap-1", 1, 12);
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineStateMachine.RefineConfiguration configuration = new EquipmentRefineStateMachine.RefineConfiguration
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1", MemberIdentity = "item-1", EquipmentId = "258" },
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute> { { "refine_attack", TargetAttribute.AttackPower } },
                ActiveRules = new List<RefineRule> { new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true } },
                RequiredMatches = 1,
                ResponseCardMode = true,
                MaxAttempts = 1,
                IntervalMs = 0
            };
            EquipmentRefineStateMachine machine = new EquipmentRefineStateMachine(configuration,
                new EquipmentRefineExecutor(CreateTemplate(), sender), token => Task.FromResult(inventory));
            machine.ConfigureResponseAdapter(new NonMatchingFixtureResponseAdapter(slot.MemberIdentity),
                token => Task.FromResult(new byte[] { 0x90, 0x35 }));
            EquipmentRefineStateMachine.ExecutionResult result = machine.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(!result.Success && result.StopReason == EquipmentRefineStateMachine.StopReason.MaxAttemptsReached &&
                result.AttemptCount == 1 && sender.SentPackets.Count == 1,
                "response mode must ignore the old current snapshot and require the post-send card result");
        }

        private static void TestResponseRejectsDuplicateOrMissingCardIndexes()
        {
            List<RefineRule> rules = new List<RefineRule>
            {
                new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 1, Enabled = true }
            };
            RefineResponseEnvelope duplicate = CompleteResponse(2);
            duplicate.Cards[19].CardIndex = duplicate.Cards[18].CardIndex;
            RefineResponseResult duplicateResult = RefineResponseEvaluator.Evaluate(duplicate, rules, 1, "equip-1", 1);
            Assert(duplicateResult.Reason == "response_cards_incomplete", "duplicate card indexes must fail closed");
            RefineResponseEnvelope missing = CompleteResponse(2);
            missing.Cards[19].CardIndex = 21;
            RefineResponseResult missingResult = RefineResponseEvaluator.Evaluate(missing, rules, 1, "equip-1", 1);
            Assert(missingResult.Reason == "response_cards_incomplete", "missing card index must fail closed");
        }

        private static void TestUnconfiguredAndFailedResponseStopWithoutFollowup()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1", MemberIdentity = "item-1", EquipmentId = "258" },
                Rules = new List<RefineRule> { new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true } },
                RequiredMatches = 1,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute> { { "refine_attack", TargetAttribute.AttackPower } }
            };
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner unconfigured = EquipmentRefineRunnerFactory.Create(
                preset,
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null,
                null,
                null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult missing = unconfigured.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(missing.StopReason == EquipmentRefineStateMachine.StopReason.ProtocolUnverified && sender.SentPackets.Count == 0,
                "unconfigured response adapter must stop before send");

            EquipmentRefineExecutor.RecordingPacketSender failedSender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            StringEquipmentRefineResultPresenter presenter = new StringEquipmentRefineResultPresenter();
            EquipmentRefineRunner failedRunner = EquipmentRefineRunnerFactory.Create(
                preset,
                new EquipmentRefineExecutor(CreateTemplate(), failedSender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                new FailingResponseAdapter("materials_insufficient"),
                token => Task.FromResult(new byte[] { 1 }),
                null,
                presenter,
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string> { { "materials_insufficient", "材料不足" } }));
            EquipmentRefineStateMachine.ExecutionResult resource = failedRunner.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(resource.StopReason == EquipmentRefineStateMachine.StopReason.ProtocolUnverified &&
                failedSender.SentPackets.Count == 1 && presenter.LastMessage.Contains("材料不足"),
                "mapped response failure should stop and present resource reason");
        }

        private static void TestHostQueuedResponseTwoRounds()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1", MemberIdentity = "item-1", EquipmentId = "258" },
                Rules = new List<RefineRule>
                {
                    new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true },
                    new RefineRule { Attribute = TargetAttribute.Speed, Operator = AttributeOperator.Equal, TargetValue = 7, Enabled = true }
                },
                RequiredMatches = 2,
                MaxAttempts = 3,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute> { { "refine_attack", TargetAttribute.AttackPower } }
            };
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            QueuedResponseSource source = new QueuedResponseSource(
                Encoding.UTF8.GetBytes(BuildJsonFixture(2, slot.MemberIdentity, true, string.Empty, 20, false)),
                Encoding.UTF8.GetBytes(BuildJsonFixture(3, slot.MemberIdentity, true, string.Empty, 20, true)));
            StringEquipmentRefineResultPresenter presenter = new StringEquipmentRefineResultPresenter();
            EquipmentRefineHost host = new EquipmentRefineHost(
                preset,
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                new JsonFixtureRefineResponseAdapter(),
                source,
                presenter,
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult result = host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(result.Success && sender.SentPackets.Count == 2 && source.ReceiveCount == 2,
                "queued response rounds did not send/receive exactly twice");
            Assert(result.MatchedResponseCard != null && result.MatchedResponseCard.CardIndex == 7 &&
                result.MatchedResponseCard.Attributes.Count == 2 && presenter.LastMessage.Contains("卡片7"),
                "second response hit details were not retained");
            Assert(source.Baselines.Count == 2 && source.Baselines[0] < 2 && source.Baselines[1] < 3,
                "response source did not receive valid per-round baselines");
        }

        private static void TestHostDefaultStartIsFailClosed()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1", MemberIdentity = "item-1", EquipmentId = "258" },
                Rules = new List<RefineRule> { new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true } },
                RequiredMatches = 1,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute> { { "refine_attack", TargetAttribute.AttackPower } }
            };
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineHost host = new EquipmentRefineHost(
                preset, new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null, null, new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult result = host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(result.StopReason == EquipmentRefineStateMachine.StopReason.ProtocolUnverified && sender.SentPackets.Count == 0,
                "default host must stop before sending without protocol adapter");
        }

        private static void TestHistoricalResidentSnapshotRejected()
        {
            string path = @"F:\项目目录\飘渺游戏助手\tools\equipment-reader\work\equipment-inventory-resident-state.json";
            Assert(File.Exists(path), "historical resident fixture is missing");
            string document = File.ReadAllText(path, Encoding.UTF8);
            Newtonsoft.Json.Linq.JObject historicalRoot = Newtonsoft.Json.Linq.JObject.Parse(document);
            Newtonsoft.Json.Linq.JObject historicalItem = historicalRoot["items"]
                .OfType<Newtonsoft.Json.Linq.JObject>().FirstOrDefault();
            Assert(historicalItem != null && historicalItem["isWorn"] == null &&
                historicalItem["equipmentId"] == null,
                "historical resident fixture must visibly lack isWorn/equipmentId");
            ResidentJsonlRefineMemoryResultSource source = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(document));
            RefineMemoryResultSnapshot result = source.ReceiveAfterSendAsync(
                new RefineMemoryBaseline
                {
                    ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = 1865, startTicks = 1423 },
                    ContainerIdentity = "historical-container",
                    StreamSessionId = "historical-session",
                    SnapshotId = "historical-snapshot",
                    Sequence = 1,
                    EquipmentIdentity = "item-1",
                    Slot = "slot_1",
                    EquipmentId = "258"
                },
                250,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert(result != null && !string.IsNullOrWhiteSpace(result.ErrorCode) &&
                result.Cards.Count == 0,
                "historical resident record must be rejected without supplementing isWorn/equipmentId");
        }

        private static void TestResidentJsonlSyntheticReplay()
        {
            const string identity = "item-1";
            const string slot = "slot_1";
            const string equipmentId = "258";
            string document = BuildResidentJsonFixture(identity, slot, equipmentId, 2, "resident-2", true);
            Dictionary<string, TargetAttribute> mapping = new Dictionary<string, TargetAttribute>
            {
                { "1001", TargetAttribute.AttackPower },
                { "1002", TargetAttribute.Speed }
            };
            RefineMemoryBaseline baseline = new RefineMemoryBaseline
            {
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ContainerIdentity = "container-1",
                StreamSessionId = "session-1",
                SnapshotId = "resident-1",
                Sequence = 1,
                EquipmentIdentity = identity,
                Slot = slot,
                EquipmentId = equipmentId
            };
            ResidentJsonlRefineMemoryResultSource directSource = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(document), mapping);
            RefineMemoryResultSnapshot decoded = directSource.ReceiveAfterSendAsync(
                baseline, 250, CancellationToken.None).GetAwaiter().GetResult();
            Assert(decoded != null && string.IsNullOrEmpty(decoded.ErrorCode) && decoded.Cards.Count == 20,
                "synthetic resident schema must decode exactly twenty cards");
            RefineResponseCard rawOnlyCard = decoded.Cards.Single(card => card.CardIndex == 1);
            Assert(rawOnlyCard.Attributes.Count == 1 && rawOnlyCard.Attributes[0].Type == TargetAttribute.Unknown &&
                rawOnlyCard.RawProperties.Count == 1 && rawOnlyCard.RawProperties[0].RawId == "9001",
                "unmapped resident raw property must be retained without becoming a target hit");
            RefineResponseCard hitCard = decoded.Cards.Single(card => card.CardIndex == 7);
            Assert(hitCard.Attributes.Count == 2 && hitCard.RawProperties.Count == 2 &&
                hitCard.RawProperties[0].RawId == "1002" && hitCard.RawProperties[1].RawId == "1001" &&
                hitCard.Attributes.Any(attribute => attribute.RawOrder == "1" && attribute.PropertyKey == "refine_attack") &&
                hitCard.Attributes.Any(attribute => attribute.RawOrder == "2" && attribute.PropertyKey == "refine_speed"),
                "resident raw id/order/property key fields or attribute order were not preserved");

            EquipmentRefineExecutor.RecordingPacketSender sender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            StringEquipmentRefineResultPresenter presenter = new StringEquipmentRefineResultPresenter();
            EquipmentRefineRunner runner = EquipmentRefineRunnerFactory.Create(
                CreateMemoryPreset(2),
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(CreateSlot(5), "resident-1", 1, 5)),
                null, null, null,
                presenter,
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                new ResidentJsonlRefineMemoryResultSource(token => Task.FromResult(document), mapping));
            EquipmentRefineStateMachine.ExecutionResult result = runner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.Success && result.MatchedResponseCard != null &&
                result.MatchedResponseCard.CardIndex == 7 && result.MatchedResponseCard.Attributes.Count == 2 &&
                result.MatchedRuleIndexes.Count == 2 && result.MatchedCount == 2 &&
                result.RequiredMatches == 2 && sender.SentPackets.Count == 1 && !result.Replaced,
                "resident schema replay did not complete through runner/state machine");
            Assert(presenter.LastMessage.Contains("卡片7") && presenter.LastMessage.Contains("攻击力") &&
                presenter.LastMessage.Contains("速度") && presenter.LastMessage.Contains("1001") &&
                presenter.LastMessage.Contains("1002") && presenter.LastMessage.Contains("未替换属性"),
                "resident replay presenter did not retain card details and raw fields");
        }

        private static void TestMemoryResultModeQueuedResidentSource()
        {
            EquipmentRefinePreset preset = CreateMemoryPreset(2);
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineExecutor.RecordingPacketSender sender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            RefineMemoryResultSnapshot first = CreateMemoryResult(2, "snap-2", slot.MemberIdentity,
                slot.Slot, slot.EquipmentId, false);
            RefineMemoryResultSnapshot second = CreateMemoryResult(3, "snap-3", slot.MemberIdentity,
                slot.Slot, slot.EquipmentId, true);
            QueuedMemoryResultSource source = new QueuedMemoryResultSource(first, second);
            StringEquipmentRefineResultPresenter presenter = new StringEquipmentRefineResultPresenter();
            EquipmentRefineRunner runner = EquipmentRefineRunnerFactory.Create(
                preset,
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null,
                null,
                null,
                presenter,
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                source);

            EquipmentRefineStateMachine.ExecutionResult result = runner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.Success && result.StopReason == EquipmentRefineStateMachine.StopReason.TargetReached,
                "memory result mode did not complete on a matching card");
            Assert(sender.SentPackets.Count == 2 && source.ReceiveCount == 2 && source.Baselines.Count == 2,
                "memory result mode must send/receive exactly two rounds");
            Assert(source.Baselines[0] == 1 && source.Baselines[1] == 1,
                "each memory round must use its own pre-send inventory baseline");
            Assert(result.MatchedResponseCard != null && result.MatchedResponseCard.CardIndex == 7 &&
                result.MatchedResponseCard.Attributes.Count == 2 &&
                result.MatchedRuleIndexes.Count == 2 && result.MatchedCount == 2 &&
                result.RequiredMatches == 2 && !result.Replaced,
                "memory hit must retain full card details and K-of-N result");
            Assert(presenter.LastMessage.Contains("卡片7") && presenter.LastMessage.Contains("攻击") &&
                presenter.LastMessage.Contains("速度") && presenter.LastMessage.Contains("未替换属性"),
                "memory result presenter did not retain card details");
        }

        private static void TestMemoryResultModeFailClosedBoundaries()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            RefineMemoryResultSnapshot valid = CreateMemoryResult(2, "snap-2", slot.MemberIdentity,
                slot.Slot, slot.EquipmentId, false);
            List<KeyValuePair<string, RefineMemoryResultSnapshot>> cases = new List<KeyValuePair<string, RefineMemoryResultSnapshot>>
            {
                new KeyValuePair<string, RefineMemoryResultSnapshot>("old-sequence", CreateMemoryResult(1, "snap-1", slot.MemberIdentity, slot.Slot, slot.EquipmentId, false)),
                new KeyValuePair<string, RefineMemoryResultSnapshot>("process", CopyMemoryResult(valid, processPid: 999)),
                new KeyValuePair<string, RefineMemoryResultSnapshot>("container", CopyMemoryResult(valid, container: "other-container")),
                new KeyValuePair<string, RefineMemoryResultSnapshot>("session", CopyMemoryResult(valid, session: "other-session")),
                new KeyValuePair<string, RefineMemoryResultSnapshot>("equipment", CopyMemoryResult(valid, identity: "other-item")),
                new KeyValuePair<string, RefineMemoryResultSnapshot>("unstable", CopyMemoryResult(valid, stable: false)),
                new KeyValuePair<string, RefineMemoryResultSnapshot>("incomplete", CopyMemoryResult(valid, cardCount: 19)),
                new KeyValuePair<string, RefineMemoryResultSnapshot>("duplicate-index", CopyMemoryResult(valid, duplicateIndex: true)),
                new KeyValuePair<string, RefineMemoryResultSnapshot>("error", CopyMemoryResult(valid, errorCode: "materials_insufficient"))
            };
            foreach (KeyValuePair<string, RefineMemoryResultSnapshot> item in cases)
            {
                EquipmentRefineExecutor.RecordingPacketSender sender =
                    new EquipmentRefineExecutor.RecordingPacketSender(true);
                EquipmentRefineRunner runner = EquipmentRefineRunnerFactory.Create(
                    CreateMemoryPreset(1),
                    new EquipmentRefineExecutor(CreateTemplate(), sender),
                    token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                    null, null, null,
                    new StringEquipmentRefineResultPresenter(),
                    new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                    new QueuedMemoryResultSource(item.Value));
                EquipmentRefineStateMachine.ExecutionResult result = runner.StartAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(!result.Success && result.StopReason == EquipmentRefineStateMachine.StopReason.ProtocolUnverified &&
                    sender.SentPackets.Count == 1,
                    "memory invalid result must stop without follow-up send [" + item.Key + "]");
            }

            EquipmentRefineExecutor.RecordingPacketSender unconfiguredSender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner unconfigured = EquipmentRefineRunnerFactory.Create(
                CreateMemoryPreset(1),
                new EquipmentRefineExecutor(CreateTemplate(), unconfiguredSender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null, null, null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult missing = unconfigured.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(missing.StopReason == EquipmentRefineStateMachine.StopReason.ProtocolUnverified &&
                missing.Message == "memory_result_source_unconfigured" && unconfiguredSender.SentPackets.Count == 0,
                "memory mode without source must stop before sending");

            EquipmentRefineExecutor.RecordingPacketSender timeoutSender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner timeoutRunner = EquipmentRefineRunnerFactory.Create(
                CreateMemoryPreset(1),
                new EquipmentRefineExecutor(CreateTemplate(), timeoutSender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null, null, null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                new ThrowingMemoryResultSource(new TimeoutException()));
            EquipmentRefineStateMachine.ExecutionResult timeout = timeoutRunner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(timeout.StopReason == EquipmentRefineStateMachine.StopReason.RefineResultTimeout &&
                timeoutSender.SentPackets.Count == 1,
                "memory timeout must be converted to a safe stop");

            EquipmentRefineExecutor.RecordingPacketSender exceptionSender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner exceptionRunner = EquipmentRefineRunnerFactory.Create(
                CreateMemoryPreset(1),
                new EquipmentRefineExecutor(CreateTemplate(), exceptionSender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null, null, null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                new ThrowingMemoryResultSource(new InvalidOperationException("source_failure")));
            EquipmentRefineStateMachine.ExecutionResult exception = exceptionRunner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(exception.StopReason == EquipmentRefineStateMachine.StopReason.InternalError &&
                exceptionSender.SentPackets.Count == 1,
                "memory source exception must fail closed without a follow-up send");

            EquipmentRefinePreset shortTimeoutPreset = CreateMemoryPreset(1);
            shortTimeoutPreset.ResultConfirmTimeoutMs = 10;
            EquipmentRefineExecutor.RecordingPacketSender shortTimeoutSender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner shortTimeoutRunner = EquipmentRefineRunnerFactory.Create(
                shortTimeoutPreset,
                new EquipmentRefineExecutor(CreateTemplate(), shortTimeoutSender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null, null, null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                new BlockingMemoryResultSource());
            EquipmentRefineStateMachine.ExecutionResult shortTimeout = shortTimeoutRunner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(shortTimeout.StopReason == EquipmentRefineStateMachine.StopReason.RefineResultTimeout &&
                shortTimeoutSender.SentPackets.Count == 1,
                "memory source timeout must be enforced by the state machine");
        }

        private static void TestMemoryResultModeCancelStopsWait()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            EquipmentRefineExecutor.RecordingPacketSender sender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner runner = EquipmentRefineRunnerFactory.Create(
                CreateMemoryPreset(1),
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)),
                null, null, null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                new BlockingMemoryResultSource());
            Task<EquipmentRefineStateMachine.ExecutionResult> running = runner.StartAsync(CancellationToken.None);
            for (int index = 0; index < 100 && sender.SentPackets.Count == 0; index++)
            {
                Thread.Sleep(5);
            }
            runner.Cancel();
            EquipmentRefineStateMachine.ExecutionResult result = running.GetAwaiter().GetResult();
            Assert(result.StopReason == EquipmentRefineStateMachine.StopReason.UserStopped &&
                sender.SentPackets.Count == 1,
                "memory cancel must return UserStopped without another send");
        }

        private static void TestResponseSourceTimeoutAndCancellationFailClosed()
        {
            EquipmentRefinePreset timeoutPreset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector
                {
                    Slot = "slot_1",
                    MemberIdentity = "item-1",
                    EquipmentId = "258"
                },
                Rules = new List<RefineRule>
                {
                    new RefineRule
                    {
                        Attribute = TargetAttribute.AttackPower,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 10,
                        Enabled = true
                    }
                },
                RequiredMatches = 1,
                MaxAttempts = 1,
                IntervalMs = 0,
                ResultConfirmTimeoutMs = 10,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute>
                {
                    { "refine_attack", TargetAttribute.AttackPower }
                }
            };
            EquipmentRefineExecutor.RecordingPacketSender timeoutSender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner timeoutRunner = EquipmentRefineRunnerFactory.Create(
                timeoutPreset,
                new EquipmentRefineExecutor(CreateTemplate(), timeoutSender),
                token => Task.FromResult(CreateInventory(CreateSlot(5), "snap-1", 1, 5)),
                new FixtureResponseAdapter("item-1"),
                null,
                new BlockingResponseSource(),
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult timeout = timeoutRunner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(timeout.StopReason == EquipmentRefineStateMachine.StopReason.RefineResultTimeout &&
                timeout.Message == "refine_result_timeout" &&
                timeoutSender.SentPackets.Count == 1,
                "response source timeout must stop without a follow-up send");

            CancellationTokenSource cancellation = new CancellationTokenSource();
            EquipmentRefineExecutor.RecordingPacketSender cancelSender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineRunner cancelRunner = EquipmentRefineRunnerFactory.Create(
                timeoutPreset,
                new EquipmentRefineExecutor(CreateTemplate(), cancelSender),
                token => Task.FromResult(CreateInventory(CreateSlot(5), "snap-1", 1, 5)),
                new FixtureResponseAdapter("item-1"),
                null,
                new CancelThenReturnResponseSource(
                    () => cancellation.Cancel(),
                    new byte[] { 0x90, 0x35 }),
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult cancelled = cancelRunner
                .StartAsync(cancellation.Token).GetAwaiter().GetResult();
            Assert(cancelled.StopReason == EquipmentRefineStateMachine.StopReason.UserStopped &&
                !cancelled.Success && cancelSender.SentPackets.Count == 1,
                "response cancellation must win over a concurrently completed response");
        }

        private static void TestWornTargetRequestIdentityFailClosed()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector { Slot = "slot_1", MemberIdentity = "item-1", EquipmentId = "258" },
                Rules = new List<RefineRule> { new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true } },
                RequiredMatches = 1,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute> { { "refine_attack", TargetAttribute.AttackPower } }
            };
            EquipmentRefineDetector.EquipmentSlot slot = CreateSlot(5);
            slot.MemberIdentity = "different-item";
            slot.EquipmentId = "different-id";
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefineHost host = new EquipmentRefineHost(preset, new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(CreateInventory(slot, "snap-1", 1, 5)), new FixtureResponseAdapter(slot.MemberIdentity),
                new QueuedResponseSource(new byte[] { 1 }), new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult result = host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert(result.StopReason == EquipmentRefineStateMachine.StopReason.EquipmentNotFound &&
                result.Message == "target_equipment_not_found_or_ambiguous" && sender.SentPackets.Count == 0,
                "mismatched worn target identity must fail closed before request");
        }

        private static void TestJsonFixtureAdapterAndFailures()
        {
            JsonFixtureRefineResponseAdapter adapter = new JsonFixtureRefineResponseAdapter();
            string valid = BuildJsonFixture(2, "equip-1", true, string.Empty, 20, true);
            RefineResponseEnvelope response;
            string error;
            Assert(adapter.TryDecode(Encoding.UTF8.GetBytes(valid), out response, out error), error);
            Assert(response.Cards.Count == 20 && response.Cards[0].CardIndex == 20,
                "fixture adapter did not preserve card order");
            List<RefineRule> rules = new List<RefineRule>
            {
                new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true },
                new RefineRule { Attribute = TargetAttribute.Speed, Operator = AttributeOperator.Equal, TargetValue = 7, Enabled = true }
            };
            RefineResponseResult evaluated = RefineResponseEvaluator.Evaluate(response, rules, 2, "equip-1", 1);
            Assert(evaluated.Success && evaluated.MatchedCard != null && evaluated.MatchedCard.Attributes.Count == 2,
                "valid JSON fixture did not satisfy K-of-N");

            KeyValuePair<string, string>[] schemaInvalidFrames =
            {
                new KeyValuePair<string, string>("bad-json", "not-json"),
                new KeyValuePair<string, string>("nineteen-cards", BuildJsonFixture(2, "equip-1", true, string.Empty, 19, true)),
                new KeyValuePair<string, string>("duplicate-index", BuildJsonFixtureWithDuplicateIndex()),
                new KeyValuePair<string, string>("out-of-range-index", BuildJsonFixtureWithOutOfRangeIndex()),
                new KeyValuePair<string, string>("missing-required", BuildJsonFixtureWithMissingRequiredField()),
                new KeyValuePair<string, string>("unknown-top-level", BuildJsonFixtureWithExtraTopLevelField()),
                new KeyValuePair<string, string>("unknown-card", BuildJsonFixtureWithExtraCardField()),
                new KeyValuePair<string, string>("unknown-attribute", BuildJsonFixtureWithExtraAttributeField()),
                new KeyValuePair<string, string>("unknown-attribute-type", BuildJsonFixtureWithUnknownAttributeType())
            };
            foreach (KeyValuePair<string, string> invalid in schemaInvalidFrames)
            {
                RefineResponseEnvelope decoded;
                string decodeError;
                bool decodedOk = adapter.TryDecode(Encoding.UTF8.GetBytes(invalid.Value), out decoded, out decodeError);
                Assert(!decodedOk, "schema-invalid fixture must be rejected by adapter [" + invalid.Key + "]: " + invalid.Value);
            }

            string[] semanticInvalidFrames =
            {
                BuildJsonFixture(2, "wrong", true, string.Empty, 20, true),
                BuildJsonFixture(1, "equip-1", true, string.Empty, 20, true),
                BuildJsonFixture(2, "equip-1", false, string.Empty, 20, true)
            };
            foreach (string invalid in semanticInvalidFrames)
            {
                RefineResponseEnvelope decoded;
                string decodeError;
                Assert(adapter.TryDecode(Encoding.UTF8.GetBytes(invalid), out decoded, out decodeError), decodeError);
                RefineResponseResult semanticResult = RefineResponseEvaluator.Evaluate(decoded, rules, 2, "equip-1", 1);
                Assert(!semanticResult.Success, "semantic-invalid fixture must fail closed: " + invalid);
            }

            RefineResponseEnvelope nonHitResponse;
            string nonHitDecode;
            Assert(adapter.TryDecode(Encoding.UTF8.GetBytes(BuildJsonFixture(2, "equip-1", true, string.Empty, 20, false)), out nonHitResponse, out nonHitDecode), nonHitDecode);
            RefineResponseResult nonHitResult = RefineResponseEvaluator.Evaluate(nonHitResponse, rules, 2, "equip-1", 1);
            Assert(!nonHitResult.Success && nonHitResult.ContinueAllowed, "complete non-hit response must continue");

            RefineResponseEnvelope errorResponse;
            string errorDecode;
            Assert(adapter.TryDecode(Encoding.UTF8.GetBytes(BuildJsonFixture(2, "equip-1", true, "materials_insufficient", 0, false)), out errorResponse, out errorDecode), errorDecode);
            RefineResponseResult errorResult = RefineResponseEvaluator.Evaluate(errorResponse, rules, 2, "equip-1", 1);
            Assert(!errorResult.Success && errorResult.Reason == "materials_insufficient", "error response must remain safely displayable");
        }

        private static void TestParsedInventoryRequiresExplicitWornFlag()
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(
                "{\"event\":\"equipment_inventory_resident_state\",\"schemaVersion\":3,\"available\":true,\"readOnly\":true,\"actionAuthorized\":false,\"snapshotId\":\"snap-1\",\"streamSessionId\":\"session-1\",\"containerIdentity\":\"container-1\",\"sequence\":1,\"updatedAtUnixMs\":1,\"binding\":{\"pid\":100,\"startTicks\":200},\"processIdentity\":{\"pid\":100,\"startTicks\":200},\"wornOnly\":true,\"itemCount\":1,\"items\":[{\"candidateOnly\":true,\"memberIdentity\":\"item-1\",\"slot\":\"slot_1\",\"equipmentId\":\"258\",\"rawFields\":[{\"key\":\"refine_attack\",\"kind\":\"int\",\"value\":\"5\"}]}]}");
            Assert(EquipmentRefineDetector.ParseInventory(root.ToString()) == null,
                "resident inventory without explicit isWorn=true must fail closed");

            ((Newtonsoft.Json.Linq.JObject)root["items"][0])["isWorn"] = true;
            EquipmentRefineDetector.EquipmentInventory parsed = EquipmentRefineDetector.ParseInventory(root.ToString());
            Assert(parsed != null && parsed.Items.Count == 1 && parsed.Items[0].IsWorn,
                "resident inventory must preserve explicit isWorn=true");

            ((Newtonsoft.Json.Linq.JObject)root["items"][0]).Remove("equipmentId");
            Assert(EquipmentRefineDetector.ParseInventory(root.ToString()) == null,
                "resident inventory without equipmentId must fail closed");

            ((Newtonsoft.Json.Linq.JObject)root["items"][0])["equipId"] = "258";
            Assert(EquipmentRefineDetector.ParseInventory(root.ToString()) == null,
                "resident inventory must not substitute equipId for equipmentId");
        }

        private static void TestResponseModeRefreshesTargetBeforeNextRound()
        {
            EquipmentRefineDetector.EquipmentSlot initialSlot = CreateSlot(5);
            EquipmentRefineDetector.EquipmentSlot changedSlot = CreateSlot(5);
            changedSlot.EquipmentId = "different-equipment";
            changedSlot.BaseAttributeHash = EquipmentRefineDetector.CalculateIdentityHash(
                changedSlot.MemberIdentity, changedSlot.Slot, changedSlot.EquipmentId);
            EquipmentRefineDetector.EquipmentInventory initial = CreateInventory(initialSlot, "snap-1", 1, 5);
            EquipmentRefineDetector.EquipmentInventory changed = CreateInventory(changedSlot, "snap-2", 2, 5);
            int inventoryReads = 0;
            EquipmentRefineExecutor.RecordingPacketSender sender =
                new EquipmentRefineExecutor.RecordingPacketSender(true);
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Target = new EquipmentRefineDetector.EquipmentTargetSelector
                {
                    Slot = "slot_1",
                    MemberIdentity = "item-1",
                    EquipmentId = "258"
                },
                Rules = new List<RefineRule>
                {
                    new RefineRule
                    {
                        Attribute = TargetAttribute.AttackPower,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 10,
                        Enabled = true
                    }
                },
                RequiredMatches = 1,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute>
                {
                    { "refine_attack", TargetAttribute.AttackPower }
                }
            };
            EquipmentRefineRunner runner = new EquipmentRefineRunner(
                preset,
                new EquipmentRefineExecutor(CreateTemplate(), sender),
                token => Task.FromResult(++inventoryReads == 1 ? initial : changed),
                new NonMatchingFixtureResponseAdapter(initialSlot.MemberIdentity),
                token => Task.FromResult(new byte[] { 1, 2 }),
                null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult result = runner.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.StopReason == EquipmentRefineStateMachine.StopReason.EquipmentIdentityChanged &&
                result.Message == "target_identity_changed_after_response" &&
                sender.SentPackets.Count == 1 && inventoryReads == 2,
                "response-card mode must refresh and validate target before another send");
        }

        private static void TestConfirmedRawFrameBoundaries()
        {
            byte[] first = BuildRawFrame(0x0039, 0xFF2F, 0x11);
            byte[] second = BuildRawFrame(0x004E, 0xE898, 0x22);
            byte[] combined = first.Concat(second).ToArray();
            List<EquipmentRefineRawFrame> frames;
            string error;
            Assert(EquipmentRefineFrameFramer.TryParseConcatenated(combined, out frames, out error),
                "confirmed 155-byte framing sample should parse");
            Assert(frames.Count == 2, "155-byte framing sample should contain two raw frames");
            Assert(frames[0].Offset == 0 && frames[0].TotalLength == 67 && frames[0].BodyLength == 0x0039,
                "first raw frame boundary must be 67 bytes");
            Assert(frames[1].Offset == 67 && frames[1].TotalLength == 88 && frames[1].BodyLength == 0x004E,
                "second raw frame boundary must be 88 bytes");
            Assert(frames[0].RawProtocolCode == 0xFF2F && frames[1].RawProtocolCode == 0xE898,
                "raw protocol codes must remain uninterpreted metadata");

            byte[] large = BuildRawFrame(0x0402, 0xF7BD, 0x33);
            Assert(EquipmentRefineFrameFramer.TryParseConcatenated(large, out frames, out error),
                "confirmed 1036-byte framing sample should parse");
            Assert(frames.Count == 1 && frames[0].TotalLength == 1036 && frames[0].BodyLength == 0x0402,
                "1036-byte sample must remain one raw frame");
            Assert(!EquipmentRefineFrameFramer.TryParseConcatenated(large.Take(1035).ToArray(), out frames, out error),
                "truncated raw frame must fail closed");
        }

        private static byte[] BuildRawFrame(int bodyLength, ushort rawProtocolCode, byte fill)
        {
            byte[] bytes = Enumerable.Repeat(fill, 10 + bodyLength).ToArray();
            bytes[0] = 0x4D;
            bytes[1] = 0x5A;
            bytes[8] = (byte)(bodyLength >> 8);
            bytes[9] = (byte)bodyLength;
            bytes[10] = (byte)(rawProtocolCode >> 8);
            bytes[11] = (byte)rawProtocolCode;
            return bytes;
        }

        private static void TestPresetRequiresWornSlotSelection()
        {
            EquipmentRefinePreset preset = EquipmentRefinePreset.CreateDefault();
            preset.Target = new EquipmentRefineDetector.EquipmentTargetSelector
            {
                MemberIdentity = "item-1"
            };
            string error;
            Assert(!preset.IsValid(out error) && error.Contains("穿戴部位"),
                "preset without a worn slot must fail closed");
            preset.Target.Slot = "slot_1";
            Assert(preset.IsValid(out error),
                "a worn slot-only preset must be valid because identity is bound at runtime: " + error);
        }

        private sealed class ScreenshotAttributeLabel
        {
            public string Name { get; private set; }
            public string ValueText { get; private set; }
            public ScreenshotAttributeLabel(string name, string valueText)
            {
                Name = name;
                ValueText = valueText;
            }
        }

        private sealed class ScreenshotCardLabel
        {
            public int Index { get; private set; }
            public int Score { get; private set; }
            public List<ScreenshotAttributeLabel> Attributes { get; private set; }
            public ScreenshotCardLabel(int index, int score, params ScreenshotAttributeLabel[] attributes)
            {
                Index = index;
                Score = score;
                Attributes = new List<ScreenshotAttributeLabel>(attributes);
            }
        }

        private static void TestScreenshotTwentyCardLabels()
        {
            List<ScreenshotCardLabel> labels = new List<ScreenshotCardLabel>
            {
                new ScreenshotCardLabel(1, 4571, A("忽视抗鬼火", "+1.7%"), A("加强拈山", "+2.6%"), A("根骨", "+5"), A("强力克金", "+4.1%"), A("狂暴率", "+1.7%")),
                new ScreenshotCardLabel(2, 4771, A("忽视抗风", "+2.6%"), A("加强加速", "+1.3%"), A("命中率", "+2.8%"), A("强力克土", "+1.8%"), A("力量", "+14")),
                new ScreenshotCardLabel(3, 4583, A("忽视抗睡眠", "+2.0%"), A("狂暴率", "+2.0%"), A("强力克火", "+1.7%"), A("连击率", "+1.8%"), A("强力克金", "+1.6%")),
                new ScreenshotCardLabel(4, 4545, A("忽视抗遗忘", "+0.8%"), A("火系狂暴率", "+4.1%"), A("强力克金", "+1.9%"), A("连击率", "+1.6%"), A("根骨", "+3")),
                new ScreenshotCardLabel(5, 4834, A("忽视抗混", "+1.2%"), A("忽视抗遗忘", "+3.4%"), A("强力克火", "+1.7%"), A("强力克金", "+11.1%"), A("强力克木", "+1.8%")),
                new ScreenshotCardLabel(6, 4579, A("忽视抗混", "+0.9%"), A("狂暴率", "+1.8%"), A("根骨", "+4"), A("连击率", "+2.0%"), A("连击率", "+2.3%")),
                new ScreenshotCardLabel(7, 4487, A("命中率", "+2.4%"), A("加强神恩", "+1.9%"), A("强力克火", "+2.0%"), A("根骨", "+2"), A("灵性", "+3")),
                new ScreenshotCardLabel(8, 4926, A("忽视抗震慑", "+0.6%"), A("命中率", "+3.1%"), A("灵性", "+13"), A("敏捷", "+9"), A("强力克火", "+6.4%")),
                new ScreenshotCardLabel(9, 4786, A("忽视抗混", "+0.9%"), A("连击次数", "+1"), A("力量", "+2"), A("根骨", "+4"), A("强力克土", "+15.7%")),
                new ScreenshotCardLabel(10, 4976, A("忽视抗鬼火", "+4.0%"), A("忽视抗雷", "+4.9%"), A("命中率", "+1.6%"), A("强力克金", "+14.1%"), A("力量", "+5")),
                new ScreenshotCardLabel(11, 4608, A("雷系狂暴率", "+3.3%"), A("忽视抗水", "+1.6%"), A("强力克水", "+2.0%"), A("强力克水", "+1.6%"), A("灵性", "+8")),
                new ScreenshotCardLabel(12, 4506, A("加强拈山", "+0.5%"), A("加强横扫", "+1.2%"), A("根骨", "+4"), A("连击率", "+1.6%"), A("强力克火", "+1.7%")),
                new ScreenshotCardLabel(13, 4645, A("水系狂暴率", "+3.4%"), A("忽视抗睡", "+1.0%"), A("强力克土", "+7.0%"), A("连击率", "+1.9%"), A("强力克金", "+1.9%")),
                new ScreenshotCardLabel(14, 4935, A("加强啸月", "+0.5%"), A("加强震击", "+2.2%"), A("力量", "+11"), A("命中率", "+1.9%"), A("力量", "+20")),
                new ScreenshotCardLabel(15, 4529, A("加强啸月", "+0.5%"), A("忽视抗雷", "+3.7%"), A("强力克土", "+1.6%"), A("强力克木", "+1.7%"), A("强力克火", "+2.0%")),
                new ScreenshotCardLabel(16, 4691, A("雷系狂暴率", "+3.4%"), A("加强加防", "+3.3%"), A("狂暴率", "+1.6%"), A("狂暴率", "+1.9%"), A("狂暴率", "+1.7%")),
                new ScreenshotCardLabel(17, 4673, A("加强破甲", "+0.8%"), A("加强横扫", "+1.2%"), A("强力克水", "+1.9%"), A("命中率", "+1.7%"), A("敏捷", "+12")),
                new ScreenshotCardLabel(18, 5092, A("加强加攻", "+1.4%"), A("加强加速", "+0.9%"), A("灵性", "+19"), A("狂暴率", "+2.0%"), A("强力克木", "+16.0%")),
                new ScreenshotCardLabel(19, 4548, A("加强啸月", "+1.6%"), A("忽视抗火", "+1.7%"), A("根骨", "+5"), A("连击率", "+2.5%"), A("命中率", "+1.9%")),
                new ScreenshotCardLabel(20, 4835, A("忽视抗毒", "+3.7%"), A("忽视抗火", "+5.3%"), A("命中率", "+1.6%"), A("狂暴率", "+1.6%"), A("力量", "+11"))
            };
            Assert(labels.Count == 20 && labels.All(card => card.Attributes.Count == 5), "screenshot labels must contain 20 cards with five attributes each");
            Assert(labels[0].Score == 4571 && labels[0].Attributes[0].Name == "忽视抗鬼火" && labels[0].Attributes[0].ValueText == "+1.7%", "first screenshot label mismatch");
            Assert(labels[19].Score == 4835 && labels[19].Attributes[4].Name == "力量" && labels[19].Attributes[4].ValueText == "+11", "last screenshot label mismatch");
        }

        private static ScreenshotAttributeLabel A(string name, string value) { return new ScreenshotAttributeLabel(name, value); }

        private static void TestScreenshotLabelsAssociatedWith7XlsOperation()
        {
            string sourceFile = "7.xls";
            int socket = 12808;
            int[] segmentLengths = { 12, 26, 44, 155, 1036 };
            int screenshotPageCount = 4;
            int candidateCardCount = 20;
            bool binaryOffsetMappingConfirmed = false;
            Assert(sourceFile == "7.xls" && socket == 12808 &&
                segmentLengths.SequenceEqual(new[] { 12, 26, 44, 155, 1036 }) &&
                screenshotPageCount == 4 && candidateCardCount == 20 &&
                !binaryOffsetMappingConfirmed,
                "user-confirmed screenshot association manifest is invalid");
        }

        private static void TestWornTargetBeatsBackpackCandidate()
        {
            EquipmentRefineDetector.EquipmentSlot worn = CreateSlot(5);
            EquipmentRefineDetector.EquipmentSlot backpack = CreateSlot(99);
            backpack.IsWorn = false;
            backpack.CandidateOnly = true;
            EquipmentRefineDetector.EquipmentInventory inventory = CreateInventory(worn, "snap-worn", 1, 5);
            inventory.Items.Add(backpack);
            EquipmentRefineDetector.EquipmentSlot selected = EquipmentRefineDetector.FindByTarget(
                inventory,
                new EquipmentRefineDetector.EquipmentTargetSelector
                {
                    Slot = worn.Slot,
                    MemberIdentity = worn.MemberIdentity,
                    EquipmentId = worn.EquipmentId
                });
            Assert(selected == worn && selected.IsWorn, "backpack candidate must not be selected as refine target");
        }

        private static void TestBagTargetSelectionAndIdentityBinding()
        {
            EquipmentRefineDetector.EquipmentSlot selected = CreateBagSlot("bag-item-1", "bag-type-1", "背包装备");
            EquipmentRefineDetector.EquipmentInventory inventory = CreateBagInventory(selected, "bag-snap-1", 1);
            EquipmentRefineDetector.BagTargetSelector selector = new EquipmentRefineDetector.BagTargetSelector
            {
                Slot = selected.Slot,
                MemberIdentity = selected.MemberIdentity,
                ItemId = selected.ItemId,
                ItemTypeId = selected.ItemTypeId,
                Name = selected.EquipmentName
            };

            EquipmentRefineDetector.EquipmentSlot found = EquipmentRefineDetector.FindBagTarget(inventory, selector);
            Assert(found == selected && !found.IsWorn && found.ItemId == "bag-item-1" &&
                found.ItemTypeId == "bag-type-1", "bag target selection did not preserve verified raw identity");

            EquipmentRefineDetector.EquipmentSlot sameMemberDifferentSlot = CreateBagSlot("bag-item-2", "bag-type-1", "同名候选");
            sameMemberDifferentSlot.MemberIdentity = selected.MemberIdentity;
            sameMemberDifferentSlot.Slot = "slot_2";
            inventory.Items.Add(sameMemberDifferentSlot);
            Assert(EquipmentRefineDetector.FindBagTarget(inventory, selector) == selected,
                "bag selection must not use same member identity from another slot");

            EquipmentRefineDetector.EquipmentSlot ambiguous = CreateBagSlot("bag-item-3", "bag-type-1", "歧义候选");
            ambiguous.MemberIdentity = selected.MemberIdentity;
            ambiguous.Slot = selected.Slot;
            inventory.Items.Add(ambiguous);
            EquipmentRefineDetector.BagTargetSelector slotOnly = new EquipmentRefineDetector.BagTargetSelector
            {
                Slot = selected.Slot,
                MemberIdentity = selected.MemberIdentity
            };
            Assert(EquipmentRefineDetector.FindBagTarget(inventory, slotOnly) == null,
                "bag selection must fail closed on same slot/member ambiguity");
        }

        private static void TestBagNameDecodeAndPresetBinding()
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(
                BuildBagResidentJsonFixture("bag-member-1", "slot_1", true, false, 20));
            root["updatedAtUnixMs"] = 1;
            root["binding"] = new Newtonsoft.Json.Linq.JObject
            {
                ["pid"] = 100,
                ["startTicks"] = 200,
                ["exe"] = "fixture-client"
            };
            root["itemCount"] = 1;
            Newtonsoft.Json.Linq.JObject item = (Newtonsoft.Json.Linq.JObject)root["items"][0];
            item["displayName"] = "玄铁剑";
            item["displayNameStatus"] = "decoded";
            item["displayNameSource"] = "raw-name-table";
            item["displayNamePath"] = "name[1]";
            item["xianqiTier"] = 1;
            item["xianqiTierLabel"] = "一阶仙器";
            item["xianqiTierStatus"] = "decoded";
            item["xianqiTierSource"] = "verified-xianqi-static-prefix";
            item["xianqiTierPath"] = "m_ItemTypeId[0:2]";

            EquipmentRefineDetector.EquipmentInventory inventory =
                EquipmentRefineDetector.ParseBagInventory(root.ToString());
            Assert(inventory != null && inventory.Items.Count == 1,
                "decoded-name bag inventory did not parse");
            EquipmentRefineDetector.EquipmentSlot selected = inventory.Items[0];
            Assert(selected.EquipmentName == "玄铁剑" &&
                selected.EquipmentNameStatus == "decoded" &&
                selected.EquipmentNameSource == "raw-name-table" &&
                selected.EquipmentNamePath == "name[1]" &&
                selected.XianqiTier == 1 && selected.XianqiTierLabel == "一阶仙器" &&
                selected.XianqiTierStatus == "decoded" &&
                selected.XianqiTierSource == "verified-xianqi-static-prefix",
                "reader decoded name or tier metadata was not preserved");

            EquipmentRefinePresetEditorModel model = new EquipmentRefinePresetEditorModel(
                EquipmentRefinePreset.CreateDefault());
            string error;
            Assert(model.SetBagTargetFromInventory(inventory, selected.Slot, selected.MemberIdentity, out error), error);
            EquipmentRefinePreset saved;
            Assert(model.TryValidateAndSave(out saved, out error), error);
            Assert(saved.BagTargetMode && saved.BagTarget.Name == "玄铁剑" &&
                saved.BagTarget.ItemTypeId == selected.ItemTypeId &&
                saved.BagTarget.XianqiTier == 1 && saved.BagTarget.XianqiTierLabel == "一阶仙器" &&
                saved.BagTarget.RawFieldsSummary.Contains("m_ItemTypeId="),
                "decoded name or tier was not copied into the bag preset");

            string json = EquipmentRefinePresetSerializer.Serialize(saved);
            EquipmentRefinePreset loaded;
            Assert(EquipmentRefinePresetSerializer.TryDeserialize(json, out loaded, out error), error);
            Assert(loaded.BagTarget.Name == "玄铁剑" &&
                loaded.BagTarget.MemberIdentity == selected.MemberIdentity &&
                loaded.BagTarget.XianqiTier == 1 && loaded.BagTarget.XianqiTierLabel == "一阶仙器",
                "decoded name or tier was not preserved by preset JSON round trip");
        }

        private static void TestBagPresetEditorAndPlanRoundTrip()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                BagTargetMode = true,
                BagTarget = new EquipmentRefineDetector.BagTargetSelector
                {
                    Slot = "slot_1",
                    MemberIdentity = "bag-member-1",
                    ItemId = "bag-item-1",
                    ItemTypeId = "bag-type-1",
                    XianqiTier = 1,
                    XianqiTierLabel = "一阶仙器",
                    Name = "原始名称",
                    RawFieldsSummary = "m_ItemId=bag-item-1;m_ItemTypeId=bag-type-1",
                    RequestSlotIndex = 12
                },
                MemoryResultPath = "C:\\state.json",
                PacketTemplatePath = "C:\\template.json",
                Rules = new List<RefineRule>
                {
                    new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true },
                    new RefineRule { Attribute = TargetAttribute.Speed, Operator = AttributeOperator.Equal, TargetValue = 7, Enabled = true }
                },
                RequiredMatches = 2,
                MaxAttempts = 4,
                IntervalMs = 0
            };
            EquipmentRefinePresetEditorModel model = new EquipmentRefinePresetEditorModel(preset);
            Assert(model.SetRequiredMatches(2), "bag editor model rejected valid K");
            EquipmentRefinePreset saved;
            string error;
            Assert(model.TryValidateAndSave(out saved, out error), error);
            Assert(saved.BagTargetMode && saved.BagTarget.Slot == "slot_1" &&
                saved.BagTarget.MemberIdentity == "bag-member-1" && saved.RequiredMatches == 2,
                "bag editor model did not preserve target or K");

            EquipmentRefinePreset cloned = saved.Clone();
            Assert(cloned.BagTargetMode && cloned.BagTarget.Slot == "slot_1" &&
                cloned.BagTarget.MemberIdentity == "bag-member-1" &&
                cloned.BagTarget.ItemId == "bag-item-1" &&
                cloned.BagTarget.ItemTypeId == "bag-type-1" &&
                cloned.BagTarget.XianqiTier == 1 && cloned.BagTarget.XianqiTierLabel == "一阶仙器" &&
                cloned.BagTarget.RawFieldsSummary == "m_ItemId=bag-item-1;m_ItemTypeId=bag-type-1" &&
                cloned.BagTarget.RequestSlotIndex == 12 &&
                cloned.MemoryResultPath == "C:\\state.json" &&
                cloned.PacketTemplatePath == "C:\\template.json",
                "preset clone lost selected bag target, runtime paths, or raw fields");

            string encoded = EquipmentRefinePresetPlan.EncodePresetData(saved);
            EquipmentRefinePreset decoded = EquipmentRefinePresetPlan.DecodePresetData(encoded);
            Assert(decoded != null && decoded.BagTargetMode && decoded.BagTarget.ItemId == "bag-item-1" &&
                decoded.BagTarget.ItemTypeId == "bag-type-1" && decoded.BagTarget.XianqiTier == 1 &&
                decoded.BagTarget.XianqiTierLabel == "一阶仙器" && decoded.RequiredMatches == 2 &&
                decoded.BagTarget.RequestSlotIndex == 12 &&
                decoded.MemoryResultPath == "C:\\state.json" &&
                decoded.PacketTemplatePath == "C:\\template.json",
                "bag preset plan round trip lost selected item fields, tier, or runtime paths");
        }

        private static void TestBagResidentCandidateAssociation()
        {
            EquipmentRefineDetector.EquipmentSlot selected = CreateBagSlot("bag-item-1", "bag-type-1", "背包装备");
            RefineMemoryBaseline baseline = new RefineMemoryBaseline
            {
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ContainerIdentity = "container-1",
                StreamSessionId = "session-1",
                SnapshotId = "bag-snap-1",
                Sequence = 1,
                EquipmentIdentity = selected.MemberIdentity,
                Slot = selected.Slot,
                TargetMode = EquipmentTargetMode.Bag,
                ItemId = selected.ItemId,
                ItemTypeId = selected.ItemTypeId
            };
            string validDocument = BuildBagResidentJsonFixture(selected.MemberIdentity, selected.Slot, true, false, 20);
            ResidentJsonlRefineMemoryResultSource source = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(validDocument),
                new Dictionary<string, TargetAttribute>
                {
                    { "refine_attack", TargetAttribute.AttackPower },
                    { "refine_speed", TargetAttribute.Speed }
                });
            RefineMemoryResultSnapshot valid = source.ReceiveAfterSendAsync(baseline, 250, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(valid.ErrorCode == string.Empty && valid.TargetMode == EquipmentTargetMode.Bag &&
                valid.ItemId == "bag-item-1" && valid.Cards.Count == 20,
                "bag resident source did not decode the uniquely associated 20 cards");

            Newtonsoft.Json.Linq.JObject invalidModeRoot = Newtonsoft.Json.Linq.JObject.Parse(validDocument);
            invalidModeRoot["inventoryMode"] = "full";
            ResidentJsonlRefineMemoryResultSource invalidModeSource = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(invalidModeRoot.ToString()));
            RefineMemoryResultSnapshot invalidMode = invalidModeSource.ReceiveAfterSendAsync(baseline, 250, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(invalidMode.ErrorCode == "resident_bag_inventory_mode_invalid",
                "bag resident source must reject a declared non-bag inventory mode");

            string ambiguousDocument = BuildBagResidentJsonFixture(selected.MemberIdentity, selected.Slot, false, true, 20);
            ResidentJsonlRefineMemoryResultSource ambiguousSource = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(ambiguousDocument));
            RefineMemoryResultSnapshot ambiguous = ambiguousSource.ReceiveAfterSendAsync(baseline, 250, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(ambiguous.ErrorCode == "resident_bag_candidate_association_ambiguous",
                "bag resident source must reject multiple equipment paths");

            string missingDocument = BuildBagResidentJsonFixture(selected.MemberIdentity, selected.Slot, false, false, 19);
            ResidentJsonlRefineMemoryResultSource missingSource = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(missingDocument));
            RefineMemoryResultSnapshot missing = missingSource.ReceiveAfterSendAsync(baseline, 250, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(missing.ErrorCode == "resident_refine_cards_incomplete",
                "bag resident source must reject incomplete 20-card response");
        }

        private static void TestResidentUnknownPropertyKeyPreservesRawFields()
        {
            EquipmentRefineDetector.EquipmentSlot selected = CreateBagSlot("bag-item-1", "bag-type-1", "背包装备");
            RefineMemoryBaseline baseline = new RefineMemoryBaseline
            {
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ContainerIdentity = "container-1",
                StreamSessionId = "session-1",
                SnapshotId = "bag-snap-1",
                Sequence = 1,
                EquipmentIdentity = selected.MemberIdentity,
                Slot = selected.Slot,
                TargetMode = EquipmentTargetMode.Bag,
                ItemId = selected.ItemId,
                ItemTypeId = selected.ItemTypeId
            };
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(
                BuildBagResidentJsonFixture(selected.MemberIdentity, selected.Slot, false, false, 20));
            Newtonsoft.Json.Linq.JObject firstCard = (Newtonsoft.Json.Linq.JObject)
                ((Newtonsoft.Json.Linq.JArray)root["refineCandidates"])[0]["refineCards"][0];
            ((Newtonsoft.Json.Linq.JObject)
                ((Newtonsoft.Json.Linq.JArray)firstCard["propertyEntries"])[0]).Remove("propertyKey");

            ResidentJsonlRefineMemoryResultSource source = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(root.ToString(Newtonsoft.Json.Formatting.None)));
            RefineMemoryResultSnapshot result = source.ReceiveAfterSendAsync(
                baseline,
                250,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert(result.ErrorCode == string.Empty && result.Cards.Count == 20,
                "resident source rejected a complete raw property without optional propertyKey");
            RefineRawProperty raw = result.Cards[0].RawProperties[0];
            Assert(raw.RawId == "9020" && raw.RawValue == "1" &&
                raw.RawOrder == "1" && raw.PropertyKey == string.Empty,
                "resident source did not preserve unmapped raw property fields");
        }

        private static void TestBagRawTargetMetadataFailClosed()
        {
            EquipmentRefineDetector.EquipmentSlot selected = CreateBagSlot("bag-item-1", "bag-type-1", "背包装备");
            RefineMemoryBaseline baseline = new RefineMemoryBaseline
            {
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ContainerIdentity = "container-1",
                StreamSessionId = "session-1",
                SnapshotId = "bag-snap-1",
                Sequence = 1,
                EquipmentIdentity = selected.MemberIdentity,
                Slot = selected.Slot,
                TargetMode = EquipmentTargetMode.Bag,
                ItemId = selected.ItemId,
                ItemTypeId = selected.ItemTypeId
            };
            string document = BuildBagResidentJsonFixture(selected.MemberIdentity, selected.Slot, true, false, 20);

            Newtonsoft.Json.Linq.JObject wrongRoot = Newtonsoft.Json.Linq.JObject.Parse(document);
            wrongRoot["bagTargetMemberIdentity"] = "other-member";
            RefineMemoryResultSnapshot wrongRootResult = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(wrongRoot.ToString()))
                .ReceiveAfterSendAsync(baseline, 250, CancellationToken.None).GetAwaiter().GetResult();
            Assert(wrongRootResult.ErrorCode == "resident_bag_target_identity_mismatch",
                "bag root target identity mismatch must fail closed");

            Newtonsoft.Json.Linq.JObject wrongCandidate = Newtonsoft.Json.Linq.JObject.Parse(document);
            ((Newtonsoft.Json.Linq.JArray)wrongCandidate["refineCandidates"])[0]["targetSlot"] = "other-slot";
            RefineMemoryResultSnapshot wrongCandidateResult = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(wrongCandidate.ToString()))
                .ReceiveAfterSendAsync(baseline, 250, CancellationToken.None).GetAwaiter().GetResult();
            Assert(wrongCandidateResult.ErrorCode == "resident_bag_candidate_target_mismatch",
                "bag candidate target mismatch must fail closed");

            Newtonsoft.Json.Linq.JObject invalidAssociation = Newtonsoft.Json.Linq.JObject.Parse(document);
            ((Newtonsoft.Json.Linq.JArray)invalidAssociation["refineCandidates"])[0]["associationState"] = "invalid";
            RefineMemoryResultSnapshot invalidAssociationResult = new ResidentJsonlRefineMemoryResultSource(
                token => Task.FromResult(invalidAssociation.ToString()))
                .ReceiveAfterSendAsync(baseline, 250, CancellationToken.None).GetAwaiter().GetResult();
            Assert(invalidAssociationResult.ErrorCode == "resident_bag_candidate_association_invalid",
                "bag candidate association state must fail closed");
        }

        private static void TestBagHostMemoryHitStopsWithoutFollowup()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateBagSlot("bag-item-1", "bag-type-1", "背包装备");
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                BagTargetMode = true,
                BagTarget = new EquipmentRefineDetector.BagTargetSelector
                {
                    Slot = slot.Slot,
                    MemberIdentity = slot.MemberIdentity,
                    ItemId = slot.ItemId,
                    ItemTypeId = slot.ItemTypeId,
                    Name = slot.EquipmentName
                },
                MemoryResultMode = true,
                Rules = new List<RefineRule>
                {
                    new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true },
                    new RefineRule { Attribute = TargetAttribute.Speed, Operator = AttributeOperator.Equal, TargetValue = 7, Enabled = true }
                },
                RequiredMatches = 2,
                MaxAttempts = 3,
                IntervalMs = 0,
                ResultConfirmTimeoutMs = 250
            };
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            RefineMemoryResultSnapshot first = CreateBagMemoryResult(2, "bag-snap-2", slot, false);
            RefineMemoryResultSnapshot second = CreateBagMemoryResult(3, "bag-snap-3", slot, true);
            QueuedMemoryResultSource source = new QueuedMemoryResultSource(first, second);
            StringEquipmentRefineResultPresenter presenter = new StringEquipmentRefineResultPresenter();
            EquipmentRefineHost host = new EquipmentRefineHost(
                preset,
                new EquipmentRefineExecutor(CreateBagTemplate(), sender),
                token => Task.FromResult(CreateBagInventory(slot, "bag-snap-1", 1)),
                null,
                null,
                presenter,
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                source);
            EquipmentRefineStateMachine.ExecutionResult result = host.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.Success && !result.Replaced && sender.SentPackets.Count == 2 && source.ReceiveCount == 2,
                "bag host memory mode did not stop after second-round hit");
            Assert(result.MatchedResponseCard != null && result.MatchedResponseCard.CardIndex == 7 &&
                result.MatchedResponseCard.Attributes.Count == 2 && presenter.LastMessage.Contains("卡片7"),
                "bag hit result did not retain card details");
        }

        private static void TestBagMemoryResultSessionMismatchStops()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateBagSlot("bag-item-1", "bag-type-1", "背包装备");
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                BagTargetMode = true,
                BagTarget = new EquipmentRefineDetector.BagTargetSelector
                {
                    Slot = slot.Slot,
                    MemberIdentity = slot.MemberIdentity,
                    ItemId = slot.ItemId,
                    ItemTypeId = slot.ItemTypeId
                },
                MemoryResultMode = true,
                Rules = new List<RefineRule>
                {
                    new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true }
                },
                RequiredMatches = 1,
                MaxAttempts = 1,
                IntervalMs = 0,
                ResultConfirmTimeoutMs = 250
            };
            RefineMemoryResultSnapshot wrongSession = CreateBagMemoryResult(2, "bag-snap-2", slot, true);
            wrongSession.StreamSessionId = "other-session";
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            QueuedMemoryResultSource source = new QueuedMemoryResultSource(wrongSession);
            EquipmentRefineHost host = new EquipmentRefineHost(
                preset,
                new EquipmentRefineExecutor(CreateBagTemplate(), sender),
                token => Task.FromResult(CreateBagInventory(slot, "bag-snap-1", 1)),
                null,
                null,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()),
                source);
            EquipmentRefineStateMachine.ExecutionResult result = host.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.StopReason == EquipmentRefineStateMachine.StopReason.ProtocolUnverified &&
                sender.SentPackets.Count == 1 && source.ReceiveCount == 1,
                "bag memory result with changed session must stop after one send");
        }

        private static void TestBagResponseCardPathPreservesIdentity()
        {
            EquipmentRefineDetector.EquipmentSlot slot = CreateBagSlot("bag-item-1", "bag-type-1", "背包装备");
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                BagTargetMode = true,
                BagTarget = new EquipmentRefineDetector.BagTargetSelector
                {
                    Slot = slot.Slot,
                    MemberIdentity = slot.MemberIdentity,
                    ItemId = slot.ItemId,
                    ItemTypeId = slot.ItemTypeId
                },
                Rules = new List<RefineRule>
                {
                    new RefineRule { Attribute = TargetAttribute.AttackPower, Operator = AttributeOperator.GreaterThanOrEqual, TargetValue = 10, Enabled = true }
                },
                RequiredMatches = 1,
                MaxAttempts = 1,
                IntervalMs = 0,
                ResultConfirmTimeoutMs = 250,
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute>
                {
                    { "refine_attack", TargetAttribute.AttackPower }
                }
            };
            EquipmentRefineExecutor.RecordingPacketSender sender = new EquipmentRefineExecutor.RecordingPacketSender(true);
            QueuedResponseSource source = new QueuedResponseSource(new byte[] { 1 });
            EquipmentRefineHost host = new EquipmentRefineHost(
                preset,
                new EquipmentRefineExecutor(CreateBagTemplate(), sender),
                token => Task.FromResult(CreateBagInventory(slot, "bag-snap-1", 1)),
                new NonMatchingFixtureResponseAdapter(slot.MemberIdentity),
                source,
                new StringEquipmentRefineResultPresenter(),
                new ExplicitRefineStopReasonMapper(new Dictionary<string, string>()));
            EquipmentRefineStateMachine.ExecutionResult result = host.StartAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert(result.StopReason == EquipmentRefineStateMachine.StopReason.MaxAttemptsReached &&
                sender.SentPackets.Count == 1 && source.ReceiveCount == 1,
                "bag response-card path must preserve selected item identity through attribute read");
        }

        private sealed class ResidentRawPropertyFixture
        {
            public int EntryIndex;
            public string RawId;
            public string RawValue;
            public string RawOrder;
            public string PropertyKey;
            public string OriginalName;
        }

        private static string BuildResidentJsonFixture(
            string identity,
            string slot,
            string equipmentId,
            long sequence,
            string snapshotId,
            bool includeHit)
        {
            Newtonsoft.Json.Linq.JObject root = new Newtonsoft.Json.Linq.JObject
            {
                ["event"] = "equipment_inventory_resident_state",
                ["schemaVersion"] = ResidentJsonlRefineMemoryResultSource.SupportedSchemaVersion,
                ["available"] = true,
                ["readOnly"] = true,
                ["actionAuthorized"] = false,
                ["processIdentity"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["pid"] = 100,
                    ["startTicks"] = 200,
                    ["exe"] = "fixture-client"
                },
                ["containerIdentity"] = "container-1",
                ["streamSessionId"] = "session-1",
                ["snapshotId"] = snapshotId,
                ["sequence"] = sequence,
                ["refineProbeState"] = "ready",
                ["refineProbeReason"] = string.Empty,
                ["diagnosticCode"] = string.Empty,
                ["items"] = new Newtonsoft.Json.Linq.JArray
                {
                    new Newtonsoft.Json.Linq.JObject
                    {
                        ["candidateOnly"] = true,
                        ["isWorn"] = true,
                        ["memberIdentity"] = identity,
                        ["slot"] = slot,
                        ["equipmentId"] = equipmentId,
                        ["rawFields"] = new Newtonsoft.Json.Linq.JArray
                        {
                            new Newtonsoft.Json.Linq.JObject
                            {
                                ["key"] = "equipmentId",
                                ["kind"] = "string",
                                ["value"] = equipmentId
                            }
                        }
                    }
                }
            };

            Newtonsoft.Json.Linq.JArray cards = new Newtonsoft.Json.Linq.JArray();
            for (int cardIndex = 20; cardIndex >= 1; cardIndex--)
            {
                List<ResidentRawPropertyFixture> properties = new List<ResidentRawPropertyFixture>();
                if (includeHit && cardIndex == 7)
                {
                    properties.Add(new ResidentRawPropertyFixture
                    {
                        EntryIndex = 1,
                        RawId = "1002",
                        RawValue = "7",
                        RawOrder = "2",
                        PropertyKey = "refine_speed",
                        OriginalName = "速度"
                    });
                    properties.Add(new ResidentRawPropertyFixture
                    {
                        EntryIndex = 2,
                        RawId = "1001",
                        RawValue = "12",
                        RawOrder = "1",
                        PropertyKey = "refine_attack",
                        OriginalName = "攻击力"
                    });
                }
                else
                {
                    properties.Add(new ResidentRawPropertyFixture
                    {
                        EntryIndex = 1,
                        RawId = (9000 + cardIndex).ToString(),
                        RawValue = "1",
                        RawOrder = "1",
                        PropertyKey = "unknown_resident_property",
                        OriginalName = "未映射属性"
                    });
                }

                Newtonsoft.Json.Linq.JArray entries = new Newtonsoft.Json.Linq.JArray();
                Newtonsoft.Json.Linq.JArray propertyEntries = new Newtonsoft.Json.Linq.JArray();
                foreach (ResidentRawPropertyFixture property in properties)
                {
                    entries.Add(new Newtonsoft.Json.Linq.JObject
                    {
                        ["index"] = property.EntryIndex,
                        ["complete"] = true,
                        ["fields"] = new Newtonsoft.Json.Linq.JArray
                        {
                            new Newtonsoft.Json.Linq.JObject { ["component"] = 1, ["value"] = property.RawId },
                            new Newtonsoft.Json.Linq.JObject { ["component"] = 2, ["value"] = property.RawValue },
                            new Newtonsoft.Json.Linq.JObject { ["component"] = 3, ["value"] = property.RawOrder }
                        }
                    });
                    propertyEntries.Add(new Newtonsoft.Json.Linq.JObject
                    {
                        ["entryIndex"] = property.EntryIndex,
                        ["rawId"] = property.RawId,
                        ["rawValue"] = property.RawValue,
                        ["rawOrder"] = property.RawOrder,
                        ["propertyKey"] = property.PropertyKey,
                        ["value"] = property.RawValue,
                        ["originalName"] = property.OriginalName
                    });
                }

                cards.Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["cardIndex"] = cardIndex,
                    ["complete"] = true,
                    ["cardSource"] = "refine-candidate",
                    ["entryCount"] = properties.Count,
                    ["completeEntryCount"] = properties.Count,
                    ["entries"] = entries,
                    ["propertyEntries"] = propertyEntries
                });
            }

            root["refineCandidates"] = new Newtonsoft.Json.Linq.JArray
            {
                new Newtonsoft.Json.Linq.JObject
                {
                    ["candidateOnly"] = true,
                    ["source"] = "fixture-resident",
                    ["refineCards"] = cards
                }
            };
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildBagResidentJsonFixture(
            string identity,
            string slot,
            bool includeHit,
            bool addAmbiguousPath,
            int cardCount)
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(
                BuildResidentJsonFixture(identity, slot, string.Empty, 2, "bag-snap-2", includeHit));
            root["inventoryMode"] = "bag";
            root["bagTargetSlot"] = slot;
            root["bagTargetMemberIdentity"] = identity;
            root["bagTargetReason"] = "target_bound";
            root["wornOnly"] = false;
            Newtonsoft.Json.Linq.JObject item = (Newtonsoft.Json.Linq.JObject)root["items"][0];
            item["isWorn"] = false;
            item.Remove("equipmentId");
            Newtonsoft.Json.Linq.JArray rawFields = (Newtonsoft.Json.Linq.JArray)item["rawFields"];
            rawFields.Add(new Newtonsoft.Json.Linq.JObject
            {
                ["key"] = "m_ItemId",
                ["kind"] = "string",
                ["value"] = "bag-item-1"
            });
            rawFields.Add(new Newtonsoft.Json.Linq.JObject
            {
                ["key"] = "m_ItemTypeId",
                ["kind"] = "string",
                ["value"] = "bag-type-1"
            });

            Newtonsoft.Json.Linq.JArray cards = (Newtonsoft.Json.Linq.JArray)root["refineCandidates"][0]["refineCards"];
            foreach (Newtonsoft.Json.Linq.JObject card in cards.OfType<Newtonsoft.Json.Linq.JObject>())
            {
                card["equipmentPath"] = "bag." + slot;
            }
            Newtonsoft.Json.Linq.JObject candidate =
                (Newtonsoft.Json.Linq.JObject)((Newtonsoft.Json.Linq.JArray)root["refineCandidates"])[0];
            candidate["targetSlot"] = slot;
            candidate["targetMemberIdentity"] = identity;
            candidate["associationState"] = "unique";
            while (cards.Count > cardCount)
            {
                cards.RemoveAt(cards.Count - 1);
            }
            if (addAmbiguousPath)
            {
                Newtonsoft.Json.Linq.JObject secondCandidate = (Newtonsoft.Json.Linq.JObject)
                    ((Newtonsoft.Json.Linq.JArray)root["refineCandidates"])[0].DeepClone();
                foreach (Newtonsoft.Json.Linq.JObject card in ((Newtonsoft.Json.Linq.JArray)secondCandidate["refineCards"])
                    .OfType<Newtonsoft.Json.Linq.JObject>())
                {
                    card["equipmentPath"] = "other-container." + slot;
                }
                ((Newtonsoft.Json.Linq.JArray)root["refineCandidates"]).Add(secondCandidate);
            }
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildJsonFixture(long sequence, string identity, bool stable, string errorCode, int cardCount, bool includeHit)
        {
            List<object> cards = new List<object>();
            for (int index = cardCount; index >= 1; index--)
            {
                List<object> attributes = new List<object>();
                if (includeHit && index == 7)
                {
                    attributes.Add(new { type = (int)TargetAttribute.Speed, name = "速度", value = 7 });
                    attributes.Add(new { type = (int)TargetAttribute.AttackPower, name = "攻击力", value = 12 });
                }
                cards.Add(new { index = index, complete = true, attributes = attributes });
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                format = "equipment-refine-fixture-v1",
                sequence = sequence,
                identity = identity,
                stable = stable,
                errorCode = errorCode,
                cards = cards
            });
        }

        private static string BuildJsonFixtureWithExtraTopLevelField()
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(
                BuildJsonFixture(2, "equip-1", true, string.Empty, 20, false));
            root["unknown"] = 1;
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildJsonFixtureWithExtraCardField()
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(
                BuildJsonFixture(2, "equip-1", true, string.Empty, 20, false));
            ((Newtonsoft.Json.Linq.JObject)root["cards"][0])["unknown"] = 1;
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildJsonFixtureWithExtraAttributeField()
        {
            string value = BuildJsonFixture(2, "equip-1", true, string.Empty, 20, true);
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(value);
            Newtonsoft.Json.Linq.JToken attribute = root["cards"]
                .First(card => card["attributes"] != null && card["attributes"].HasValues)["attributes"][0];
            ((Newtonsoft.Json.Linq.JObject)attribute)["unknown"] = 1;
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildJsonFixtureWithUnknownAttributeType()
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(
                BuildJsonFixture(2, "equip-1", true, string.Empty, 20, true));
            Newtonsoft.Json.Linq.JToken attribute = root["cards"]
                .First(card => card["attributes"] != null && card["attributes"].HasValues)["attributes"][0];
            ((Newtonsoft.Json.Linq.JObject)attribute)["type"] = (int)TargetAttribute.Unknown;
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildJsonFixtureWithDuplicateIndex()
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(BuildJsonFixture(2, "equip-1", true, string.Empty, 20, false));
            root["cards"][0]["index"] = 1;
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildJsonFixtureWithOutOfRangeIndex()
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(BuildJsonFixture(2, "equip-1", true, string.Empty, 20, false));
            root["cards"][0]["index"] = 21;
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string BuildJsonFixtureWithMissingRequiredField()
        {
            Newtonsoft.Json.Linq.JObject root = Newtonsoft.Json.Linq.JObject.Parse(BuildJsonFixture(2, "equip-1", true, string.Empty, 20, false));
            ((Newtonsoft.Json.Linq.JObject)root["cards"][0]).Remove("attributes");
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static EquipmentRefinePreset CreateMemoryPreset(int requiredMatches)
        {
            return new EquipmentRefinePreset
            {
                MemoryResultMode = true,
                Target = new EquipmentRefineDetector.EquipmentTargetSelector
                {
                    Slot = "slot_1",
                    MemberIdentity = "item-1",
                    EquipmentId = "258"
                },
                Rules = new List<RefineRule>
                {
                    new RefineRule
                    {
                        Attribute = TargetAttribute.AttackPower,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 10,
                        Enabled = true
                    },
                    new RefineRule
                    {
                        Attribute = TargetAttribute.Speed,
                        Operator = AttributeOperator.Equal,
                        TargetValue = 7,
                        Enabled = true
                    }
                },
                RequiredMatches = requiredMatches,
                MaxAttempts = 3,
                IntervalMs = 0,
                ResultConfirmTimeoutMs = 250
            };
        }

        private static RefineMemoryResultSnapshot CreateMemoryResult(
            long sequence,
            string snapshotId,
            string identity,
            string slot,
            string equipmentId,
            bool hit)
        {
            RefineMemoryResultSnapshot result = new RefineMemoryResultSnapshot
            {
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ContainerIdentity = "container-1",
                StreamSessionId = "session-1",
                SnapshotId = snapshotId,
                Sequence = sequence,
                EquipmentIdentity = identity,
                Slot = slot,
                EquipmentId = equipmentId,
                IsStable = true
            };
            for (int index = 1; index <= 20; index++)
            {
                RefineResponseCard card = new RefineResponseCard
                {
                    CardIndex = index,
                    IsComplete = true
                };
                if (hit && index == 7)
                {
                    card.Attributes.Add(new AttributeValue
                    {
                        Type = TargetAttribute.Speed,
                        Name = "速度",
                        CurrentValue = 7,
                        RawValue = 7
                    });
                    card.Attributes.Add(new AttributeValue
                    {
                        Type = TargetAttribute.AttackPower,
                        Name = "攻击",
                        CurrentValue = 12,
                        RawValue = 12
                    });
                }
                result.Cards.Add(card);
            }
            return result;
        }

        private static RefineMemoryResultSnapshot CopyMemoryResult(
            RefineMemoryResultSnapshot source,
            int processPid = 100,
            string container = null,
            string session = null,
            string identity = null,
            bool? stable = null,
            int cardCount = -1,
            bool duplicateIndex = false,
            string errorCode = null)
        {
            RefineMemoryResultSnapshot copy = new RefineMemoryResultSnapshot
            {
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = processPid, startTicks = 200 },
                ContainerIdentity = container ?? source.ContainerIdentity,
                StreamSessionId = session ?? source.StreamSessionId,
                SnapshotId = source.SnapshotId,
                Sequence = source.Sequence,
                EquipmentIdentity = identity ?? source.EquipmentIdentity,
                Slot = source.Slot,
                EquipmentId = source.EquipmentId,
                IsStable = stable ?? source.IsStable,
                ErrorCode = errorCode ?? source.ErrorCode
            };
            foreach (RefineResponseCard sourceCard in source.Cards)
            {
                RefineResponseCard card = new RefineResponseCard
                {
                    CardIndex = sourceCard.CardIndex,
                    IsComplete = sourceCard.IsComplete
                };
                card.Attributes.AddRange(sourceCard.Attributes.Select(attribute => new AttributeValue
                {
                    Type = attribute.Type,
                    Name = attribute.Name,
                    CurrentValue = attribute.CurrentValue,
                    RawValue = attribute.RawValue,
                    Unit = attribute.Unit
                }));
                copy.Cards.Add(card);
            }
            if (cardCount >= 0 && copy.Cards.Count > cardCount)
            {
                copy.Cards.RemoveRange(cardCount, copy.Cards.Count - cardCount);
            }
            if (duplicateIndex && copy.Cards.Count >= 2)
            {
                copy.Cards[copy.Cards.Count - 1].CardIndex = copy.Cards[copy.Cards.Count - 2].CardIndex;
            }
            return copy;
        }

        private sealed class QueuedResponseSource : IRefineResponseSource
        {
            private readonly Queue<byte[]> _frames;
            public readonly List<long> Baselines = new List<long>();
            public int ReceiveCount { get; private set; }
            public QueuedResponseSource(params byte[][] frames) { _frames = new Queue<byte[]>(frames); }
            public Task<byte[]> ReceiveAfterSendAsync(long baselineSequence, int timeoutMs, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Baselines.Add(baselineSequence);
                ReceiveCount++;
                return Task.FromResult(_frames.Count == 0 ? null : _frames.Dequeue());
            }
        }

        private sealed class BlockingResponseSource : IRefineResponseSource
        {
            public Task<byte[]> ReceiveAfterSendAsync(
                long baselineSequence,
                int timeoutMs,
                CancellationToken cancellationToken)
            {
                return new TaskCompletionSource<byte[]>().Task;
            }
        }

        private sealed class CancelThenReturnResponseSource : IRefineResponseSource
        {
            private readonly Action _cancel;
            private readonly byte[] _frame;

            public CancelThenReturnResponseSource(Action cancel, byte[] frame)
            {
                this._cancel = cancel;
                this._frame = frame;
            }

            public Task<byte[]> ReceiveAfterSendAsync(
                long baselineSequence,
                int timeoutMs,
                CancellationToken cancellationToken)
            {
                this._cancel();
                return Task.FromResult((byte[])this._frame.Clone());
            }
        }

        private sealed class QueuedMemoryResultSource : IRefineMemoryResultSource
        {
            private readonly Queue<RefineMemoryResultSnapshot> _results;
            public readonly List<long> Baselines = new List<long>();
            public int ReceiveCount { get; private set; }

            public QueuedMemoryResultSource(params RefineMemoryResultSnapshot[] results)
            {
                this._results = new Queue<RefineMemoryResultSnapshot>(results ?? new RefineMemoryResultSnapshot[0]);
            }

            public Task<RefineMemoryResultSnapshot> ReceiveAfterSendAsync(
                RefineMemoryBaseline baseline,
                int timeoutMs,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.Baselines.Add(baseline == null ? 0 : baseline.Sequence);
                this.ReceiveCount++;
                return Task.FromResult(this._results.Count == 0 ? null : this._results.Dequeue());
            }
        }

        private sealed class ThrowingMemoryResultSource : IRefineMemoryResultSource
        {
            private readonly Exception _exception;
            public ThrowingMemoryResultSource(Exception exception) { this._exception = exception; }

            public Task<RefineMemoryResultSnapshot> ReceiveAfterSendAsync(
                RefineMemoryBaseline baseline,
                int timeoutMs,
                CancellationToken cancellationToken)
            {
                throw this._exception;
            }
        }

        private sealed class BlockingMemoryResultSource : IRefineMemoryResultSource
        {
            public async Task<RefineMemoryResultSnapshot> ReceiveAfterSendAsync(
                RefineMemoryBaseline baseline,
                int timeoutMs,
                CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            }
        }

        private sealed class TokenResponseAdapter : IRefineResponseAdapter
        {
            private readonly string _identity;
            public TokenResponseAdapter(string identity) { _identity = identity; }
            public bool TryDecode(byte[] frame, out RefineResponseEnvelope response, out string error)
            {
                bool hit = frame != null && frame.Length == 1 && frame[0] == 2;
                response = CompleteResponse(hit ? 3 : 2,
                    hit ? new AttributeValue { Type = TargetAttribute.Speed, CurrentValue = 7, RawValue = 7 } : null,
                    hit ? new AttributeValue { Type = TargetAttribute.AttackPower, CurrentValue = 12, RawValue = 12 } : null);
                response.EquipmentIdentity = _identity;
                if (hit) response.Cards[6].Attributes.Reverse();
                error = string.Empty;
                return frame != null && frame.Length == 1;
            }
        }

        private sealed class QueuedTokenResponseAdapter : IRefineResponseAdapter
        {
            private readonly string _identity;
            public QueuedTokenResponseAdapter(string identity) { _identity = identity; }
            public bool TryDecode(byte[] frame, out RefineResponseEnvelope response, out string error)
            {
                bool hit = frame != null && frame.Length == 1 && frame[0] == 2;
                response = CompleteResponse(hit ? 3 : 2);
                response.EquipmentIdentity = _identity;
                if (hit)
                {
                    response.Cards[6].Attributes.Add(new AttributeValue { Type = TargetAttribute.Speed, CurrentValue = 7, RawValue = 7 });
                    response.Cards[6].Attributes.Add(new AttributeValue { Type = TargetAttribute.AttackPower, CurrentValue = 12, RawValue = 12 });
                }
                List<RefineResponseCard> reordered = response.Cards.OrderByDescending(card => card.CardIndex).ToList();
                response.Cards.Clear();
                response.Cards.AddRange(reordered);
                error = string.Empty;
                return frame != null && frame.Length == 1;
            }
        }

        private static void awaitBrief()
        {
            System.Threading.Thread.Sleep(20);
        }

        private sealed class FixtureResponseAdapter : IRefineResponseAdapter
        {
            private readonly string _identity;

            public FixtureResponseAdapter(string identity)
            {
                this._identity = identity;
            }

            public bool TryDecode(byte[] frame, out RefineResponseEnvelope response, out string error)
            {
                response = CompleteResponse(2, new AttributeValue
                {
                    Type = TargetAttribute.AttackPower,
                    CurrentValue = 12,
                    RawValue = 12
                });
                response.EquipmentIdentity = this._identity;
                error = string.Empty;
                return frame != null && frame.Length == 2;
            }
        }

        private sealed class NonMatchingFixtureResponseAdapter : IRefineResponseAdapter
        {
            private readonly string _identity;
            public NonMatchingFixtureResponseAdapter(string identity) { this._identity = identity; }
            public bool TryDecode(byte[] frame, out RefineResponseEnvelope response, out string error)
            {
                response = CompleteResponse(2);
                response.EquipmentIdentity = this._identity;
                error = string.Empty;
                return frame != null;
            }
        }

        private sealed class FailingResponseAdapter : IRefineResponseAdapter
        {
            private readonly string _error;
            public FailingResponseAdapter(string error) { this._error = error; }
            public bool TryDecode(byte[] frame, out RefineResponseEnvelope response, out string error)
            {
                response = null;
                error = this._error;
                return false;
            }
        }

        private static RefineResponseEnvelope CompleteResponse(long sequence, params AttributeValue[] firstCardAttributes)
        {
            RefineResponseEnvelope response = new RefineResponseEnvelope
            {
                Sequence = sequence,
                EquipmentIdentity = "equip-1",
                IsStable = true
            };
            for (int index = 1; index <= 20; index++)
            {
                RefineResponseCard card = new RefineResponseCard
                {
                    CardIndex = index,
                    IsComplete = true
                };
                if (index == 1 && firstCardAttributes != null)
                {
                    card.Attributes.AddRange(firstCardAttributes);
                }
                response.Cards.Add(card);
            }
            return response;
        }

        private static void TestPresetPlanAndJsonRoundTrip()
        {
            EquipmentRefinePreset preset = new EquipmentRefinePreset
            {
                Name = "炼化|百分比",
                Target = new EquipmentRefineDetector.EquipmentTargetSelector
                {
                    Slot = "slot_1",
                    MemberIdentity = "item-1",
                    EquipmentId = "258"
                },
                Rules = new List<RefineRule>
                {
                    new RefineRule
                    {
                        Attribute = TargetAttribute.AttackPower,
                        Operator = AttributeOperator.GreaterThanOrEqual,
                        TargetValue = 10,
                        Enabled = true
                    },
                    new RefineRule
                    {
                        Attribute = TargetAttribute.CritRate,
                        Operator = AttributeOperator.Equal,
                        TargetValue = 2,
                        Enabled = true
                    }
                },
                VerifiedAttributeFields = new Dictionary<string, TargetAttribute>
                {
                    { "refine_attack", TargetAttribute.AttackPower }
                },
                TypeCode = 16,
                OperationCode = 1,
                RequiredMatches = 1,
                MemoryResultMode = true
            };

            string validationError;
            Assert(preset.IsValid(out validationError), validationError);
            string encoded = EquipmentRefinePresetPlan.EncodePresetData(preset);
            Assert(!string.IsNullOrWhiteSpace(encoded), "preset plan encoding failed");
            EquipmentRefinePreset decoded = EquipmentRefinePresetPlan.DecodePresetData(encoded);
            Assert(decoded != null && decoded.Rules.Count == 2, "preset plan round trip failed");
            Assert(decoded.Rules[0].TargetValue == 10, "preset rule target mismatch");
            Assert(decoded.Rules[1].TargetValue == 2, "percentage threshold protocol unit changed");
            string[] invalidPlanParts = encoded.Split('|');
            invalidPlanParts[5] = "0:0:10:1";
            Assert(EquipmentRefinePresetPlan.DecodePresetData(string.Join("|", invalidPlanParts)) == null,
                "preset plan decoder must reject unsupported operators");
            Assert(decoded.Name == "炼化|百分比", "preset plan name escaping failed");
            Assert(decoded.Target != null && decoded.Target.Slot == "slot_1", "preset plan target mismatch");
            Assert(decoded.TypeCode == 16 && decoded.OperationCode == 1,
                "preset plan protocol codes mismatch");
            Assert(decoded.RequiredMatches == 1, "preset plan K mismatch");
            Assert(decoded.MemoryResultMode, "preset plan memory result mode mismatch");

            EquipmentRefinePresetStep legacyStep;
            Assert(EquipmentRefinePresetPlan.TryDecodeStep(
                "EquipmentRefine|1|1|IDLE",
                out legacyStep) &&
                legacyStep != null &&
                legacyStep.Index == 1 &&
                legacyStep.State == "IDLE",
                "legacy v1 equipment refine step must remain compatible");

            string json = EquipmentRefinePresetSerializer.Serialize(preset);
            EquipmentRefinePreset loaded;
            string error;
            Assert(EquipmentRefinePresetSerializer.TryDeserialize(json, out loaded, out error), error);
            Assert(loaded.Target != null && loaded.Target.Slot == "slot_1", "preset json target mismatch");
            Assert(loaded.VerifiedAttributeFields.ContainsKey("refine_attack"), "preset json field map missing");
            Assert(loaded.TypeCode == 16 && loaded.OperationCode == 1,
                "preset json protocol codes mismatch");
            Assert(loaded.RequiredMatches == 1, "preset json K mismatch");
            Assert(loaded.MemoryResultMode, "preset json memory result mode mismatch");
            Assert(loaded.Rules[1].Attribute == TargetAttribute.CritRate && loaded.Rules[1].TargetValue == 2,
                "percentage threshold JSON unit changed");
        }

        private static void TestPresetRuleContractAndCloneCompleteness()
        {
            EquipmentRefinePreset preset = EquipmentRefinePreset.CreateDefault();
            preset.Target = new EquipmentRefineDetector.EquipmentTargetSelector
            {
                Slot = "slot_1",
                MemberIdentity = "member-1",
                EquipmentId = "equipment-1"
            };
            preset.EnableLooping = true;
            preset.VisionRegionOffsetX = 12;
            preset.VisionRegionOffsetY = 34;
            preset.Rules[0].TargetValue = 2;

            EquipmentRefinePreset clone = preset.Clone();
            Assert(clone.EnableLooping && clone.VisionRegionOffsetX == 12 && clone.VisionRegionOffsetY == 34,
                "preset clone lost looping or legacy compatibility fields");
            Assert(clone.Rules.Count == 5 && clone.Rules[0].TargetValue == 2,
                "preset clone changed integer threshold units");
            clone.Rules[0].TargetValue = 9;
            Assert(preset.Rules[0].TargetValue == 2, "preset clone is not deep for rules");

            RefineRule unsupported = new RefineRule
            {
                Attribute = TargetAttribute.AttackPower,
                Operator = AttributeOperator.GreaterThan,
                TargetValue = 1,
                Enabled = true
            };
            Assert(!unsupported.Evaluate(2), "unsupported operator must evaluate fail-closed");
            string error;
            preset.Rules[0].Operator = AttributeOperator.LessThan;
            Assert(!preset.IsValid(out error) && error.Contains("只支持"),
                "preset must reject unsupported operators");
            string json = EquipmentRefinePresetSerializer.Serialize(preset);
            EquipmentRefinePreset loaded;
            Assert(!EquipmentRefinePresetSerializer.TryDeserialize(json, out loaded, out error),
                "JSON loader must reject unsupported operators");
        }

        private static void TestRefineAttributeCatalog()
        {
            string[] expectedNames =
            {
                "根骨", "灵性", "力量", "敏捷", "命中率", "闪躲率", "连击次数", "连击率", "狂暴率",
                "抗混乱", "抗毒", "抗昏睡", "抗封印", "抗风", "抗雷", "抗水", "抗火", "抗震慑",
                "抗三尸", "抗鬼火", "抗遗忘", "物理吸收", "忽视抗混乱", "忽视抗毒", "忽视抗昏睡",
                "忽视抗封印", "忽视抗风", "忽视抗雷", "忽视抗水", "忽视抗火", "忽视抗震慑", "忽视抗三尸",
                "忽视抗鬼火", "忽视抗遗忘", "风系狂暴率", "雷系狂暴率", "水系狂暴率", "火系狂暴率",
                "三尸狂暴率", "鬼火狂暴率", "加强加速", "加强加防", "加强加攻", "加强破甲", "加强震击",
                "加强治愈", "加强横扫", "加强魅惑", "强力克金", "强力克木", "强力克水", "强力克火",
                "强力克土", "抗感山", "抗啸月"
            };
            IList<TargetAttributeDefinition> definitions = EquipmentRefineAttributeCatalog.GetReaderDefinitions();
            Assert(definitions.Count == expectedNames.Length, "reader attribute catalog count mismatch");
            Assert(definitions.Select(definition => definition.DisplayName).SequenceEqual(expectedNames),
                "reader attribute catalog order/name mismatch");
            Assert(definitions.All(definition => !definition.DisplayName.Any(character =>
                    (character >= 'A' && character <= 'Z') ||
                    (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') ||
                    character == '_')),
                "preset attribute display names must not expose ASCII identifiers");

            IDictionary<string, TargetAttribute> map = EquipmentRefineAttributeCatalog.CreateReaderFieldMap();
            Assert(map.Count == expectedNames.Length, "reader attribute mapping count mismatch");
            Assert(map["lh_gg"] == TargetAttribute.RootBone, "root bone field mapping mismatch");
            Assert(map["80613"] == TargetAttribute.ResistGanShan, "gan shan raw ID mapping mismatch");
            Assert(EquipmentRefineAttributeCatalog.GetDisplayName(TargetAttribute.ResistXiaoYue) == "抗啸月",
                "xiao yue display name mismatch");
            Assert(EquipmentRefinePreset.CreateDefault().Rules[0].Attribute == TargetAttribute.RootBone,
                "default refine rule must use reader catalog");
        }

        private static EquipmentRefineDetector.EquipmentSlot CreateSlot(int attack)
        {
            EquipmentRefineDetector.EquipmentSlot slot = new EquipmentRefineDetector.EquipmentSlot
            {
                MemberIdentity = "item-1",
                Slot = "slot_1",
                EquipmentId = "258",
                CandidateOnly = true,
                IsWorn = true,
                RawFields = new List<EquipmentRefineDetector.RawField>
                {
                    new EquipmentRefineDetector.RawField
                    {
                        Key = "refine_attack",
                        Kind = "int",
                        Value = attack.ToString()
                    }
                }
            };
            slot.BaseAttributeHash = EquipmentRefineDetector.CalculateIdentityHash(
                slot.MemberIdentity,
                slot.Slot,
                slot.EquipmentId);
            return slot;
        }

        private static EquipmentRefineDetector.EquipmentSlot CreateBagSlot(
            string itemId,
            string itemTypeId,
            string name)
        {
            EquipmentRefineDetector.EquipmentSlot slot = new EquipmentRefineDetector.EquipmentSlot
            {
                MemberIdentity = "bag-member-1",
                Slot = "slot_1",
                EquipmentName = name,
                ItemId = itemId,
                ItemTypeId = itemTypeId,
                CandidateOnly = true,
                IsWorn = false,
                RawFields = new List<EquipmentRefineDetector.RawField>
                {
                    new EquipmentRefineDetector.RawField { Key = "m_ItemId", Kind = "string", Value = itemId },
                    new EquipmentRefineDetector.RawField { Key = "m_ItemTypeId", Kind = "string", Value = itemTypeId },
                    new EquipmentRefineDetector.RawField { Key = "name", Kind = "string", Value = name },
                    new EquipmentRefineDetector.RawField { Key = "refine_attack", Kind = "int", Value = "5" }
                }
            };
            slot.BaseAttributeHash = EquipmentRefineDetector.CalculateIdentityHash(
                slot.MemberIdentity, slot.Slot, string.Empty);
            return slot;
        }

        private static EquipmentRefineDetector.EquipmentInventory CreateBagInventory(
            EquipmentRefineDetector.EquipmentSlot slot,
            string snapshotId,
            long sequence)
        {
            return new EquipmentRefineDetector.EquipmentInventory
            {
                SchemaVersion = 3,
                SnapshotId = snapshotId,
                StreamSessionId = "session-1",
                Sequence = sequence,
                Available = true,
                ReadOnly = true,
                ActionAuthorized = false,
                ItemCount = 1,
                UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Binding = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ContainerIdentity = "container-1",
                WornOnly = false,
                BagMode = true,
                Items = new List<EquipmentRefineDetector.EquipmentSlot> { slot }
            };
        }

        private static RefineMemoryResultSnapshot CreateBagMemoryResult(
            long sequence,
            string snapshotId,
            EquipmentRefineDetector.EquipmentSlot slot,
            bool hit)
        {
            RefineMemoryResultSnapshot result = CreateMemoryResult(
                sequence,
                snapshotId,
                slot.MemberIdentity,
                slot.Slot,
                string.Empty,
                hit);
            result.TargetMode = EquipmentTargetMode.Bag;
            result.ItemId = slot.ItemId;
            result.ItemTypeId = slot.ItemTypeId;
            return result;
        }

        private static EquipmentRefineExecutor.RefinePacketTemplate CreateBagTemplate()
        {
            return new EquipmentRefineExecutor.RefinePacketTemplate
            {
                ProtocolId = 0x8008,
                FixtureBytes = new byte[] { 0x00, 0xAA },
                ProtocolVerified = true,
                EvidenceId = "offline-bag-fixture",
                Fields = new List<EquipmentRefineExecutor.RefinePacketField>
                {
                    new EquipmentRefineExecutor.RefinePacketField
                    {
                        Name = "slot",
                        Offset = 0,
                        Length = 1,
                        Source = EquipmentRefineExecutor.PacketFieldSource.SlotIndex,
                        Verified = true
                    },
                    new EquipmentRefineExecutor.RefinePacketField
                    {
                        Name = "type",
                        Offset = 1,
                        Length = 1,
                        Source = EquipmentRefineExecutor.PacketFieldSource.RefineType,
                        Verified = true
                    }
                }
            };
        }

        private static EquipmentRefineDetector.EquipmentInventory CreateInventory(
            EquipmentRefineDetector.EquipmentSlot slot,
            string snapshotId,
            long sequence,
            int attack)
        {
            return new EquipmentRefineDetector.EquipmentInventory
            {
                SchemaVersion = 3,
                SnapshotId = snapshotId,
                StreamSessionId = "session-1",
                Sequence = sequence,
                Available = true,
                ReadOnly = true,
                ActionAuthorized = false,
                ItemCount = 1,
                UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Binding = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ProcessIdentity = new EquipmentRefineDetector.ProcessIdentity { pid = 100, startTicks = 200 },
                ContainerIdentity = "container-1",
                WornOnly = true,
                Items = new List<EquipmentRefineDetector.EquipmentSlot> { slot }
            };
        }

        private static EquipmentRefineExecutor.RefinePacketTemplate CreateTemplate()
        {
            return new EquipmentRefineExecutor.RefinePacketTemplate
            {
                ProtocolId = 0x8008,
                FixtureBytes = new byte[] { 0x00, 0x00, 0x00, 0xAA },
                ProtocolVerified = true,
                EvidenceId = "offline-fixture",
                Fields = new List<EquipmentRefineExecutor.RefinePacketField>
                {
                    new EquipmentRefineExecutor.RefinePacketField
                    {
                        Name = "slot",
                        Offset = 0,
                        Length = 1,
                        Source = EquipmentRefineExecutor.PacketFieldSource.SlotIndex,
                        Verified = true
                    },
                    new EquipmentRefineExecutor.RefinePacketField
                    {
                        Name = "equipmentId",
                        Offset = 1,
                        Length = 2,
                        Source = EquipmentRefineExecutor.PacketFieldSource.EquipmentId,
                        Verified = true
                    },
                    new EquipmentRefineExecutor.RefinePacketField
                    {
                        Name = "type",
                        Offset = 2,
                        Length = 1,
                        Source = EquipmentRefineExecutor.PacketFieldSource.RefineType,
                        Verified = true
                    }
                }
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static int ReadUInt16BigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 8) | buffer[offset + 1];
        }

        private static int ReadInt32BigEndian(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24) |
                (buffer[offset + 1] << 16) |
                (buffer[offset + 2] << 8) |
                buffer[offset + 3];
        }
    }
}


