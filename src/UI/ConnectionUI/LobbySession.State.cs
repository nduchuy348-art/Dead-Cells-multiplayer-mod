using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using dc.pr;
using dc.ui;
using DeadCellsMultiplayerMod.MultiplayerModUI.Connection;
using DeadCellsMultiplayerMod.MultiplayerModUI.lifeUI;
using ModCore.Modules;
using Serilog;

namespace DeadCellsMultiplayerMod
{
    internal static partial class LobbySession
    {
        internal static readonly object Sync = new();
        internal static ILogger? _log;
        internal static ILogger? Log => _log;
        internal static NetRole _role = NetRole.None;
        internal static bool _inActualRun;
        internal static int? _serverSeed;
        internal static int? _remoteSeed;
        internal static int? _pendingClientRestartSeed;
        internal static string _pendingClientRestartReason = string.Empty;
        internal const int MaxSeed = 999_999;
        public static NetNode? NetRef { get; set; }

        internal static bool _menuHooksAttached;
        internal static bool _addMenuHookRegistered;
        internal static WeakReference<TitleScreen?>? _titleScreenRef;
        internal static string _mpIp = "127.0.0.1";
        internal static int _mpPort = 1234;
        internal static string _mpRoomCodeOrIp = string.Empty;
        internal static NetRole _menuSelection = NetRole.None;
        internal enum ConnectionTransport
        {
            Lan,
            Steam
        }
        internal static ConnectionTransport _menuTransport = ConnectionTransport.Lan;
        internal static ConnectionTransport CurrentTransport => _menuTransport;
        internal static SteamConnect.SteamLobbyVisibility _steamLobbyVisibility =
            SteamConnect.SteamLobbyVisibility.FriendsOnly;
        internal static ulong _steamLobbyId;
        internal static string _steamLobbyCode = string.Empty;
        internal static ulong _steamHostSteamId;
        internal static bool _steamJoinLobbyResolvePending;
        internal static ulong? _pendingOverlayJoinLobbyId;
        internal static bool _steamFriendJoinPageActive;
        internal static bool _steamFriendLobbyRefreshInFlight;
        internal static long _nextSteamFriendLobbyRefreshTicks;
        internal static string _steamFriendLobbySignature = string.Empty;
        internal static List<SteamConnect.FriendLobbyInfo> _steamFriendLobbies = new();
        internal const int SteamFriendLobbyRefreshMs = 2500;
        internal const int ClientConnectMaxAttempts = 5;
        internal static bool _pendingAutoStart;
        internal static bool _autoStartTriggered;
        internal static bool _continueLaunchInProgress;
        internal static DateTime _continueLaunchStartedAt = DateTime.MinValue;
        internal const int ContinueLaunchGuardMs = 6000;
        internal static DateTime _autoStartRetryAt = DateTime.MinValue;
        internal const int DeathRestartCooldownMs = 1000;
        internal static DateTime _deathRestartCooldownUntil = DateTime.MinValue;
        internal const string AutoStartMutexName = "DeadCellsMultiplayerMod.AutoStart";
        internal static bool _mainMenuButtonAdded;
        internal static bool _suppressAutoButton;
        internal static bool _worldExitHandled;
        internal static bool _hostDisconnectCountdownActive;
        internal static WeakReference<dc.pr.Game>? _hostDisconnectCountdownGameRef;
        internal static DateTime _hostDisconnectCountdownUntil = DateTime.MinValue;
        internal static int _lastHostDisconnectCountdown = -1;
        internal const int HostDisconnectCountdownSeconds = 5;
        internal static bool _hostDisconnectSavePending;
        internal static DateTime _hostDisconnectSaveRetryAt = DateTime.MinValue;
        internal static DateTime _hostDisconnectSaveDeadline = DateTime.MinValue;
        internal const int HostDisconnectSaveRetryMs = 500;
        internal const int HostDisconnectSaveMaxSeconds = 10;
        internal static bool _seedArrived;
        internal static string _username = "guest";
        internal static string _remoteUsername = "guest";
        internal static string _playerId = Guid.NewGuid().ToString("N");
        public static string Username => ReadSessionSnapshot().Username;
        public static string RemoteUsername => ReadSessionSnapshot().RemoteUsername;

        internal static bool IsSteamJoinLobbyResolvePending() => ReadSessionSnapshot().SteamJoinLobbyResolvePending;
        internal static bool _localReady;
        internal static List<PlayerInfo> _playersDisplay = new();
        internal static bool _inHostStatusMenu;
        internal static bool _inClientWaitingMenu;
        internal static int _menuRebuildDepth;
        internal static bool _genArrived;
        internal static LevelDescSync? _cachedLevelDescSync;
        internal static readonly object TextInputSync = new();
        internal static WeakReference<TextInput?>? _activeTextInputRef;
        internal static bool _activeTextInputNoSpaces;
        internal const int KeyCtrl = 17;
        internal const int KeyLCtrl = 162;
        internal const int KeyRCtrl = 163;
        internal const int KeyC = 67;
        internal const int KeyV = 86;
        internal const int KeySpace = 32;
        internal const int KeyEsc = 27;
        internal const uint CfUnicodeText = 13;
        internal const uint GmemMoveable = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint uFormat);
        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        // ================= ROOM CODE & NETWORK HELPERS =================

        internal static string IpToRoomCode(string ipStr)
        {
            if (IPAddress.TryParse(ipStr, out var ip))
            {
                var bytes = ip.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    uint val = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | (uint)bytes[3];
                    const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                    var result = string.Empty;
                    while (val > 0)
                    {
                        int rem = (int)(val % 36);
                        result = chars[rem] + result;
                        val /= 36;
                    }
                    return result.Length > 0 ? result : "0";
                }
            }
            return ipStr;
        }

        internal static bool TryParseRoomCodeOrIp(string? input, out string ipStr, out int port)
        {
            port = _mpPort > 0 ? _mpPort : 1234;
            ipStr = "127.0.0.1";
            if (string.IsNullOrWhiteSpace(input))
                return false;

            input = input.Trim();
            if (input.StartsWith("DC-", StringComparison.OrdinalIgnoreCase))
                input = input.Substring(3);

            // Direct IP:Port
            if (input.Contains(':'))
            {
                var parts = input.Split(':');
                if (IPAddress.TryParse(parts[0], out var parsedIp) && int.TryParse(parts[1], out var parsedPort))
                {
                    ipStr = parsedIp.ToString();
                    port = parsedPort;
                    return true;
                }
            }

            // Direct IP
            if (IPAddress.TryParse(input, out var directIp))
            {
                ipStr = directIp.ToString();
                return true;
            }

            // Base36 Room Code
            try
            {
                const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                uint val = 0;
                string upper = input.ToUpperInvariant();
                foreach (char c in upper)
                {
                    int idx = chars.IndexOf(c);
                    if (idx < 0) return false;
                    val = (val * 36) + (uint)idx;
                }

                byte b1 = (byte)((val >> 24) & 0xFF);
                byte b2 = (byte)((val >> 16) & 0xFF);
                byte b3 = (byte)((val >> 8) & 0xFF);
                byte b4 = (byte)(val & 0xFF);
                ipStr = $"{b1}.{b2}.{b3}.{b4}";
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static (string tailscaleIp, string lanIp, string bestIp, string roomCode) GetHostNetworkInfo()
        {
            string tailscaleIp = string.Empty;
            string lanIp = string.Empty;
            try
            {
                foreach (var addr in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (addr.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(addr))
                        continue;
                    var str = addr.ToString();
                    if (str.StartsWith("100."))
                    {
                        tailscaleIp = str;
                    }
                    else if (str.StartsWith("192.168.") || str.StartsWith("10.") || str.StartsWith("172."))
                    {
                        if (string.IsNullOrEmpty(lanIp))
                            lanIp = str;
                    }
                }
            }
            catch { }

            string bestIp = !string.IsNullOrEmpty(tailscaleIp) ? tailscaleIp :
                            (!string.IsNullOrEmpty(lanIp) ? lanIp : "127.0.0.1");

            string roomCode = IpToRoomCode(bestIp);
            return (tailscaleIp, lanIp, bestIp, roomCode);
        }

        internal static string GetLanLobbyCodeForUi()
        {
            var info = GetHostNetworkInfo();
            return info.roomCode;
        }

        internal static void CopyHostInfoToClipboard()
        {
            var info = GetHostNetworkInfo();
            DeadCellsMultiplayerMod.UI.ClipboardHelper.TrySetClipboardText(info.roomCode);
        }
    }
}
