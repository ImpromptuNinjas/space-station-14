using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Joveler.Compression.XZ;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Encoders;
using K4os.Compression.LZ4.Streams;
using Moq;
using NUnit.Framework;
using Robust.Server.Interfaces.GameObjects;
using Robust.Server.Interfaces.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Interfaces.Serialization;
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
            List<EntityState> es = null;

            server.Post(() =>
            {
                var sem = IoCManager.Resolve<IServerEntityManager>();
                es = sem.GetEntityStates(GameTick.Zero);
                mtx.Set();
            });
            mtx.Wait();

            Assert.NotNull(es);

            var serializer = new BwoinkSerializer();

            List<EntityState> roundTrip;

            await using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, es);
                ms.Position = 0;
                roundTrip = (List<EntityState>) serializer.Deserialize(ms);
                var buf = new byte[ms.Length];
                var src = ms.ToArray();

                var sw = Stopwatch.StartNew();
                var encoded = LZ4Codec.Encode(src, buf, LZ4Level.L09_HC);
                var ticks = sw.ElapsedTicks;
                Console.WriteLine($"LZ4 Array Compressed: {src.Length} -> {encoded} @ {(encoded / (double) src.Length):P} in {ticks}");

                ms.Position = 0;
                await using (var encMs = new MemoryStream())
                {
                    sw.Restart();
                    var encStream = LZ4Stream.Encode(encMs, new LZ4EncoderSettings {BlockSize = 4 * 1024 * 1024, ChainBlocks = true, CompressionLevel = LZ4Level.L09_HC, ExtraMemory = 12 * 1024 * 1024}, true);
                    encStream.Write(src);

                    encStream.Flush();
                    encStream.Dispose();
                    ticks = sw.ElapsedTicks;

                    Console.WriteLine($"LZ4 Stream Compressed: {src.Length} -> {encMs.Length} @ {(encMs.Length / (double) src.Length):P} in {ticks} ticks");
                }
                ms.Position = 0;
                await using (var encMs = new MemoryStream())
                {
                    NetChannel.StaticInitializer();

                    sw.Restart();
                    var encStream = new XZStream(encMs, new XZCompressOptions { BufferSize = 4 * 1024 * 1024, Check = LzmaCheck.None, ExtremeFlag = false, Level = LzmaCompLevel.Level1, LeaveOpen = true });
                    encStream.Write(src);

                    encStream.Flush();
                    encStream.Dispose();
                    ticks = sw.ElapsedTicks;

                    Console.WriteLine($"XZ Stream Compressed: {src.Length} -> {encMs.Length} @ {(encMs.Length / (double) src.Length):P} in {ticks}");
                }
            }

            Assert.That(roundTrip, Is.DeepEqualTo(es));
        }

    }

}
