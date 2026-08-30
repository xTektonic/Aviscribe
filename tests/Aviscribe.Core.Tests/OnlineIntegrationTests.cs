using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Aviscribe.Core.Online;

namespace Aviscribe.Core.Tests;

public sealed class OnlineIntegrationTests
{
    [Fact]
    public void HintAndCollectionFactsCommuteAndManualTransitionsAreExplicit()
    {
        var hintThenCollection = RunFactReducer.Apply(
            RunFactReducer.Apply(null, RunEventKind.HintObserved),
            RunEventKind.CollectionObserved);
        var collectionThenHint = RunFactReducer.Apply(
            RunFactReducer.Apply(null, RunEventKind.CollectionObserved),
            RunEventKind.HintObserved);
        Assert.Equal(hintThenCollection, collectionThenHint);
        Assert.Equal(new RunFact(true, true), hintThenCollection);

        var pending = RunFactReducer.Apply(hintThenCollection, RunEventKind.SetPending);
        Assert.Equal(new RunFact(true, false), pending);
        var counted = RunFactReducer.Apply(pending, RunEventKind.SetCounted);
        Assert.Equal(ManualClassification.Counted, counted!.Value.ManualClassification);
        var wrong = RunFactReducer.Apply(counted, RunEventKind.SetUncounted);
        Assert.Equal(ManualClassification.Uncounted, wrong!.Value.ManualClassification);
        Assert.Null(RunFactReducer.Apply(wrong, RunEventKind.RemoveMoon));
    }

    [Fact]
    public void RemoteApplicationDoesNotEchoAndProjectionHandlesHintArtAndMultiMoons()
    {
        var repository = MoonRepository.LoadDefault();
        var state = new GameState();
        state.SetKingdom(GameState.InitialKingdom);
        var coordinator = new RunCoordinator(state, repository);
        var outbound = 0;
        coordinator.LocalEventCreated += (_, _) => outbound++;
        var hintArt = repository.Moons.First(moon => moon.IsHintArt);

        Assert.True(coordinator.ApplyRemote(new SharedRunEvent(
            Guid.NewGuid(),
            RunEventKind.HintObserved,
            coordinator.Catalog.ToWire(hintArt))));
        Assert.Equal(0, outbound);
        state.SetKingdom(hintArt.Kingdom);
        Assert.Contains(state.Pending, moon => Same(moon, hintArt));
        state.SetKingdom(hintArt.CollectionLocationKingdom);
        Assert.Contains(state.Pending, moon => Same(moon, hintArt));

        var multi = repository.Moons.First(moon => moon.IsMulti);
        coordinator.SetCounted(multi);
        state.SetKingdom(multi.Kingdom);
        Assert.Contains(state.Collected, moon => Same(moon, multi));
        Assert.True(state.CountedMoonCount >= multi.MoonCountValue);
        Assert.Equal(1, outbound);
    }

    [Fact]
    public void CatalogHashIgnoresTranslationsButTracksGameplayMetadata()
    {
        var original = new Moon
        {
            Kingdom = "Cascade",
            Id = 1,
            CollectionKingdom = "Sand",
            IsStory = true,
            IsMulti = false,
            English = "Name",
            Japanese = "名前"
        };
        var translated = Copy(original);
        translated.English = "Different";
        translated.Japanese = "別";
        Assert.Equal(
            OnlineCatalog.CalculateHash([original]),
            OnlineCatalog.CalculateHash([translated]));

        var gameplayChange = Copy(original);
        gameplayChange.IsMulti = true;
        Assert.NotEqual(
            OnlineCatalog.CalculateHash([original]),
            OnlineCatalog.CalculateHash([gameplayChange]));
    }

    [Fact]
    public void LegacyPersistenceMigratesPendingCountedAndWrongToFacts()
    {
        var repository = MoonRepository.LoadDefault();
        var moons = repository.Moons.Take(3).ToArray();
        var saved = new SavedRunState
        {
            KingdomStates = new Dictionary<string, SavedKingdomState>
            {
                [moons[0].Kingdom] = new SavedKingdomState
                {
                    Pending = [Reference(moons[0])],
                    Collected = [Reference(moons[1])],
                    UncountedCollected = [Reference(moons[2])]
                }
            }
        };
        var facts = new RunStateStore(repository).RestoreFacts(saved);
        Assert.Contains(facts, fact => fact.MoonId == moons[0].Id && fact.Hinted && !fact.Collected);
        Assert.Contains(facts, fact => fact.MoonId == moons[1].Id &&
                                      fact.ManualClassification == ManualClassification.Counted);
        Assert.Contains(facts, fact => fact.MoonId == moons[2].Id &&
                                      fact.ManualClassification == ManualClassification.Uncounted);
    }

    [Fact]
    public void WireEventsSerializeEnumAndMoonIdentityAsIntegers()
    {
        var json = JsonSerializer.Serialize(new WireRunEvent
        {
            EventId = Guid.NewGuid(),
            Kind = RunEventKind.HintObserved,
            KingdomId = 4,
            MoonId = 17
        }, OnlineProtocol.JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(0, document.RootElement.GetProperty("t").GetInt32());
        Assert.Equal(4, document.RootElement.GetProperty("k").GetInt32());
        Assert.Equal(17, document.RootElement.GetProperty("m").GetInt32());
        Assert.DoesNotContain("kingdom", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultiplayerFeedUsesPlayerActionAndResolvedMoonName()
    {
        var repository = MoonRepository.LoadDefault();
        var runs = new RunCoordinator(new GameState(), repository);
        var online = new OnlineRunCoordinator(runs);
        var moon = repository.Moons.First();
        var wire = runs.Catalog.ToWire(moon);

        var collection = online.DescribeFeedItem(new OnlineFeedItem
        {
            Kind = nameof(RunEventKind.CollectionObserved),
            ActorDisplayName = "Runner",
            Moon = new WireMoonKeyDto { KingdomId = wire.KingdomId, MoonId = wire.MoonId }
        });
        var reset = online.DescribeFeedItem(new OnlineFeedItem
        {
            Kind = "runReset",
            ActorDisplayName = "Owner"
        });

        Assert.Equal($"Runner collected {moon.Kingdom} #{moon.Id} — {moon.English}.", collection);
        Assert.Equal("Owner started a new run.", reset);
    }

    [Fact]
    public void MultiplayerPendingOwnershipCountsOnlyMoonsAddedByTheLocalPlayer()
    {
        var localPlayer = Guid.NewGuid();
        var remotePlayer = Guid.NewGuid();
        var localMoon = new WireMoonKey(1, 10);
        var remoteMoon = new WireMoonKey(1, 11);
        var tracker = new LocalPendingMoonTracker();

        tracker.Apply(localMoon, RunEventKind.HintObserved, addedByLocalParticipant: true);
        tracker.Apply(remoteMoon, RunEventKind.HintObserved, addedByLocalParticipant: false);

        Assert.True(tracker.Contains(localMoon));
        Assert.False(tracker.Contains(remoteMoon));

        tracker.Reconcile(
            [
                Fact(localMoon, hinted: true, collected: false),
                Fact(remoteMoon, hinted: true, collected: false)
            ],
            [Feed(1, localMoon, RunEventKind.HintObserved, localPlayer),
             Feed(2, remoteMoon, RunEventKind.HintObserved, remotePlayer)],
            localPlayer,
            generationChanged: false);

        Assert.True(tracker.Contains(localMoon));
        Assert.False(tracker.Contains(remoteMoon));

        tracker.Apply(localMoon, RunEventKind.CollectionObserved, addedByLocalParticipant: false);
        Assert.False(tracker.Contains(localMoon));
    }

    [Fact]
    public void LocalHintObservationIsReportedWhenSharedFactAlreadyExists()
    {
        var repository = MoonRepository.LoadDefault();
        var runs = new RunCoordinator(new GameState(), repository);
        var moon = repository.Moons.First();
        runs.ReplaceFacts(
        [
            new RunFactSnapshot(
                moon.Kingdom,
                moon.Id,
                Hinted: true,
                Collected: false,
                ManualClassification.Automatic)
        ]);
        var observed = new List<SharedRunEvent>();
        var created = new List<SharedRunEvent>();
        runs.LocalEventObserved += (_, item) => observed.Add(item);
        runs.LocalEventCreated += (_, item) => created.Add(item);

        var changed = runs.ObserveHint(moon);

        Assert.False(changed);
        var item = Assert.Single(observed);
        Assert.Equal(RunEventKind.HintObserved, item.Kind);
        Assert.Empty(created);
    }

    [Fact]
    public async Task ApiClientAcceptsFragmentedFramesAndRejectsMismatchedResponses()
    {
        var (port, server) = StartFakeServerAsync(async (stream, request) =>
        {
            var response = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                requestId = request.GetProperty("requestId").GetGuid(),
                ok = true,
                data = new
                {
                    enabled = true,
                    protocolVersions = new[] { 1 },
                    maximumActiveRuns = 16,
                    maximumParticipantsPerRun = 8,
                    idleExpirationMinutes = 30
                }
            }, OnlineProtocol.JsonOptions);
            await WriteFragmentedResponseAsync(stream, response);
        });
        var capabilities = await new OnlineApiClient("127.0.0.1", port)
            .GetCapabilitiesAsync(TestContext.Current.CancellationToken);
        Assert.True(capabilities.Enabled);
        await server;

        var (badPort, badServer) = StartFakeServerAsync(async (stream, _) =>
        {
            var response = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 1,
                requestId = Guid.NewGuid(),
                ok = true,
                data = new { enabled = true }
            }, OnlineProtocol.JsonOptions);
            await WriteFragmentedResponseAsync(stream, response);
        });
        var error = await Assert.ThrowsAsync<OnlineApiException>(() =>
            new OnlineApiClient("127.0.0.1", badPort)
                .GetCapabilitiesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("invalidResponse", error.Code);
        await badServer;
    }

    [Fact]
    public async Task ArmedCapturePublishesAutomaticEventsAcrossThreads()
    {
        var repository = MoonRepository.LoadDefault();
        var runs = new RunCoordinator(new GameState(), repository);
        var resumePath = Path.Combine(
            Path.GetTempPath(),
            $"aviscribe-online-{Guid.NewGuid():N}.json");
        var fakeServer = StartPublishingServer();
        await using var online = new OnlineRunCoordinator(
            runs,
            resumePath: resumePath);
        try
        {
            await online.CreateAsync(
                "127.0.0.1",
                fakeServer.Port,
                "Runner",
                new RunSettings(),
                TestContext.Current.CancellationToken);

            using (File.Open(resumePath, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.True(online.HasPreviousRun);

            await Task.Run(() => online.CaptureSharingArmed = true);
            var moon = repository.Moons.First();
            await Task.Run(() => runs.ObserveCollection(moon));

            var published = await fakeServer.Published.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            var expected = runs.Catalog.ToWire(moon);
            Assert.Equal(RunEventKind.CollectionObserved, published.Kind);
            Assert.Equal(expected.KingdomId, published.KingdomId);
            Assert.Equal(expected.MoonId, published.MoonId);
        }
        finally
        {
            fakeServer.Stop.Cancel();
            fakeServer.Listener.Stop();
            await fakeServer.Server;
            if (File.Exists(resumePath)) File.Delete(resumePath);
        }
    }

    private static (int Port, Task Server) StartFakeServerAsync(
        Func<NetworkStream, JsonElement, Task> responder)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var task = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                await using var stream = client.GetStream();
                var magic = new byte[OnlineProtocol.Magic.Length];
                await ReadExactAsync(stream, magic);
                Assert.Equal(OnlineProtocol.Magic, magic);
                var length = new byte[4];
                await ReadExactAsync(stream, length);
                var payload = new byte[BinaryPrimitives.ReadInt32BigEndian(length)];
                await ReadExactAsync(stream, payload);
                using var request = JsonDocument.Parse(payload);
                await responder(stream, request.RootElement.Clone());
            }
            finally
            {
                listener.Stop();
            }
        });
        return (port, task);
    }

    private static PublishingServer StartPublishingServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var stop = new CancellationTokenSource();
        var published = new TaskCompletionSource<WireRunEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionId = Guid.NewGuid();
        var participantId = Guid.NewGuid();
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stop.Token);
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            try
                            {
                                await using var stream = client.GetStream();
                                var magic = new byte[OnlineProtocol.Magic.Length];
                                await ReadExactAsync(stream, magic);
                                var length = new byte[4];
                                await ReadExactAsync(stream, length);
                                var payload = new byte[BinaryPrimitives.ReadInt32BigEndian(length)];
                                await ReadExactAsync(stream, payload);
                                using var document = JsonDocument.Parse(payload);
                                var request = document.RootElement;
                                var operation = request.GetProperty("operation").GetString();
                                object data;
                                switch (operation)
                                {
                                    case "capabilities":
                                        data = new
                                        {
                                            enabled = true,
                                            protocolVersions = new[] { 1 },
                                            maximumActiveRuns = 1,
                                            maximumParticipantsPerRun = 8,
                                            idleExpirationMinutes = 30
                                        };
                                        break;
                                    case "createRun":
                                        data = new
                                        {
                                            sessionId,
                                            generation = 1,
                                            joinCode = "TEST-ROOM",
                                            participantId,
                                            participantToken = "test-token",
                                            isOwner = true,
                                            snapshot = new
                                            {
                                                sessionId,
                                                generation = 1,
                                                revision = 1,
                                                configuration = new
                                                {
                                                    category = "standard",
                                                    includePostGame = false
                                                },
                                                ownerParticipantId = participantId,
                                                moonFacts = Array.Empty<object>(),
                                                participants = new[]
                                                {
                                                    new
                                                    {
                                                        participantId,
                                                        displayName = "Runner",
                                                        isOnline = true,
                                                        isOwner = true,
                                                        joinedSequence = 1
                                                    }
                                                },
                                                recentEvents = Array.Empty<object>()
                                            }
                                        };
                                        break;
                                    case "publishEvents":
                                    {
                                        var eventJson = request.GetProperty("data")
                                            .GetProperty("events")[0];
                                        var runEvent = eventJson.Deserialize<WireRunEvent>(
                                            OnlineProtocol.JsonOptions)!;
                                        published.TrySetResult(runEvent);
                                        data = new
                                        {
                                            generation = 1,
                                            revision = 2,
                                            events = new[]
                                            {
                                                new { id = runEvent.EventId, r = 2, d = false }
                                            }
                                        };
                                        break;
                                    }
                                    case "waitForChanges":
                                        await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token);
                                        return;
                                    default:
                                        throw new InvalidOperationException(
                                            $"Unexpected operation {operation}.");
                                }

                                var response = JsonSerializer.SerializeToUtf8Bytes(new
                                {
                                    version = 1,
                                    requestId = request.GetProperty("requestId").GetGuid(),
                                    ok = true,
                                    data
                                }, OnlineProtocol.JsonOptions);
                                var responseLength = new byte[4];
                                BinaryPrimitives.WriteInt32BigEndian(
                                    responseLength,
                                    response.Length);
                                await stream.WriteAsync(responseLength, stop.Token);
                                await stream.WriteAsync(response, stop.Token);
                            }
                            catch (Exception ex) when (
                                ex is OperationCanceledException or IOException or SocketException)
                            {
                            }
                        }
                    }, stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException) when (stop.IsCancellationRequested)
            {
            }
        });
        return new PublishingServer(
            ((IPEndPoint)listener.LocalEndpoint).Port,
            listener,
            stop,
            server,
            published);
    }

    private static async Task WriteFragmentedResponseAsync(NetworkStream stream, byte[] payload)
    {
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        foreach (var value in frame)
            await stream.WriteAsync(new byte[] { value }, TestContext.Current.CancellationToken);
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], TestContext.Current.CancellationToken);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private static bool Same(Moon left, Moon right) => left.Id == right.Id &&
        left.Kingdom.Equals(right.Kingdom, StringComparison.OrdinalIgnoreCase);
    private static OnlineMoonFact Fact(WireMoonKey moon, bool hinted, bool collected) => new()
    {
        Moon = new WireMoonKeyDto { KingdomId = moon.KingdomId, MoonId = moon.MoonId },
        Hinted = hinted,
        Collected = collected
    };
    private static OnlineFeedItem Feed(
        long revision,
        WireMoonKey moon,
        RunEventKind kind,
        Guid actorParticipantId) => new()
    {
        Revision = revision,
        Kind = kind.ToString(),
        ActorParticipantId = actorParticipantId,
        Moon = new WireMoonKeyDto { KingdomId = moon.KingdomId, MoonId = moon.MoonId }
    };
    private static SavedMoonReference Reference(Moon moon) => new() { Kingdom = moon.Kingdom, MoonId = moon.Id };
    private static Moon Copy(Moon moon) => new()
    {
        Kingdom = moon.Kingdom,
        Id = moon.Id,
        CollectionKingdom = moon.CollectionKingdom,
        IsStory = moon.IsStory,
        IsMulti = moon.IsMulti,
        English = moon.English,
        Japanese = moon.Japanese
    };

    private sealed record PublishingServer(
        int Port,
        TcpListener Listener,
        CancellationTokenSource Stop,
        Task Server,
        TaskCompletionSource<WireRunEvent> Published);
}
