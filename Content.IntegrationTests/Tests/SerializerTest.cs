using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Joveler.Compression.XZ;
using NUnit.Framework;
using Robust.Client.Console;
using Robust.Server.Interfaces.GameObjects;
using Robust.Server.Interfaces.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Is = NUnit.DeepObjectCompare.Is;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Content.IntegrationTests.Tests
{

    public class SerializerTest : ContentIntegrationTest
    {

        [Test]
        public async Task EntityStatesTest()
        {
            BwoinkSerializer.TraceWriter = Console.Out;
            var client = StartClient();
            var server = StartServer();

            await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());

            // Connect.

            client.SetConnectTarget(server);

            client.Post(() => IoCManager.Resolve<IClientNetManager>().ClientConnect(null, 0, null));

            // Run some ticks for the handshake to complete and such.

            server.RunTicks(1);
            await server.WaitIdleAsync();
            client.RunTicks(1);
            await client.WaitIdleAsync();

            await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());

            // Basic checks to ensure that they're connected and data got replicated.

            var mtx = new ManualResetEventSlim();
            GameState gs = null;

            server.Post(() =>
            {
                var sem = IoCManager.Resolve<IServerEntityManager>();
                var spm = IoCManager.Resolve<IPlayerManager>();
                var smm = IoCManager.Resolve<IMapManager>();
                var fromTick = new GameTick(0);
                var toTick = new GameTick(1);
                var es = sem.GetEntityStates(fromTick);
                var ps = spm.GetPlayerStates(fromTick);
                var ds = sem.GetDeletedEntities(fromTick);
                var md = smm.GetStateData(fromTick);
                gs = new GameState(fromTick, toTick, es, ps, ds, md);
                mtx.Set();
            });
            mtx.Wait();
            mtx.Reset();

            Assert.NotNull(gs);

            var serializer = new BwoinkSerializer();

            GameState roundTrip;

            NetChannel.StaticInitializer();
            long ticks;
            using var streamMs = new MemoryStream();
            var compOpts = new XZCompressOptions
            {
                BufferSize = 8 * 1024 * 1024,
                Check = LzmaCheck.None,
                ExtremeFlag = false,
                Level = LzmaCompLevel.Level1,
                LeaveOpen = true
            };
            var streamEncStream = new XZStream(streamMs, compOpts);

            await using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, gs);
                ms.Position = 0;
                roundTrip = (GameState) serializer.Deserialize(ms);
                var src = ms.ToArray();

                var sw = Stopwatch.StartNew();
                await using (var encMs = new MemoryStream())
                {
                    sw.Restart();
                    var encStream = new XZStream(encMs, compOpts);
                    encStream.Write(src);

                    encStream.Flush();
                    encStream.Dispose();
                    ticks = sw.ElapsedTicks;

                    Console.WriteLine($"Tick 0 XZ Frame Compressed: {src.Length} -> {encMs.Length} @ {(encMs.Length / (double) src.Length):P} in {ticks}");
                }

                sw.Restart();
                var streamStart = streamMs.Position;
                streamEncStream.Write(src);
                streamEncStream.Flush();
                var streamEnd = streamMs.Position;
                var streamChange = streamEnd - streamStart;
                ticks = sw.ElapsedTicks;
                Console.WriteLine($"Tick 0 XZ Stream Compressed: {src.Length} -> {streamChange} @ {(streamChange / (double) src.Length):P} in {ticks}");
            }

            Assert.That(roundTrip, Is.DeepEqualTo(gs));

            for (var i = 1u; i < 120; ++i)
            {
                server.RunTicks(1);
                await server.WaitIdleAsync();
                client.Post(() =>
                {
                    if (i % 30 == 0)
                    {
                        var con = IoCManager.Resolve<IClientConsole>();
                        con.ProcessCommand("ooc \"well hello there\"");
                    }

                    mtx.Set();
                });
                mtx.Wait();
                mtx.Reset();
                client.RunTicks(1);
                await client.WaitIdleAsync();

                server.Post(() =>
                {
                    var sem = IoCManager.Resolve<IServerEntityManager>();
                    var spm = IoCManager.Resolve<IPlayerManager>();
                    var smm = IoCManager.Resolve<IMapManager>();
                    var fromTick = new GameTick(i);
                    var toTick = new GameTick(i + 1);
                    var es = sem.GetEntityStates(fromTick);
                    var ps = spm.GetPlayerStates(fromTick);
                    var ds = sem.GetDeletedEntities(fromTick);
                    var md = smm.GetStateData(fromTick);
                    gs = new GameState(fromTick, toTick, es, ps, ds, md);
                    mtx.Set();
                });
                mtx.Wait();
                mtx.Reset();

                Assert.NotNull(gs);

                await using (var ms = new MemoryStream())
                {
                    serializer.Serialize(ms, gs);
                    ms.Position = 0;
                    roundTrip = (GameState) serializer.Deserialize(ms);
                    var src = ms.ToArray();

                    var sw = Stopwatch.StartNew();
                    await using (var encMs = new MemoryStream())
                    {
                        sw.Restart();
                        var encStream = new XZStream(encMs, compOpts);
                        encStream.Write(src);

                        encStream.Flush();
                        encStream.Dispose();
                        ticks = sw.ElapsedTicks;

                        Console.WriteLine($"Tick {i} XZ Frame Compressed: {src.Length} -> {encMs.Length} @ {(encMs.Length / (double) src.Length):P} in {ticks}");
                    }

                    sw.Restart();
                    var streamStart = streamMs.Position;
                    streamEncStream.Write(src);
                    streamEncStream.Flush();
                    var streamEnd = streamMs.Position;
                    var streamChange = streamEnd - streamStart;
                    ticks = sw.ElapsedTicks;
                    Console.WriteLine($"Tick {i} XZ Stream Compressed: {src.Length} -> {streamChange} @ {(streamChange / (double) src.Length):P} in {ticks}");
                }

                Assert.That(roundTrip, Is.DeepEqualTo(gs));
            }
        }

    }

}
