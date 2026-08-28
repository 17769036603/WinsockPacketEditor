using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using WPELibrary.Lib;
using WPELibrary.Lib.Vision;

namespace TreasureC6StreamRegression
{
    internal static class Program
    {
        private static readonly string Session = "c6-fixture-session";

        private static int Main()
        {
            try
            {
                TestProtocol();
                TestTcpClientHandshake();
                TestBufferedTcpClient();
                TestDirectTargetPreparation();
                TestCurrentSessionUseTemplateBinding();
                TestRobotInstructionContract();
                TestInstructionCodec();
                TestEvidenceDecoder();
                TestPersistentRunLogStore();
                TestRunnerUsesAfterJumpWithoutArrivalEvidence();
                TestRunnerEnforcedArrivalEvidenceFailsClosed();
                TestRunnerStopsContinuousAfterSocketSendFailure();
                TestRunnerDoesNotJumpWithoutCompleteActionTemplate();
                TestRunnerKeepsJumpWhenUseTemplateIsMissing();
                TestRunnerUsesGuardedJumpWithValidatedAutoDig();
                TestRunnerExclusiveControlRejectsConcurrentRun();
                TestRunnerUsesOriginalSlotWhenSnapshotChangesAfterJump();
                TestRunnerDoesNotWaitForC6EventBeforeUse();
                TestRunnerEvidenceAndMultiMap();
                TestRunnerDeduplicatesEmptySnapshotDiagnostics();
                TestRunnerUsesUseOnlyOnFirstConsumptionRetry();
                TestRunnerResyncsStaleSnapshot();
                TestRunnerKeepsListeningAfterRepeatedConnectFailures();
                TestRunnerRetriesTransientJumpFailure();
                TestRunnerSkipsJumpAfterRetryBudgetAndKeepsListening();
                TestRunnerRetriesUnconsumedTargetUntilConfirmed();
                TestRunnerWaitsForDelayedConsumptionBeforeRetry();
                TestRunnerUsesOneMidRouteJumpRetry();
                TestRunnerLeavesCooldownWhenConsumptionAppears();
                TestRunnerLogsUnrecognizedTarget();
                TestRunnerReprocessesReusedSlotAfterRemoval();
                Console.WriteLine("TreasureC6StreamRegression: PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("TreasureC6StreamRegression: FAIL: " + ex);
                return 1;
            }
        }

        private static void TestProtocol()
        {
            TreasureC6StreamProtocol protocol = new TreasureC6StreamProtocol();
            protocol.ProcessLine(Common(
                "hello_ack",
                0,
                Session,
                "starting",
                message =>
                {
                    message["selectedProtocolVersion"] = 1;
                    message["selectedSchemaVersion"] = 2;
                    message["reasonCode"] = "ready";
                }));
            Assert(protocol.HandshakeComplete, "hello_ack completes handshake");

            JArray items = new JArray(
                Item("member-a", 13, 1009, 19, 65),
                Item("member-b", 14, 1018, 81, 48));
            protocol.ProcessLine(Common(
                "snapshot",
                1,
                Session,
                "ready",
                message =>
                {
                    message["processIdentity"] = ReadyIdentity();
                    message["containerIdentity"] = "bagmgr:fixture/m_ItemDict:fixture";
                    message["snapshotId"] = "snapshot-1";
                    message["items"] = items;
                    message["formalCount"] = 2;
                    message["rejectedMemberCount"] = 0;
                    message["events"] = new JArray();
                }));
            Assert(protocol.CurrentSnapshot != null, "snapshot is materialized");
            Assert(protocol.CurrentSnapshot.Items.Count == 2, "snapshot item count");
            Assert(protocol.CurrentSnapshot.Items[0].PackageNum == 13, "packageNum is the slot");

            protocol.ProcessLine(Common(
                "event",
                2,
                Session,
                "ready",
                message =>
                {
                    message["processIdentity"] = ReadyIdentity();
                    message["containerIdentity"] = "bagmgr:fixture/m_ItemDict:fixture";
                    message["snapshotId"] = "snapshot-1";
                    message["formalCount"] = 3;
                    message["eventType"] = "added";
                    message["item"] = Item("member-c", 15, 1020, 22, 33);
                }));
            Assert(protocol.CurrentSnapshot.Items.Count == 3, "added event updates membership");

            TreasureC6StreamProtocol invalidAuthProtocol = new TreasureC6StreamProtocol();
            AssertCode(
                () => invalidAuthProtocol.ProcessLine(Common(
                    "hello_ack",
                    0,
                    "invalid-auth",
                    "starting",
                    message => message["actionAuthorized"] = true)),
                "action_authorized",
                "actionAuthorized=true is rejected");
            Assert(invalidAuthProtocol.CurrentSnapshot == null, "invalid auth clears state");

            TreasureC6StreamProtocol gapProtocol = new TreasureC6StreamProtocol();
            gapProtocol.ProcessLine(Common(
                "hello_ack",
                0,
                Session,
                "starting",
                message =>
                {
                    message["selectedProtocolVersion"] = 1;
                    message["selectedSchemaVersion"] = 2;
                    message["reasonCode"] = "ready";
                }));
            AssertCode(
                () => gapProtocol.ProcessLine(Common(
                    "snapshot",
                    2,
                    Session,
                    "ready",
                    message =>
                    {
                        message["processIdentity"] = ReadyIdentity();
                        message["containerIdentity"] = "bagmgr:fixture/m_ItemDict:fixture";
                        message["snapshotId"] = "snapshot-gap";
                        message["items"] = new JArray();
                        message["formalCount"] = 0;
                        message["rejectedMemberCount"] = 0;
                        message["events"] = new JArray();
                    })),
                "sequence_gap",
                "sequence gaps are rejected");

            TreasureC6StreamProtocol boundaryProtocol = new TreasureC6StreamProtocol();
            boundaryProtocol.ProcessLine(Common(
                "hello_ack",
                0,
                Session,
                "starting",
                message =>
                {
                    message["selectedProtocolVersion"] = 1;
                    message["selectedSchemaVersion"] = 2;
                    message["reasonCode"] = "ready";
                }));
            AssertCode(
                () => boundaryProtocol.ProcessLine(Common(
                    "event",
                    0,
                    "new-session",
                    "ready",
                    message =>
                    {
                        message["processIdentity"] = ReadyIdentity();
                        message["containerIdentity"] = "bagmgr:fixture/m_ItemDict:fixture";
                        message["snapshotId"] = "snapshot-1";
                        message["formalCount"] = 1;
                        message["eventType"] = "added";
                        message["item"] = Item("member-d", 16, 1020, 22, 34);
                    })),
                "session_boundary_invalid",
                "new sessions cannot start with an event");
        }

        private static void TestDirectTargetPreparation()
        {
            TreasureMapPresetOptions defaults = new TreasureMapPresetOptions();
            Assert(defaults.Host == "127.0.0.1" && defaults.Port == 28765,
                "C6 endpoint defaults");
            Assert(defaults.UseType == 13 && defaults.UseNum == 1 && defaults.UseParam == "2",
                "advanced Use defaults");
            Assert(defaults.JumpUseDelayMilliseconds == 300 &&
                   defaults.NextTargetDelayMilliseconds == 100 &&
                   defaults.RetryDelayMilliseconds == 250 &&
                   defaults.ConsumptionConfirmTimeoutMilliseconds == 2500 &&
                   defaults.FailedTargetCooldownMilliseconds == 3000 &&
                   !defaults.StopContinuousOnTransportFailure &&
                   !defaults.LiveSendEnabled &&
                   defaults.Mode == TreasureMapExecutionMode.Continuous,
                "preset timing, continuous-mode and live-send defaults");
            Assert(!TreasurePacketRuntime.PurePacketPreset.ReadsTargetFromResidentState &&
                   TreasurePacketRuntime.PurePacketPreset.ReadsTargetFromC6Stream,
                "production preset target source is C6");

            TreasureInventoryTarget target = new TreasureInventoryTarget(13, 1009, 19, 65);
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasurePacketPreparedSet prepared =
                TreasurePacketRuntime.PrepareEncodedFromTarget(target, route, 13, 1, "2");
            Assert(prepared.Target.PackageNum == 13, "direct target binds Use pos");
            Assert(TreasurePacketEncoder.DecodeUse(prepared.UsePacket.PacketBuffer).PackageNum == 13,
                "direct target Use packet");
            Assert(TreasureAutoDigPacketContract.IsFrame(prepared.AutoDigPacket.PacketBuffer),
                "direct target AutoDig packet");
            Assert(TreasurePacketEncoder.DecodeJump(prepared.JumpPacket.PacketBuffer).X == 19,
                "direct target Jump x");

            TreasurePacketPreparedSet autoOnly =
                TreasurePacketRuntime.PrepareEncodedFromTarget(target, route);
            Assert(autoOnly.UsePacket == null &&
                   TreasureAutoDigPacketContract.IsFrame(autoOnly.AutoDigPacket.PacketBuffer),
                   "production target preparation does not require a Use frame");
        }

        private static void TestCurrentSessionUseTemplateBinding()
        {
            TreasurePacketRuntime.BeginSession();
            try
            {
                byte[] template = TreasurePacketEncoder.EncodeUse(
                    new TreasureUsePacketRequest(13, 7, 1, "2"));
                Socket_PacketInfo packet = new Socket_PacketInfo
                {
                    PacketSocket = 6168,
                    PacketType = Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    PacketFrom = "127.0.0.1:50000",
                    PacketTo = "127.0.0.1:12345",
                    PacketBuffer = template,
                    PacketLen = template.Length,
                    PacketTime = DateTime.UtcNow
                };
                TreasurePacketRuntime.ObserveCapturedPacket(packet);

                TreasureUsePacketRequest request =
                    TreasurePacketRuntime.GetCurrentSessionUseRequest(8);
                Assert(request.PackageNum == 8 &&
                       request.Type == 7 &&
                       request.Num == 1 &&
                       request.Param == "2",
                    "current session Use template keeps fields and rebinds only the C6 slot");
            }
            finally
            {
                TreasurePacketRuntime.EndSession();
            }
        }

        private static void TestPersistentRunLogStore()
        {
            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "WpeTreasureRunLogRegression",
                Guid.NewGuid().ToString("N"));
            try
            {
                TreasureMapRunLogStore store = new TreasureMapRunLogStore(
                    testRoot,
                    600,
                    2);
                Guid runId = Guid.NewGuid();
                TreasureC6InventoryItem target = new TreasureC6InventoryItem(
                    "member-sensitive-value",
                    13,
                    1009,
                    19,
                    65);
                TreasureMapLogEntry entry = new TreasureMapLogEntry(
                    runId,
                    "jump",
                    "Jump",
                    target,
                    "sent",
                    0,
                    true,
                    7);

                Assert(store.TryAppend(entry), "persistent run log accepts a structured entry");
                Assert(File.Exists(store.LogFilePath), "persistent run log creates the active JSONL file");
                string firstLine = File.ReadLines(store.LogFilePath).Single();
                JObject record = JObject.Parse(firstLine);
                Assert(record.Value<int>("schemaVersion") == TreasureMapRunLogStore.CurrentSchemaVersion,
                    "persistent run log schema version");
                Assert(record.Value<string>("runId") == runId.ToString("D"),
                    "persistent run log correlates a run id");
                Assert(record.Value<string>("step") == "jump" &&
                       record.Value<string>("packetKind") == "Jump" &&
                       record.Value<string>("code") == "sent" &&
                       record.Value<bool>("success"),
                    "persistent run log preserves the diagnostic result");
                Assert(record.Value<long>("attemptId") == 7,
                    "persistent run log correlates a target attempt");
                Assert(record["target"].Value<int>("slot") == 13 &&
                       record["target"].Value<int>("scene") == 1009 &&
                       record["target"].Value<int>("x") == 19 &&
                       record["target"].Value<int>("y") == 65,
                    "persistent run log preserves safe target coordinates");
                Assert(firstLine.IndexOf("member-sensitive-value", StringComparison.Ordinal) < 0 &&
                       firstLine.IndexOf("packetBuffer", StringComparison.OrdinalIgnoreCase) < 0,
                    "persistent run log excludes member identities and raw packet bytes");

                for (int index = 0; index < 12; index++)
                {
                    Assert(store.TryAppend(new TreasureMapLogEntry(
                        runId,
                        "recognition",
                        "C6",
                        target,
                        "rotation-fixture-" + index,
                        index,
                        false)),
                        "persistent run log remains writable during bounded rotation");
                }

                string[] files = Directory.GetFiles(testRoot, "treasure-map*.jsonl");
                Assert(files.Length <= 3 && files.Length >= 2,
                    "persistent run log keeps one active file and at most two archives");
                Assert(files.Any(path => path.EndsWith("treasure-map.1.jsonl", StringComparison.OrdinalIgnoreCase)),
                    "persistent run log creates a first archive");
            }
            finally
            {
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                string resolvedTestRoot = Path.GetFullPath(testRoot);
                if (resolvedTestRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
                    Directory.Exists(resolvedTestRoot))
                {
                    Directory.Delete(resolvedTestRoot, true);
                }
            }
        }

        private static void TestTcpClientHandshake()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Exception serverError = null;
            Task serverTask = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        using (TcpClient accepted = listener.AcceptTcpClient())
                        using (NetworkStream network = accepted.GetStream())
                        using (StreamReader reader = new StreamReader(network, new UTF8Encoding(false)))
                        using (StreamWriter writer = new StreamWriter(network, new UTF8Encoding(false))
                        {
                            AutoFlush = true
                        })
                        {
                            string hello = reader.ReadLine();
                            Assert(hello != null && hello.Contains("piaomiao.treasure.stream"),
                                "TCP client sends C6 hello");
                            writer.WriteLine(Common(
                                "hello_ack",
                                0,
                                "tcp-session",
                                "starting",
                                message =>
                                {
                                    message["selectedProtocolVersion"] = 1;
                                    message["selectedSchemaVersion"] = 2;
                                    message["reasonCode"] = "ready";
                                }));
                            writer.WriteLine(Common(
                                "snapshot",
                                1,
                                "tcp-session",
                                "ready",
                                message =>
                                {
                                    message["processIdentity"] = ReadyIdentity();
                                    message["containerIdentity"] = "bagmgr:fixture/m_ItemDict:fixture";
                                    message["snapshotId"] = "tcp-snapshot";
                                    message["items"] = new JArray(Item("member-a", 13, 1009, 19, 65));
                                    message["formalCount"] = 1;
                                    message["rejectedMemberCount"] = 0;
                                    message["events"] = new JArray();
                                }));
                        }
                    }
                    catch (Exception ex)
                    {
                        serverError = ex;
                    }
                },
                CancellationToken.None);

            try
            {
                using (TreasureC6StreamClient client = new TreasureC6StreamClient(
                    "127.0.0.1",
                    port,
                    2000))
                {
                    client.Connect(CancellationToken.None);
                    TreasureC6StreamMessage snapshot = client.ReadNext(CancellationToken.None);
                    Assert(snapshot != null && snapshot.MessageType == "snapshot",
                        "TCP client reads C6 snapshot after handshake");
                    Assert(client.CurrentSnapshot != null &&
                        client.CurrentSnapshot.Items.Count == 1,
                        "TCP client exposes current snapshot");
                }
            }
            finally
            {
                listener.Stop();
            }

            Assert(serverTask.Wait(3000), "synthetic C6 TCP server completes");
            if (serverError != null)
            {
                throw new InvalidOperationException("Synthetic C6 TCP server failed.", serverError);
            }
        }

        private static void TestBufferedTcpClient()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Exception serverError = null;
            Task serverTask = Task.Run(() =>
            {
                try
                {
                    using (TcpClient serverClient = listener.AcceptTcpClient())
                    using (NetworkStream network = serverClient.GetStream())
                    using (StreamReader reader = new StreamReader(network, Encoding.UTF8))
                    using (StreamWriter writer = new StreamWriter(network, new UTF8Encoding(false))
                    {
                        AutoFlush = true
                    })
                    {
                        Assert(reader.ReadLine() != null, "buffered client sends C6 hello");
                        writer.WriteLine(Common(
                            "hello_ack",
                            0,
                            "buffered-session",
                            "starting",
                            message =>
                            {
                                message["selectedProtocolVersion"] = 1;
                                message["selectedSchemaVersion"] = 2;
                                message["reasonCode"] = "ready";
                            }));
                        writer.WriteLine(Common(
                            "snapshot",
                            1,
                            "buffered-session",
                            "ready",
                            message =>
                            {
                                message["processIdentity"] = ReadyIdentity();
                                message["containerIdentity"] = "bagmgr:fixture/m_ItemDict:fixture";
                                message["snapshotId"] = "buffered-snapshot";
                                message["items"] = new JArray(Item("member-a", 13, 1009, 19, 65));
                                message["formalCount"] = 1;
                                message["rejectedMemberCount"] = 0;
                                message["events"] = new JArray();
                            }));
                        writer.WriteLine(Common(
                            "event",
                            2,
                            "buffered-session",
                            "ready",
                            message =>
                            {
                                message["processIdentity"] = ReadyIdentity();
                                message["containerIdentity"] = "bagmgr:fixture/m_ItemDict:fixture";
                                message["snapshotId"] = "buffered-snapshot";
                                message["formalCount"] = 2;
                                message["eventType"] = "added";
                                message["item"] = Item("member-b", 14, 1018, 81, 48);
                            }));
                        Thread.Sleep(2000);
                    }
                }
                catch (Exception ex)
                {
                    serverError = ex;
                }
            });

            try
            {
                using (TreasureC6BufferedInventoryStream stream =
                    new TreasureC6BufferedInventoryStream(
                        new TreasureC6StreamClient("127.0.0.1", port, 2000)))
                {
                    stream.Connect(CancellationToken.None);
                    using (CancellationTokenSource waitForEvent =
                        new CancellationTokenSource(3000))
                    {
                        TreasureC6StreamMessage message = null;
                        while (stream.CurrentSnapshot == null ||
                               stream.CurrentSnapshot.Items.Count < 2)
                        {
                            message = stream.ReadNext(waitForEvent.Token);
                        }
                        Assert(message != null &&
                            (message.MessageType == "snapshot" || message.MessageType == "event"),
                            "buffered client returns a C6 notification");
                    }
                    Assert(stream.CurrentSnapshot != null &&
                        stream.CurrentSnapshot.Items.Count == 2,
                        "buffered client keeps latest snapshot while runner consumes notification");
                }
            }
            finally
            {
                listener.Stop();
            }

            Assert(serverTask.Wait(3000), "buffered synthetic C6 TCP server completes");
            if (serverError != null)
            {
                throw new InvalidOperationException("Buffered synthetic C6 TCP server failed.", serverError);
            }
        }

        private static void TestInstructionCodec()
        {
            TreasureMapInstructionDefinition definition;
            Assert(TreasureMapInstructionCodec.TryDecode(
                TreasureMapInstructionCodec.Encode(TreasureMapExecutionMode.CurrentSnapshot),
                out definition),
                "v2 current instruction decodes");
            Assert(definition.Version == TreasureMapInstructionVersion.V2 &&
                   definition.Mode == TreasureMapExecutionMode.CurrentSnapshot,
                "v2 current instruction preserves mode");

            Assert(TreasureMapInstructionCodec.TryDecode(
                TreasurePacketRuntime.PurePacketPreset.Id,
                out definition),
                "legacy instruction decodes");
            Assert(definition.Version == TreasureMapInstructionVersion.V1 &&
                   definition.Mode == TreasureMapExecutionMode.Continuous,
                "legacy instruction preserves continuous semantics");
            Assert(!TreasureMapInstructionCodec.TryDecode(
                "TREASURE_MAP_V2|mode=invalid",
                out definition),
                "invalid mode is rejected");
            Assert(TreasureMapInstructionCodec.DecodeOrDefault("invalid").Mode ==
                   TreasureMapExecutionMode.Continuous,
                 "invalid or missing instruction falls back to continuous mode");

            string v3 = TreasureMapInstructionCodec.Encode(
                TreasureMapExecutionMode.CurrentSnapshot,
                TreasureControlMode.Cooperative,
                TreasureEvidenceMode.Enforced);
            Assert(TreasureMapInstructionCodec.TryDecode(v3, out definition),
                "v3 instruction decodes");
            Assert(definition.Version == TreasureMapInstructionVersion.V3 &&
                   definition.Mode == TreasureMapExecutionMode.CurrentSnapshot &&
                   definition.Control == TreasureControlMode.Cooperative &&
                   definition.Evidence == TreasureEvidenceMode.Enforced,
                "v3 instruction preserves control and evidence policy");
        }

        private static void TestEvidenceDecoder()
        {
            byte[] enterMap = new byte[28];
            enterMap[0] = 0x4D;
            enterMap[1] = 0x5A;
            enterMap[10] = 0xFF;
            enterMap[11] = 0xE1;
            WriteInt32BigEndian(enterMap, 12, 1009);
            WriteInt32BigEndian(enterMap, 16, 19);
            WriteInt32BigEndian(enterMap, 20, 65);
            TreasureJumpArrivalEvidence evidence =
                TreasureEvidenceDecoder.DecodeEnterMap(enterMap);
            Assert(evidence.EvidenceType == TreasureJumpArrivalEvidenceType.EnterMap &&
                   evidence.MatchesTarget(1009, 19, 65),
                "enter-map evidence decodes MZ protocol and coordinates");

            byte[] playerJump = new byte[32];
            playerJump[0] = 0x4D;
            playerJump[1] = 0x5A;
            playerJump[10] = 0x10;
            playerJump[11] = 0x99;
            WriteInt32BigEndian(playerJump, 20, 1009);
            WriteInt32BigEndian(playerJump, 24, 19);
            WriteInt32BigEndian(playerJump, 28, 65);
            evidence = TreasureEvidenceDecoder.DecodePlayerJumpToPos(playerJump);
            Assert(evidence.EvidenceType == TreasureJumpArrivalEvidenceType.PlayerJumpToPos &&
                   evidence.MatchesTarget(1009, 19, 65),
                "player-jump evidence decodes actor-prefixed coordinates");

            byte[] simplePlayerJump = new byte[24];
            simplePlayerJump[0] = 0x4D;
            simplePlayerJump[1] = 0x5A;
            simplePlayerJump[10] = 0x10;
            simplePlayerJump[11] = 0x99;
            WriteInt32BigEndian(simplePlayerJump, 12, 1009);
            WriteInt32BigEndian(simplePlayerJump, 16, 19);
            WriteInt32BigEndian(simplePlayerJump, 20, 65);
            evidence = TreasureEvidenceDecoder.DecodePlayerJumpToPos(simplePlayerJump);
            Assert(evidence.MatchesTarget(1009, 19, 65),
                "player-jump evidence falls back to the compact coordinate layout");

            byte[] alternateEnterMap = new byte[28];
            alternateEnterMap[0] = 0x4D;
            alternateEnterMap[1] = 0x5A;
            alternateEnterMap[12] = 0xFF;
            alternateEnterMap[13] = 0xE1;
            WriteInt32BigEndian(alternateEnterMap, 14, 1009);
            WriteInt32BigEndian(alternateEnterMap, 18, 19);
            WriteInt32BigEndian(alternateEnterMap, 22, 65);
            evidence = TreasureEvidenceDecoder.DecodeEnterMap(alternateEnterMap);
            Assert(evidence.MatchesTarget(1009, 19, 65),
                "MZ evidence also accepts the alternate protocol offset");

            byte[] useResult = new byte[16];
            useResult[0] = 0x4D;
            useResult[1] = 0x5A;
            useResult[10] = 0x81;
            useResult[11] = 0x12;
            WriteInt32BigEndian(useResult, 12, 1);
            TreasureUseResultEvidence result =
                TreasureEvidenceDecoder.DecodeRespPotholing(useResult);
            Assert(result.Consumed && result.Result == 1,
                "RespPotholing result decodes safely");
        }

        private static void TestRunnerEvidenceAndMultiMap()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-runner-session",
                    "runner-snapshot",
                    new[]
                    {
                        new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65),
                        new TreasureC6InventoryItem("member-b", 14, 1018, 81, 48)
                    },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 1,
                RetryDelayMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.CurrentSnapshot
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => route,
                logs.Add,
                options);
            runner.Run(cancellation.Token);

            Assert(sender.Calls == 4, "runner processes two maps without long retry backoff");
            Assert(sender.Packets.Count(packet => packet.PacketBuffer.Length == 28) == 2,
                "runner sends one Jump packet per map");
            Assert(sender.Packets.Count(packet => packet.PacketBuffer.Length == 24) == 2,
                "runner sends one AutoDig packet per map");
            Assert(logs.Any(entry => entry.Success && entry.Step == "auto_dig"),
                "runner logs successful AutoDig sends");
            TreasureMapLogEntry successfulAutoDig = logs.First(entry => entry.Success && entry.Step == "auto_dig");
            Assert(
                successfulAutoDig.ToString().IndexOf("code=", StringComparison.Ordinal) <
                successfulAutoDig.ToString().IndexOf("packet=", StringComparison.Ordinal),
                "log text exposes result code before the truncated packet field");
        }

        private static void TestRunnerUsesGuardedJumpWithValidatedAutoDig()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-guarded-auto-dig-session",
                    "guarded-auto-dig-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream,
                UsesBeforeConsumption = 1
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            byte[] autoDigBytes = TreasurePacketEncoder.EncodeAutoDig();
            Socket_PacketInfo autoDigPacket = new Socket_PacketInfo
            {
                PacketType = route.PacketType,
                PacketFrom = route.PacketFrom,
                PacketTo = route.PacketTo,
                PacketBuffer = autoDigBytes,
                PacketLen = autoDigBytes.Length,
                PacketTime = DateTime.UtcNow
            };
            Socket_SendInfo preset = new Socket_SendInfo(
                false,
                Guid.NewGuid(),
                "挖宝图",
                true,
                0,
                1000,
                new System.ComponentModel.BindingList<Socket_PacketInfo>(
                    new[] { autoDigPacket }),
                string.Empty);
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();

            TreasurePacketRuntime.BeginSession();
            Socket_Cache.SocketList.lstRecPacket.Clear();
            Socket_Cache.SendList.lstSend.Add(preset);
            try
            {
                TreasureMapPresetOptions options = new TreasureMapPresetOptions
                {
                    UseNativeAutoDig = true,
                    RequireCurrentPacketTemplates = true,
                    JumpUseDelayMilliseconds = 0,
                    NextTargetDelayMilliseconds = 0,
                    ConsumptionConfirmTimeoutMilliseconds = 1,
                    LiveSendEnabled = true,
                    Mode = TreasureMapExecutionMode.CurrentSnapshot
                };
                TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                    stream,
                    sender,
                    () => route,
                    logs.Add,
                    options);
                runner.Run(cancellation.Token);

                Assert(sender.Calls == 2,
                    "validated current-route AutoDig permits one guarded Jump and one AutoDig");
                Assert(sender.Packets.Count(packet =>
                        packet.PacketBuffer.Length == TreasureJumpPacketContract.FrameLength) == 1,
                    "guarded AutoDig path sends one encoded Jump when no Jump template was captured");
                Assert(sender.Packets.Count(packet =>
                        TreasureAutoDigPacketContract.IsFrame(packet.PacketBuffer)) == 1,
                    "guarded AutoDig path sends the validated AutoDig template");
                Assert(!logs.Any(entry => entry.Code == "jump_template_not_found"),
                    "guarded AutoDig path does not fail at the strict Jump gate");
            }
            finally
            {
                Socket_Cache.SendList.lstSend.Remove(preset);
                Socket_Cache.SocketList.lstRecPacket.Clear();
                TreasurePacketRuntime.EndSession();
            }
        }

        private static void TestRunnerDoesNotJumpWithoutCompleteActionTemplate()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-action-template-gate-session",
                    "action-template-gate-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();

            TreasurePacketRuntime.BeginSession();
            Socket_Cache.SocketList.lstRecPacket.Clear();
            try
            {
                TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                    stream,
                    sender,
                    () => route,
                    logs.Add,
                    new TreasureMapPresetOptions
                    {
                        UseNativeAutoDig = false,
                        RequireCurrentPacketTemplates = true,
                        RequireActionTemplateBeforeJump = true,
                        LiveSendEnabled = true,
                        Mode = TreasureMapExecutionMode.CurrentSnapshot
                    });
                runner.Run(cancellation.Token);

                Assert(sender.Calls == 0,
                    "missing current Use/AutoDig templates must block Jump before any send");
                Assert(logs.Any(entry =>
                        entry.Code == "action_templates_not_ready:jump_template_not_found" ||
                        entry.Code == "action_templates_not_ready:use_template_not_found"),
                    "the action template gate records the missing complete path");
                Assert(runner.CurrentState == TreasureMapState.Failed,
                    "the action template gate fails closed");
            }
            finally
            {
                Socket_Cache.SocketList.lstRecPacket.Clear();
                TreasurePacketRuntime.EndSession();
            }
        }

        private static void TestRunnerKeepsJumpWhenUseTemplateIsMissing()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-jump-without-use-session",
                    "jump-without-use-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream
            };
            TcpListener routeListener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                routeListener.Start();
                using (TcpClient routeClient = new TcpClient())
                {
                    int routePort = ((IPEndPoint)routeListener.LocalEndpoint).Port;
                    routeClient.Connect(IPAddress.Loopback, routePort);
                    using (TcpClient routeServer = routeListener.AcceptTcpClient())
                    {
                        IPEndPoint localEndpoint = (IPEndPoint)routeClient.Client.LocalEndPoint;
                        IPEndPoint remoteEndpoint = (IPEndPoint)routeClient.Client.RemoteEndPoint;
                        TreasurePacketRoute route = new TreasurePacketRoute(
                            Socket_Cache.SocketPacket.PacketType.WS2_Send,
                            localEndpoint.ToString(),
                            remoteEndpoint.ToString());
                        byte[] jumpBytes = TreasurePacketTemplatePatcher.BuildJump(
                            new TreasureInventoryTarget(13, 1009, 19, 65));
                        Socket_PacketInfo jumpTemplate = new Socket_PacketInfo
                        {
                            PacketSocket = routeClient.Client.Handle.ToInt32(),
                            PacketType = route.PacketType,
                            PacketFrom = route.PacketFrom,
                            PacketTo = route.PacketTo,
                            PacketBuffer = jumpBytes,
                            PacketLen = jumpBytes.Length,
                            PacketTime = DateTime.UtcNow
                        };
                        List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();

                        TreasurePacketRuntime.BeginSession();
                        Socket_Cache.SocketList.lstRecPacket.Clear();
                        TreasurePacketRuntime.ObserveCapturedPacket(jumpTemplate);
                        Socket_Cache.SocketList.lstRecPacket.Add(jumpTemplate);
                        try
                        {
                            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                                stream,
                                sender,
                                () => route,
                                logs.Add,
                                new TreasureMapPresetOptions
                                {
                                    UseNativeAutoDig = false,
                                    RequireCurrentPacketTemplates = true,
                                    RequireActionTemplateBeforeJump = true,
                                    JumpUseDelayMilliseconds = 0,
                                    NextTargetDelayMilliseconds = 0,
                                    ConsumptionConfirmTimeoutMilliseconds = 1,
                                    LiveSendEnabled = true,
                                    Mode = TreasureMapExecutionMode.CurrentSnapshot
                                });
                            runner.Run(cancellation.Token);

                        Assert(sender.Calls == 1,
                            "a current Jump template must still allow one Jump when Use is missing");
                            Assert(sender.Packets.Count == 1 &&
                                   TreasurePacketEncoder.DecodeJump(sender.Packets[0].PacketBuffer).MapId == 1009,
                                "the restored Jump path sends the current target coordinates");
                            Assert(logs.Any(entry =>
                                    entry.Step == "preflight" &&
                                    entry.Code == "use_template_not_found;continue_with_jump"),
                                "missing Use is recorded without blocking the Jump");
                            Assert(logs.Any(entry =>
                                    entry.Step == "use" &&
                                    entry.Code == "use_template_not_found"),
                                "Use remains fail-closed when no current Use template exists");
                            Assert(runner.CurrentState == TreasureMapState.Failed,
                                "the runner stops after the missing Use is confirmed before dispatch");
                        }
                        finally
                        {
                            Socket_Cache.SocketList.lstRecPacket.Clear();
                            TreasurePacketRuntime.EndSession();
                        }
                    }
                }
            }
            finally
            {
                routeListener.Stop();
            }
        }

        private static void TestRunnerUsesUseOnlyOnFirstConsumptionRetry()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-use-retry-session",
                    "use-retry-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream,
                UsesBeforeConsumption = 2
            };
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                UseNativeAutoDig = false,
                JumpUseDelayMilliseconds = 0,
                NextTargetDelayMilliseconds = 0,
                ConsumptionConfirmTimeoutMilliseconds = 1,
                FailedTargetCooldownMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                logs.Add,
                options);

            runner.Run(cancellation.Token);

            Assert(sender.Calls == 3,
                "first consumption retry sends Use only before a full pair retry");
            Assert(sender.Packets.Count(packet => packet.PacketBuffer.Length == TreasureJumpPacketContract.FrameLength) == 1,
                "Use-only first retry does not repeat Jump");
            Assert(sender.Packets.Count(packet => IsUsePacket(packet)) == 2,
                "production retry path sends the original Use twice");
            Assert(logs.Count(entry => entry.Step == "use") == 2 &&
                   logs.Count(entry => entry.Code == "target_retry") == 1,
                "Use-only retry is visible in the structured timeline");
            TreasureMapLogEntry[] useLogs = logs.Where(entry => entry.Step == "use").ToArray();
            Assert(useLogs[0].AttemptId > 0 && useLogs[1].AttemptId > useLogs[0].AttemptId,
                "Use attempts receive increasing attempt IDs");
            Assert(useLogs.All(entry => entry.Target != null && entry.Target.X == 19 && entry.Target.Y == 65),
                "Use logs retain the prepared target after the inventory changes");
        }

        private static void TestRunnerDeduplicatesEmptySnapshotDiagnostics()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-empty-session",
                    "empty-snapshot",
                    new TreasureC6InventoryItem[0],
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                new RegressionSender { FailuresRemaining = 0, Stream = stream },
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                logs.Add,
                new TreasureMapPresetOptions
                {
                    LiveSendEnabled = true,
                    Mode = TreasureMapExecutionMode.Continuous
                });

            runner.Run(cancellation.Token);

            Assert(logs.Count(entry => entry.Step == "recognition" &&
                       entry.Code.StartsWith("snapshot_empty;", StringComparison.Ordinal)) == 1,
                "repeated empty snapshots are reduced to one diagnostic during a fast idle loop");
        }

        private static bool IsUsePacket(Socket_PacketInfo packet)
        {
            try
            {
                TreasurePacketEncoder.DecodeUse(packet.PacketBuffer);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TestRunnerResyncsStaleSnapshot()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            TreasureC6InventorySnapshot recoveredSnapshot = new TreasureC6InventorySnapshot(
                "c6-resync-session",
                "resync-snapshot",
                new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                "31531|7373672|/system/bin/app_process64",
                "bagmgr:fixture/m_ItemDict:fixture");
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                ThrowStaleSnapshotOnFirstRead = true,
                SnapshotAfterStale = recoveredSnapshot,
                CancelAfterRead = true
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                RetryDelayMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => route,
                logs.Add,
                options);

            runner.Run(cancellation.Token);

            Assert(stream.Connects >= 2,
                "stale C6 snapshot triggers a fresh stream connection");
            Assert(logs.Any(entry => entry.Code == "c6_resync_required:stale_snapshot_rejected"),
                "runner records stale snapshot resynchronization");
            Assert(logs.Any(entry => entry.Code == "c6_resync_connected" && entry.Success),
                "runner records successful C6 resynchronization");
            Assert(sender.Calls == 2,
                "runner resumes the target only after a complete snapshot is restored");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "runner remains stoppable after resynchronization");
        }

        private static void TestRunnerRetriesTransientJumpFailure()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-skip-session",
                    "skip-snapshot",
                    new[]
                    {
                        new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65),
                        new TreasureC6InventoryItem("member-b", 14, 1018, 81, 48)
                    },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            // 第一张 Jump 明确未发送时短重试，恢复后两张都应完成。
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 1,
                Stream = stream
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                RetryDelayMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => route,
                logs.Add,
                options);

            runner.Run(cancellation.Token);

            Assert(sender.Calls == 5,
                "a transient Jump failure is retried once before both targets complete");
            Assert(sender.Packets.Count(packet => packet.PacketBuffer.Length == 24) == 2,
                "both recovered targets receive exactly one AutoDig packet");
            Assert(logs.Count(entry => entry.Step == "jump_retry") == 1,
                "runner records the scheduled transient Jump retry");
            Assert(!logs.Any(entry => entry.Code == "target_failed_skipped"),
                "a recovered Jump target is not added to the skipped ledger");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "continuous runner only stops after the external cancellation");
        }

        private static void TestRunnerSkipsJumpAfterRetryBudgetAndKeepsListening()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-retry-exhausted-session",
                    "retry-exhausted-snapshot",
                    new[]
                    {
                        new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65),
                        new TreasureC6InventoryItem("member-b", 14, 1018, 81, 48)
                    },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            // 初次 Jump 和两次短重试都明确未发送，才跳过第一张并继续第二张。
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 3,
                Stream = stream
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                RetryDelayMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => route,
                logs.Add,
                options);

            runner.Run(cancellation.Token);

            Assert(sender.Calls == 5,
                "three failed Jump attempts are followed by the next target Jump and AutoDig");
            Assert(sender.Packets.Count(packet => packet.PacketBuffer.Length == 24) == 1,
                "an exhausted Jump target never receives AutoDig");
            Assert(logs.Count(entry => entry.Step == "jump_retry") == 2,
                "Jump retry budget is exactly two additional attempts");
            Assert(logs.Any(entry => entry.Code == "target_failed_skipped"),
                "runner skips the failed target only after the Jump retry budget is exhausted");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "continuous runner keeps listening after the exhausted target is skipped");
        }

        private static void TestRunnerKeepsListeningAfterRepeatedConnectFailures()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                ConnectFailuresRemaining = 6,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-reconnect-session",
                    "reconnect-snapshot",
                    Array.Empty<TreasureC6InventoryItem>(),
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                new RegressionSender { FailuresRemaining = 0 },
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                logs.Add,
                options);

            runner.Run(cancellation.Token);

            Assert(stream.Connects >= 7,
                "continuous runner retries after an exhausted reconnect cycle");
            Assert(logs.Any(entry => entry.Code == "c6_resync_waiting"),
                "continuous runner records the wait between reconnect cycles");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "repeated C6 connection failures do not stop the continuous runner");
        }

        private static void TestRunnerRetriesUnconsumedTargetUntilConfirmed()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-consume-retry-session",
                    "consume-retry-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream,
                UsesBeforeConsumption = 3
            };
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                NextTargetDelayMilliseconds = 0,
                ConsumptionConfirmTimeoutMilliseconds = 1,
                FailedTargetCooldownMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                logs.Add,
                options);

            runner.Run(cancellation.Token);

            Assert(sender.Calls == 4,
                "an unconsumed target retries AutoDig without repeating the same coordinate Jump");
            Assert(sender.Packets.Count(packet =>
                           packet.PacketBuffer.Length == TreasureJumpPacketContract.FrameLength) == 1,
                "AutoDig retries keep the original Jump and do not refly the target");
            Assert(sender.Packets.Count(packet =>
                           TreasureAutoDigPacketContract.IsFrame(packet.PacketBuffer)) == 3,
                "AutoDig retries resend only the digging packet until confirmed");
            Assert(logs.Count(entry => entry.Code == "consume_timeout") == 2,
                "each ignored AutoDig is recorded as a consumption timeout");
            Assert(logs.Count(entry => entry.Code == "target_retry") == 1,
                "the first ignored AutoDig receives one retry without a new Jump");
            Assert(logs.Count(entry => entry.Code == "target_cooldown") == 1,
                "the second ignored AutoDig enters the configured cooldown");
            Assert(logs.Any(entry => entry.Code == "consume_confirmed" && entry.Success),
                "the target completes only after the inventory confirms consumption");
            Assert(!logs.Any(entry => entry.Code == "target_failed_skipped"),
                "an unconsumed target is not permanently skipped");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "the retrying continuous runner remains active until externally stopped");
        }

        private static void TestRunnerWaitsForDelayedConsumptionBeforeRetry()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-delayed-consume-session",
                    "delayed-consume-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream,
                UsesBeforeConsumption = 1,
                ConsumptionDelayMilliseconds = 1200
            };
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                NextTargetDelayMilliseconds = 0,
                ConsumptionConfirmTimeoutMilliseconds = 2500,
                FailedTargetCooldownMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                logs.Add,
                options);

            runner.Run(cancellation.Token);
            sender.DelayedConsumptionTask?.Wait();

            Assert(sender.Calls == 2,
                "a consumption confirmed after one second does not trigger a duplicate Jump and AutoDig");
            Assert(!logs.Any(entry => entry.Code == "consume_timeout" || entry.Code == "target_retry"),
                "delayed consumption within the confirmation window is not treated as a retry");
            Assert(logs.Any(entry => entry.Code == "consume_confirmed" && entry.Success),
                "delayed consumption completes after the inventory update arrives");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "the delayed-consumption runner remains active until externally stopped");
        }

        private static void TestRunnerUsesOneMidRouteJumpRetry()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-mid-route-retry-session",
                    "mid-route-retry-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream,
                UsesBeforeConsumption = 1,
                ConsumptionDelayMilliseconds = 300
            };
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                UseNativeAutoDig = false,
                JumpUseDelayMilliseconds = 0,
                NextTargetDelayMilliseconds = 0,
                TargetStabilityMilliseconds = 20,
                MinimumJumpIntervalMilliseconds = 150,
                JumpRetryDelayMilliseconds = 100,
                ConsumptionConfirmTimeoutMilliseconds = 1000,
                FailedTargetCooldownMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                logs.Add,
                options);

            runner.Run(cancellation.Token);
            sender.DelayedConsumptionTask?.Wait();

            Assert(sender.Calls == 3,
                "a delayed consumption receives one controlled mid-route Jump retry");
            Assert(sender.Packets.Count(packet =>
                    packet.PacketBuffer.Length == TreasureJumpPacketContract.FrameLength) == 2,
                "the mid-route retry never sends more than one additional Jump");
            Assert(logs.Any(entry => entry.Step == "target_stable" && entry.Success),
                "the target stability window is recorded before the first Jump");
            Assert(logs.Any(entry => entry.Step == "jump_throttle_wait"),
                "the minimum Jump interval is enforced before the retry");
            Assert(logs.Any(entry => entry.Step == "jump_retry" &&
                                     entry.Code.StartsWith("mid_route_retry:", StringComparison.Ordinal)),
                "the mid-route Jump retry is explicitly recorded");
            Assert(!logs.Any(entry => entry.Code == "target_retry"),
                "consumption confirmed after the mid-route retry avoids a duplicate Use retry");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "the mid-route retry runner remains active until externally stopped");
        }

        private static void TestRunnerLeavesCooldownWhenConsumptionAppears()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-cooldown-confirm-session",
                    "cooldown-confirm-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream,
                UsesBeforeConsumption = int.MaxValue
            };
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                NextTargetDelayMilliseconds = 0,
                ConsumptionConfirmTimeoutMilliseconds = 1,
                FailedTargetCooldownMilliseconds = 1000,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            Task delayedConsumption = null;
            DateTime started = DateTime.UtcNow;
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                entry =>
                {
                    logs.Add(entry);
                    if (entry.Code == "target_cooldown" && delayedConsumption == null)
                    {
                        delayedConsumption = Task.Run(async () =>
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                            stream.ConsumePackage(13);
                        });
                    }
                },
                options);

            runner.Run(cancellation.Token);
            delayedConsumption?.Wait();
            TimeSpan elapsed = DateTime.UtcNow - started;

            Assert(sender.Calls == 3,
                "consumption during cooldown prevents a third Jump and AutoDig pair");
            Assert(elapsed.TotalMilliseconds < 800,
                "cooldown exits soon after inventory consumption instead of waiting the full second");
            Assert(logs.Any(entry =>
                entry.Code == "consume_confirmed" &&
                entry.Retry == 2 &&
                entry.Success),
                "consumption detected during cooldown is confirmed immediately");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "cooldown confirmation leaves the continuous runner externally stoppable");
        }

        private static void TestRunnerLogsUnrecognizedTarget()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-unrecognized-session",
                    "unrecognized-snapshot",
                    new[]
                    {
                        new TreasureC6InventoryItem("member-empty", 0, 1009, 19, 65)
                    },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                new RegressionSender { FailuresRemaining = 0 },
                () => route,
                logs.Add,
                options);

            // 连续模式在没有有效 packageNum 时只记录诊断并继续监听；
            // 由测试流在若干次读取后取消，避免测试永久等待。
            stream.CancelAfterRead = true;
            runner.Run(cancellation.Token);

            Assert(logs.Any(entry =>
                entry.Step == "recognition" &&
                entry.Code.StartsWith("target_not_recognized;", StringComparison.Ordinal)),
                "runner records an explicit unrecognized-target diagnostic");
            Assert(!logs.Any(entry => entry.Step == "jump" || entry.Step == "auto_dig"),
                "unrecognized target does not attempt Jump or Use");
        }

        private static void TestRunnerReprocessesReusedSlotAfterRemoval()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                RemoveFirstTargetOnFirstRead = true,
                ReAddTargetAfterRemovalOnSecondRead = true,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-reused-slot-session",
                    "reused-slot-snapshot",
                    new[]
                    {
                        new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65)
                    },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                RetryDelayMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous
            };
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => route,
                entry => { },
                options);

            runner.Run(cancellation.Token);

            Assert(sender.Calls == 4,
                "a reused bag slot is processed again after the previous map leaves inventory");
            Assert(sender.Packets.Count(packet => packet.PacketBuffer.Length == 24) == 2,
                "the reused slot receives AutoDig once per map");
            Assert(runner.CurrentState == TreasureMapState.Stopping,
                "reused-slot listener remains stoppable after the regression");
        }

        private static void TestRunnerUsesAfterJumpWithoutArrivalEvidence()
        {
            RegressionStream stream = new RegressionStream
            {
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-safe-session",
                    "safe-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender { FailuresRemaining = 0 };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                RetryDelayMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.CurrentSnapshot
            };

            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => route,
                entry => { },
                options);
            runner.Run();

            Assert(sender.Calls == 2, "runner sends Jump and AutoDig without arrival evidence");
            Assert(sender.Packets.Count(packet => packet.PacketBuffer.Length == 24) == 1,
                "runner sends AutoDig after the configured delay without arrival evidence");
            Assert(runner.CurrentState == TreasureMapState.Completed,
                "runner completes immediately after AutoDig dispatch");
        }

        private static void TestRunnerEnforcedArrivalEvidenceFailsClosed()
        {
            RegressionStream stream = new RegressionStream
            {
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-enforced-session",
                    "enforced-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = stream
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                logs.Add,
                new TreasureMapPresetOptions
                {
                    JumpUseDelayMilliseconds = 0,
                    ArrivalEvidenceTimeoutMilliseconds = 0,
                    LiveSendEnabled = true,
                    EvidenceMode = TreasureEvidenceMode.Enforced,
                    Mode = TreasureMapExecutionMode.CurrentSnapshot
                });

            runner.Run();

            Assert(sender.Calls == 1,
                "enforced arrival evidence stops after the dispatched Jump");
            Assert(runner.CurrentState == TreasureMapState.Failed,
                "missing enforced arrival evidence fails closed");
            Assert(runner.LastError == "arrival_evidence_timeout",
                "enforced arrival timeout exposes a deterministic error code");
            Assert(logs.Any(entry => entry.Code == "arrival_evidence_timeout" && !entry.Success),
                "enforced arrival timeout is recorded in the structured timeline");
            Assert(!sender.Packets.Any(packet => packet.PacketBuffer.Length == 24),
                "missing enforced arrival evidence does not send AutoDig");
        }

        private static void TestRunnerStopsContinuousAfterSocketSendFailure()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            RegressionStream stream = new RegressionStream
            {
                StopSource = cancellation,
                CancelAfterRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-socket-failure-session",
                    "socket-failure-snapshot",
                    new[]
                    {
                        new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65),
                        new TreasureC6InventoryItem("member-b", 14, 1010, 20, 66)
                    },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender
            {
                FailuresRemaining = 0,
                FailureOnCall = 3,
                FailureCode = "socket_send_failed",
                FailureDisposition = TreasureMapPacketSendDisposition.Ambiguous,
                Stream = stream
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();
            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => new TreasurePacketRoute(
                    Socket_Cache.SocketPacket.PacketType.WS2_Send,
                    "127.0.0.1:50000",
                    "127.0.0.1:12345"),
                logs.Add,
                new TreasureMapPresetOptions
                {
                    UseNativeAutoDig = false,
                    JumpUseDelayMilliseconds = 0,
                    NextTargetDelayMilliseconds = 0,
                    RetryDelayMilliseconds = 1,
                    ConsumptionConfirmTimeoutMilliseconds = 10,
                    LiveSendEnabled = true,
                    StopContinuousOnTransportFailure = true,
                    Mode = TreasureMapExecutionMode.Continuous
                });

            runner.Run(cancellation.Token);

            Assert(sender.Calls == 3,
                "a socket send failure does not trigger another packet after the ambiguous write");
            Assert(runner.CurrentState == TreasureMapState.Ambiguous,
                "a socket send failure ends the continuous run as ambiguous");
            Assert(runner.LastError == "socket_send_failed",
                "the runner exposes the transport failure code");
            Assert(logs.Any(entry =>
                    entry.Code == "stopped_after_transport_failure:socket_send_failed" &&
                    !entry.Success),
                "the runner records an explicit transport stop reason");
            Assert(!logs.Any(entry => entry.Code == "target_failed_skipped"),
                "a transport failure is not silently skipped as an ordinary target failure");
        }

        private static void TestRunnerExclusiveControlRejectsConcurrentRun()
        {
            CancellationTokenSource firstCancellation = new CancellationTokenSource();
            RegressionStream firstStream = new RegressionStream
            {
                BlockReads = true,
                ReadEntered = new ManualResetEventSlim(false),
                ReleaseRead = new ManualResetEventSlim(false),
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-exclusive-session",
                    "exclusive-snapshot",
                    new TreasureC6InventoryItem[0],
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.Continuous,
                ControlMode = TreasureControlMode.Exclusive
            };
            TreasureMapPresetRunner firstRunner = new TreasureMapPresetRunner(
                firstStream,
                new RegressionSender { FailuresRemaining = 0, Stream = firstStream },
                () => route,
                entry => { },
                options);
            Task firstTask = Task.Run(() => firstRunner.Run(firstCancellation.Token));
            Assert(firstStream.ReadEntered.Wait(3000),
                "first exclusive runner reaches its C6 wait");

            RegressionStream secondStream = new RegressionStream
            {
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-exclusive-second-session",
                    "exclusive-second-snapshot",
                    new TreasureC6InventoryItem[0],
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender secondSender = new RegressionSender
            {
                FailuresRemaining = 0,
                Stream = secondStream
            };
            TreasureMapPresetRunner secondRunner = new TreasureMapPresetRunner(
                secondStream,
                secondSender,
                () => route,
                entry => { },
                options);
            secondRunner.Run();

            Assert(secondRunner.CurrentState == TreasureMapState.ControllerConflict,
                "a concurrent exclusive runner is rejected");
            Assert(secondSender.Calls == 0,
                "controller conflict occurs before any packet send");

            firstCancellation.Cancel();
            firstStream.ReleaseRead.Set();
            Assert(firstTask.Wait(3000),
                "first exclusive runner releases after cancellation");
            Assert(firstRunner.CurrentState == TreasureMapState.Stopping,
                "first exclusive runner stops cleanly after cancellation");
        }

        private static void TestRunnerUsesOriginalSlotWhenSnapshotChangesAfterJump()
        {
            RegressionStream stream = new RegressionStream
            {
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-switch-session",
                    "switch-before-jump",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender { FailuresRemaining = 0 };
            sender.OnSend = call =>
            {
                if (call == 1)
                {
                    stream.Snapshot = new TreasureC6InventorySnapshot(
                        "c6-switch-session",
                        "switch-after-jump",
                        new[] { new TreasureC6InventoryItem("member-a", 13, 1000, 72, 25) },
                        "31531|7373672|/system/bin/app_process64",
                        "bagmgr:fixture/m_ItemDict:fixture");
                }
            };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 1,
                RetryDelayMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.CurrentSnapshot
            };
            List<TreasureMapLogEntry> logs = new List<TreasureMapLogEntry>();

            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => route,
                logs.Add,
                options);
            runner.Run();

            Assert(sender.Calls == 2,
                "runner sends AutoDig for the dispatched Jump even if the background snapshot changes");
            Assert(!logs.Any(entry => entry.Code == "target_changed_before_use"),
                "runner no longer performs a before-AutoDig inventory recheck");
            Assert(runner.CurrentState == TreasureMapState.Completed,
                "snapshot changes after Jump do not turn a dispatched target into a skipped failure");
        }

        private static void TestRunnerDoesNotWaitForC6EventBeforeUse()
        {
            RegressionStream stream = new RegressionStream
            {
                RemoveFirstTargetOnFirstRead = true,
                Snapshot = new TreasureC6InventorySnapshot(
                    "c6-current-snapshot-session",
                    "current-snapshot",
                    new[] { new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65) },
                    "31531|7373672|/system/bin/app_process64",
                    "bagmgr:fixture/m_ItemDict:fixture")
            };
            RegressionSender sender = new RegressionSender { FailuresRemaining = 0 };
            TreasurePacketRoute route = new TreasurePacketRoute(
                Socket_Cache.SocketPacket.PacketType.WS2_Send,
                "127.0.0.1:50000",
                "127.0.0.1:12345");
            TreasureMapPresetOptions options = new TreasureMapPresetOptions
            {
                JumpUseDelayMilliseconds = 0,
                RetryDelayMilliseconds = 1,
                LiveSendEnabled = true,
                Mode = TreasureMapExecutionMode.CurrentSnapshot
            };

            TreasureMapPresetRunner runner = new TreasureMapPresetRunner(
                stream,
                sender,
                () => route,
                entry => { },
                options);
            runner.Run();

            Assert(sender.Packets.Count(packet => packet.PacketBuffer.Length == 24) == 1,
                "runner sends AutoDig without waiting for a newer C6 event");
            Assert(stream.Reads == 0,
                "runner does not read a C6 event after AutoDig");
            Assert(runner.CurrentState == TreasureMapState.Completed,
                "runner completes from the AutoDig dispatch result");
        }

        private static void TestRobotInstructionContract()
        {
            DataTable instructions = Socket_Cache.Robot.InitInstructions();
            DataRow first = instructions.NewRow();
            first["Type"] = Socket_Cache.Robot.InstructionType.TreasureMap;
            first["Content"] = TreasurePacketRuntime.PurePacketPreset.Id;
            instructions.Rows.Add(first);
            Assert(Socket_Cache.Robot.CheckRobotInstruction(instructions, true) == -1,
                "TreasureMap robot instruction validates");
            DataRow second = instructions.NewRow();
            second["Type"] = Socket_Cache.Robot.InstructionType.TreasureMap;
            second["Content"] = TreasurePacketRuntime.PurePacketPreset.Id;
            instructions.Rows.Add(second);
            Assert(Socket_Cache.Robot.CheckRobotInstruction(instructions, true) == 1,
                "robot allows only one TreasureMap instruction");
            Assert(Socket_Cache.Robot.GetName_ByInstructionType(
                Socket_Cache.Robot.InstructionType.TreasureMap) == "藏宝图流程",
                "TreasureMap instruction display name");
        }

        private static JObject ReadyIdentity()
        {
            return new JObject
            {
                ["pid"] = 31531,
                ["startTicks"] = 7373672000L,
                ["exe"] = "/system/bin/app_process64"
            };
        }

        private static JObject Item(string member, int packageNum, int scene, int x, int y)
        {
            return new JObject
            {
                ["memberIdentity"] = member,
                ["packageNum"] = packageNum,
                ["scene"] = scene,
                ["x"] = x,
                ["y"] = y
            };
        }

        private static string Common(
            string messageType,
            int sequence,
            string session,
            string state,
            Action<JObject> customize)
        {
            JObject message = new JObject
            {
                ["protocolName"] = "piaomiao.treasure.stream",
                ["protocolVersion"] = 1,
                ["messageType"] = messageType,
                ["schemaVersion"] = 2,
                ["cacheKind"] = "treasure_inventory",
                ["streamSessionId"] = session,
                ["sequence"] = sequence,
                ["state"] = state,
                ["validationScope"] = "current_inventory_membership",
                ["actionAuthorized"] = false,
                ["processIdentity"] = new JObject
                {
                    ["pid"] = 0,
                    ["startTicks"] = 0,
                    ["exe"] = "fixture"
                },
                ["containerIdentity"] = JValue.CreateNull(),
                ["monotonicNs"] = Math.Max(0, sequence)
            };
            customize(message);
            return message.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("ASSERT FAILED: " + message);
            }
        }

        private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
        {
            uint unsignedValue = unchecked((uint)value);
            buffer[offset] = (byte)(unsignedValue >> 24);
            buffer[offset + 1] = (byte)(unsignedValue >> 16);
            buffer[offset + 2] = (byte)(unsignedValue >> 8);
            buffer[offset + 3] = (byte)unsignedValue;
        }

        private static void AssertCode(Action action, string expectedCode, string message)
        {
            try
            {
                action();
            }
            catch (TreasureC6ProtocolException ex)
            {
                Assert(string.Equals(ex.Code, expectedCode, StringComparison.Ordinal),
                    message + " (expected=" + expectedCode + ", actual=" + ex.Code + ")");
                return;
            }

            throw new InvalidOperationException("ASSERT FAILED: " + message + " (no exception)");
        }

        private sealed class RegressionStream : ITreasureC6InventoryStream
        {
            public bool Connected;
            public int Reads;
            public int Connects;
            public volatile TreasureC6InventorySnapshot Snapshot;
            public TreasureC6InventorySnapshot SnapshotAfterStale;
            public CancellationTokenSource StopSource;
            public bool RemoveFirstTargetOnFirstRead;
            public bool ReAddTargetAfterRemovalOnSecondRead;
            public bool ThrowStaleSnapshotOnFirstRead;
            public bool CancelAfterRead;
            public int ConnectFailuresRemaining;
            public bool BlockReads;
            public ManualResetEventSlim ReadEntered;
            public ManualResetEventSlim ReleaseRead;
            private bool staleSnapshotThrown;

            public bool IsConnected
            {
                get { return this.Connected; }
            }

            public TreasureC6InventorySnapshot CurrentSnapshot
            {
                get { return this.Snapshot; }
            }

            public void Connect(CancellationToken cancellationToken)
            {
                this.Connects++;
                if (this.ConnectFailuresRemaining > 0)
                {
                    this.ConnectFailuresRemaining--;
                    this.Connected = false;
                    throw new TreasureC6TransportException(
                        "connect_failed",
                        "synthetic connection failure");
                }
                this.Connected = true;
            }

            public TreasureC6StreamMessage ReadNext(CancellationToken cancellationToken)
            {
                this.Reads++;
                if (this.BlockReads)
                {
                    this.ReadEntered?.Set();
                    while (!this.ReleaseRead.IsSet)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        this.ReleaseRead.Wait(25);
                    }
                }
                bool hadSnapshot = this.Snapshot != null;
                if (this.ThrowStaleSnapshotOnFirstRead && !this.staleSnapshotThrown)
                {
                    this.staleSnapshotThrown = true;
                    this.Connected = false;
                    throw new TreasureC6ProtocolException(
                        "stale_snapshot_rejected",
                        "synthetic stale snapshot");
                }

                if (this.Snapshot == null && this.SnapshotAfterStale != null)
                {
                    this.Snapshot = this.SnapshotAfterStale;
                }

                if (this.RemoveFirstTargetOnFirstRead && this.Reads == 1)
                {
                    this.Snapshot = SnapshotWithout(13);
                }
                else if (this.ReAddTargetAfterRemovalOnSecondRead && this.Reads == 2)
                {
                    this.Snapshot = SnapshotWith(
                        13,
                        new TreasureC6InventoryItem("member-a", 13, 1009, 19, 65));
                }
                else if (hadSnapshot && this.Reads == 2)
                {
                    this.Snapshot = SnapshotWithout(13);
                }
                else if (hadSnapshot && this.Reads == 4)
                {
                    this.Snapshot = SnapshotWithout(14);
                }
                if (this.CancelAfterRead && this.Reads >= 3)
                {
                    this.StopSource?.Cancel();
                }
                return null;
            }

            private TreasureC6InventorySnapshot SnapshotWithout(int packageNum)
            {
                return new TreasureC6InventorySnapshot(
                    this.Snapshot.StreamSessionId,
                    this.Snapshot.SnapshotId + "-" + this.Reads,
                    this.Snapshot.Items.Where(item => item.PackageNum != packageNum),
                    this.Snapshot.ProcessIdentity,
                    this.Snapshot.ContainerIdentity);
            }

            private TreasureC6InventorySnapshot SnapshotWith(
                int packageNum,
                TreasureC6InventoryItem item)
            {
                return new TreasureC6InventorySnapshot(
                    this.Snapshot.StreamSessionId,
                    this.Snapshot.SnapshotId + "-" + this.Reads,
                    this.Snapshot.Items
                        .Where(existing => existing.PackageNum != packageNum)
                        .Concat(new[] { item }),
                    this.Snapshot.ProcessIdentity,
                    this.Snapshot.ContainerIdentity);
            }

            public void ConsumePackage(int packageNum)
            {
                if (this.Snapshot != null)
                {
                    this.Snapshot = this.SnapshotWithout(packageNum);
                }
            }

            public void Disconnect()
            {
                this.Connected = false;
            }

            public void Dispose()
            {
                this.Connected = false;
            }
        }

        private sealed class RegressionSender : ITreasureMapPacketSender
        {
            public int Calls;
            public int FailuresRemaining = 1;
            public int FailureOnCall;
            public string FailureCode = "synthetic_failure";
            public TreasureMapPacketSendDisposition FailureDisposition =
                TreasureMapPacketSendDisposition.NotDispatched;
            public List<Socket_PacketInfo> Packets = new List<Socket_PacketInfo>();
            public Action<int> OnSend;
            public RegressionStream Stream;
            public int UsesBeforeConsumption = 1;
            public int ConsumptionDelayMilliseconds;
            public Task DelayedConsumptionTask;
            private int successfulConsumptionActions;
            private TreasureC6InventoryItem pendingTarget;

            public TreasureMapPacketSendResult SendOnce(
                Socket_PacketInfo packet,
                bool liveSendEnabled)
            {
                this.Calls++;
                this.Packets.Add(packet);
                this.OnSend?.Invoke(this.Calls);
                bool shouldFailOnCall = this.FailureOnCall > 0 &&
                    this.Calls >= this.FailureOnCall;
                if (this.FailuresRemaining > 0 || shouldFailOnCall)
                {
                    if (this.FailuresRemaining > 0)
                    {
                        this.FailuresRemaining--;
                    }
                    return new TreasureMapPacketSendResult(
                        false,
                        this.FailureCode,
                        0,
                        0,
                        shouldFailOnCall
                            ? this.FailureDisposition
                            : TreasureMapPacketSendDisposition.NotDispatched);
                }

                if (packet.PacketBuffer.Length == TreasureJumpPacketContract.FrameLength &&
                    this.Stream != null)
                {
                    TreasureJumpPacketRequest jump = TreasurePacketEncoder.DecodeJump(
                        packet.PacketBuffer);
                    this.pendingTarget = this.Stream.CurrentSnapshot == null
                        ? null
                        : this.Stream.CurrentSnapshot.Items.FirstOrDefault(item =>
                            item != null &&
                            item.PackageNum > 0 &&
                            item.Scene == jump.MapId &&
                            item.X == jump.X &&
                            item.Y == jump.Y);
                }

                bool isAutoDig = TreasureAutoDigPacketContract.IsFrame(packet.PacketBuffer);
                bool isUse = false;
                if (!isAutoDig)
                {
                    try
                    {
                        TreasurePacketEncoder.DecodeUse(packet.PacketBuffer);
                        isUse = true;
                    }
                    catch
                    {
                        isUse = false;
                    }
                }
                if ((isAutoDig || isUse) && this.Stream != null)
                {
                    this.successfulConsumptionActions++;
                    if (this.successfulConsumptionActions >= this.UsesBeforeConsumption)
                    {
                        TreasureC6InventoryItem currentTarget = this.pendingTarget;
                        this.pendingTarget = null;
                        if (currentTarget != null && this.ConsumptionDelayMilliseconds > 0)
                        {
                            this.DelayedConsumptionTask = Task.Run(async () =>
                            {
                                await Task.Delay(this.ConsumptionDelayMilliseconds).ConfigureAwait(false);
                                this.Stream.ConsumePackage(currentTarget.PackageNum);
                            });
                        }
                        else if (currentTarget != null)
                        {
                            this.Stream.ConsumePackage(currentTarget.PackageNum);
                        }
                    }
                }

                return new TreasureMapPacketSendResult(
                    true,
                    "synthetic_sent",
                    1,
                    packet.PacketBuffer.Length);
            }
        }

    }
}
