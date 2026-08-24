using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MechaChameleon
{
    [Serializable]
    public sealed class RoomListing
    {
        public string RoomId;
        public bool IsOnline;
        public string HostAddress;
        public int Port;
        public string RoomName;
        public bool IsLocked;
        public bool IsJoinLocked;
        public int PlayerCount;
        public int MaxPlayers;

        [NonSerialized] public float LastSeenAt;

        public RoomListing Copy()
        {
            return new RoomListing
            {
                RoomId = RoomId,
                IsOnline = IsOnline,
                HostAddress = HostAddress,
                Port = Port,
                RoomName = RoomName,
                IsLocked = IsLocked,
                IsJoinLocked = IsJoinLocked,
                PlayerCount = PlayerCount,
                MaxPlayers = MaxPlayers,
                LastSeenAt = LastSeenAt
            };
        }
    }

    public sealed class LocalRoomDiscovery : MonoBehaviour
    {
        const int DiscoveryPort = 47779;
        const string Protocol = "MECHA_CHAMELEON_ROOM_V1";
        const float AdvertiseInterval = 0.75f;
        const float RoomTimeout = 3f;

        [Serializable]
        sealed class Advertisement
        {
            public string Protocol;
            public RoomListing Room;
        }

        readonly List<RoomListing> rooms = new();

        UdpClient listener;
        UdpClient advertiser;
        Timer advertisementTimer;
        readonly object advertisementLock = new();
        byte[] advertisementBytes;
        Func<RoomListing> listingProvider;
        float nextAdvertisementAt;
        bool listening;
        bool loggedSuccessfulAdvertisement;
        int sentAdvertisements;
        int receivedAdvertisements;
        int lastLoggedSentAdvertisements;
        int lastLoggedReceivedAdvertisements;
        float nextDiagnosticAt;

        public event Action RoomsChanged;
        public IReadOnlyList<RoomListing> Rooms => rooms;

        void OnEnable()
        {
            Application.runInBackground = true;
        }

        public void StartListening()
        {
            StopAdvertising();
            if (listening) return;

            try
            {
                listener = new UdpClient();
                listener.ExclusiveAddressUse = false;
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                listener.Client.Blocking = false;
                listening = true;
                LogDiagnostic($"listening port={DiscoveryPort}");
            }
            catch (SocketException ex)
            {
                StopListening();
                Debug.LogWarning($"[LocalRoomDiscovery] Could not listen on UDP {DiscoveryPort}: {ex.Message}");
            }
        }

        public void Refresh()
        {
            rooms.Clear();
            RoomsChanged?.Invoke();
            if (!listening)
                StartListening();
        }

        public void StartAdvertising(Func<RoomListing> provider)
        {
            StopListening();
            StopAdvertising();
            listingProvider = provider;

            try
            {
                advertiser = new UdpClient();
                advertiser.EnableBroadcast = true;
                loggedSuccessfulAdvertisement = false;
                RefreshAdvertisementPayload();
                SendAdvertisement(true);
                advertisementTimer = new Timer(
                    _ => SendAdvertisement(false),
                    null,
                    TimeSpan.FromSeconds(AdvertiseInterval),
                    TimeSpan.FromSeconds(AdvertiseInterval));
                nextAdvertisementAt = Time.unscaledTime + AdvertiseInterval;
                LogDiagnostic($"advertising started interval={AdvertiseInterval:0.00}s");
            }
            catch (SocketException ex)
            {
                StopAdvertising();
                Debug.LogWarning($"[LocalRoomDiscovery] Could not advertise rooms: {ex.Message}");
            }
        }

        public void StopAll()
        {
            StopListening();
            StopAdvertising();
            if (rooms.Count == 0) return;
            rooms.Clear();
            RoomsChanged?.Invoke();
        }

        void Update()
        {
            if (advertiser != null && Time.unscaledTime >= nextAdvertisementAt)
            {
                RefreshAdvertisementPayload();
                nextAdvertisementAt = Time.unscaledTime + AdvertiseInterval;
            }

            if (listener == null)
            {
                LogHeartbeatIfNeeded();
                return;
            }

            var changed = ReceiveAdvertisements();
            changed |= RemoveExpiredRooms();
            if (changed)
                RoomsChanged?.Invoke();

            LogHeartbeatIfNeeded();
        }

        void OnDestroy()
        {
            RoomsChanged = null;
            StopAll();
        }

        void RefreshAdvertisementPayload()
        {
            var room = listingProvider?.Invoke();
            if (room == null) return;

            var payload = JsonUtility.ToJson(new Advertisement
            {
                Protocol = Protocol,
                Room = room
            });
            var bytes = Encoding.UTF8.GetBytes(payload);

            lock (advertisementLock)
                advertisementBytes = bytes;
        }

        void SendAdvertisement(bool logErrors)
        {
            lock (advertisementLock)
            {
                if (advertiser == null || advertisementBytes == null) return;

                try
                {
                    advertiser.Send(advertisementBytes, advertisementBytes.Length,
                        new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
                    Interlocked.Increment(ref sentAdvertisements);
                    if (logErrors && !loggedSuccessfulAdvertisement)
                    {
                        loggedSuccessfulAdvertisement = true;
                        GameDiagnostics.Info("discovery", "advertisement_sent",
                            $"component={GetInstanceID()} destination=loopback port={DiscoveryPort}");
                        Debug.Log($"[LocalRoomDiscovery] Advertising rooms on UDP {DiscoveryPort}.");
                    }
                }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
                {
                    if (logErrors)
                        Debug.LogWarning($"[LocalRoomDiscovery] Loopback advertisement failed: {ex.Message}");
                }

                try
                {
                    advertiser.Send(advertisementBytes, advertisementBytes.Length,
                        new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                }
                catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
                {
                    if (logErrors)
                        Debug.LogWarning($"[LocalRoomDiscovery] LAN advertisement failed: {ex.Message}");
                }
            }
        }

        bool ReceiveAdvertisements()
        {
            var changed = false;
            while (listener != null && listener.Available > 0)
            {
                try
                {
                    var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    var bytes = listener.Receive(ref endpoint);
                    var advertisement = JsonUtility.FromJson<Advertisement>(
                        System.Text.Encoding.UTF8.GetString(bytes));
                    if (advertisement?.Protocol != Protocol || advertisement.Room == null ||
                        string.IsNullOrWhiteSpace(advertisement.Room.RoomId))
                    {
                        continue;
                    }

                    var incoming = advertisement.Room;
                    incoming.HostAddress = endpoint.Address.ToString();
                    incoming.LastSeenAt = Time.unscaledTime;
                    receivedAdvertisements++;

                    var existing = rooms.FindIndex(room => room.RoomId == incoming.RoomId);
                    if (existing >= 0)
                    {
                        if (IPAddress.IsLoopback(endpoint.Address) ||
                            !IPAddress.TryParse(rooms[existing].HostAddress, out var currentAddress) ||
                            !IPAddress.IsLoopback(currentAddress))
                        {
                            rooms[existing] = incoming;
                        }
                        else
                        {
                            rooms[existing].LastSeenAt = incoming.LastSeenAt;
                            rooms[existing].PlayerCount = incoming.PlayerCount;
                        }
                    }
                    else
                    {
                        rooms.Add(incoming);
                        LogDiagnostic(
                            $"room added id={incoming.RoomId} name=\"{incoming.RoomName}\" " +
                            $"host={incoming.HostAddress}:{incoming.Port} rooms={rooms.Count}");
                    }

                    changed = true;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LocalRoomDiscovery] Ignored invalid room advertisement: {ex.Message}");
                }
            }

            return changed;
        }

        bool RemoveExpiredRooms()
        {
            var removed = false;
            for (var i = rooms.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime - rooms[i].LastSeenAt <= RoomTimeout) continue;
                LogDiagnostic(
                    $"room expired id={rooms[i].RoomId} name=\"{rooms[i].RoomName}\" " +
                    $"age={Time.unscaledTime - rooms[i].LastSeenAt:0.00}s");
                rooms.RemoveAt(i);
                removed = true;
            }

            return removed;
        }

        void StopListening()
        {
            if (listener != null)
                LogDiagnostic($"listening stopped rooms={rooms.Count}");
            listening = false;
            listener?.Close();
            listener = null;
        }

        void StopAdvertising()
        {
            if (advertiser != null)
                LogDiagnostic($"advertising stopped sent={Volatile.Read(ref sentAdvertisements)}");
            listingProvider = null;
            advertisementTimer?.Dispose();
            advertisementTimer = null;

            lock (advertisementLock)
            {
                advertiser?.Close();
                advertiser = null;
                advertisementBytes = null;
            }
        }

        void LogHeartbeatIfNeeded()
        {
            if (Time.unscaledTime < nextDiagnosticAt) return;

            var sent = Volatile.Read(ref sentAdvertisements);
            if (sent == lastLoggedSentAdvertisements &&
                receivedAdvertisements == lastLoggedReceivedAdvertisements)
            {
                return;
            }

            nextDiagnosticAt = Time.unscaledTime + 2f;
            lastLoggedSentAdvertisements = sent;
            lastLoggedReceivedAdvertisements = receivedAdvertisements;
            LogDiagnostic(
                $"heartbeat mode={(advertiser != null ? "advertiser" : "listener")} " +
                $"sent={sent} received={receivedAdvertisements} rooms={rooms.Count}");
        }

        void LogDiagnostic(string message)
        {
            GameDiagnostics.Info("discovery", "state",
                $"component={GetInstanceID()} {message}");
        }
    }
}
