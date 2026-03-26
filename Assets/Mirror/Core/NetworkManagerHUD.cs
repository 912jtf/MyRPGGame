using UnityEngine;

namespace Mirror
{
    /// <summary>Shows NetworkManager controls in a GUI at runtime.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Network/Network Manager HUD")]
    [RequireComponent(typeof(NetworkManager))]
    [HelpURL("https://mirror-networking.gitbook.io/docs/components/network-manager-hud")]
    public class NetworkManagerHUD : MonoBehaviour
    {
        NetworkManager manager;

        public int offsetX;
        public int offsetY;

        enum StartMode
        {
            None,
            SingleHost,
            DualHostWaiting,      // created room, waiting for 2nd player
            DualClientConnecting, // joining room, waiting for 2nd player
            DualInGame
        }

        StartMode _mode;

        [Header("Easeplan HUD")]
        [Tooltip("双人开始：本地玩家在对方未加入前禁用操作脚本。")]
        public bool lockLocalPlayerControlUntilBothReady = true;

        void Awake()
        {
            manager = GetComponent<NetworkManager>();
        }

#if !UNITY_SERVER || UNITY_EDITOR
        void OnGUI()
        {
            // If this width is changed, also change offsetX in GUIConsole::OnGUI
            int width = 430;

            GUILayout.BeginArea(new Rect(10 + offsetX, 10 + offsetY, width, 9999));

            if (!NetworkClient.isConnected && !NetworkServer.active)
                StartButtons();
            else
                StatusLabels();

            if (NetworkClient.isConnected && !NetworkClient.ready)
            {
                if (GUILayout.Button("Client Ready"))
                {
                    // client ready
                    NetworkClient.Ready();
                    if (NetworkClient.localPlayer == null)
                        NetworkClient.AddPlayer();
                }
            }

            StopButtons();

            GUILayout.EndArea();
        }

        void StartButtons()
        {
            if (!NetworkClient.active)
            {
                GUILayout.Label("<b>启动方式</b>");

                GUILayout.BeginHorizontal();
                GUILayout.Label("地址", GUILayout.Width(40));
                manager.networkAddress = GUILayout.TextField(manager.networkAddress, GUILayout.Width(170));

                if (Transport.active is PortTransport portTransport)
                {
                    GUILayout.Label("端口", GUILayout.Width(40));
                    if (ushort.TryParse(GUILayout.TextField(portTransport.Port.ToString(), GUILayout.Width(70)), out ushort port))
                        portTransport.Port = port;
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                if (GUILayout.Button("单人开始"))
                {
                    PlayerPrefs.SetInt("Easeplan.RequiredPlayers", 1);
                    PlayerPrefs.Save();
                    _mode = StartMode.SingleHost;
                    manager.StartHost();
                }

                if (GUILayout.Button("创建双人房间（Host）"))
                {
                    PlayerPrefs.SetInt("Easeplan.RequiredPlayers", 2);
                    PlayerPrefs.Save();
                    _mode = StartMode.DualHostWaiting;
                    manager.StartHost();
                }

                if (GUILayout.Button("加入双人（Client）"))
                {
                    PlayerPrefs.SetInt("Easeplan.RequiredPlayers", 2);
                    PlayerPrefs.Save();
                    _mode = StartMode.DualClientConnecting;
                    manager.StartClient();
                }
            }
            else
            {
                // Connecting
                GUILayout.Label($"连接中: {manager.networkAddress}  via {Transport.active}");

                if (GUILayout.Button("取消"))
                {
                    _mode = StartMode.None;
                    manager.StopClient();
                }
            }
        }

        void StatusLabels()
        {
            if ((_mode == StartMode.DualHostWaiting || _mode == StartMode.DualClientConnecting) && BothPlayersPresent())
                _mode = StartMode.DualInGame;

            if (lockLocalPlayerControlUntilBothReady && (_mode == StartMode.DualHostWaiting || _mode == StartMode.DualClientConnecting))
                SetLocalPlayerControlEnabled(false);
            if (lockLocalPlayerControlUntilBothReady && _mode == StartMode.DualInGame)
                SetLocalPlayerControlEnabled(true);

            // host mode
            // display separately because this always confused people:
            //   Server: ...
            //   Client: ...
            if (NetworkServer.active && NetworkClient.active)
            {
                // host mode
                GUILayout.Label($"<b>Host</b>（Server + Client）  via {Transport.active}");
                GUILayout.Label($"地址: {manager.networkAddress}");
                if (Transport.active is PortTransport portTransportHost)
                    GUILayout.Label($"端口: {portTransportHost.Port}");

                if (_mode == StartMode.DualHostWaiting)
                {
                    int connected = NetworkServer.connections != null ? NetworkServer.connections.Count : 0;
                    GUILayout.Label($"双人：等待对方加入... ({connected}/2)");
                    if (BothPlayersPresent())
                        GUILayout.Label("对方已准备好，正在开始游戏...");
                }
            }
            else if (NetworkServer.active)
            {
                // server only
                GUILayout.Label($"<b>Server</b>: running via {Transport.active}");
            }
            else if (NetworkClient.isConnected)
            {
                // client only
                GUILayout.Label($"<b>Client</b>: connected to {manager.networkAddress} via {Transport.active}");
                if (_mode == StartMode.DualClientConnecting)
                {
                    GUILayout.Label("双人：已连接，等待对方进入...");
                    if (BothPlayersPresent())
                        GUILayout.Label("对方已准备好，正在开始游戏...");
                }
            }
        }

        bool BothPlayersPresent()
        {
            // Host: rely on connections count (includes local host connection)
            if (NetworkServer.active)
            {
                int connected = NetworkServer.connections != null ? NetworkServer.connections.Count : 0;
                return connected >= 2;
            }

            // Client only: count spawned NetworkIdentity with tag "Player".
            // Avoid referencing project scripts here (Mirror assembly shouldn't depend on Assembly-CSharp).
            int players = 0;
            if (NetworkClient.active && NetworkClient.spawned != null)
            {
                foreach (var kv in NetworkClient.spawned)
                {
                    NetworkIdentity ni = kv.Value;
                    if (ni == null) continue;
                    if (ni.gameObject != null && ni.gameObject.CompareTag("Player"))
                        players++;
                }
            }
            return players >= 2;
        }

        void SetLocalPlayerControlEnabled(bool enabled)
        {
            if (!NetworkClient.active)
                return;
            if (NetworkClient.localPlayer == null)
                return;

            GameObject go = NetworkClient.localPlayer.gameObject;
            if (go == null)
                return;

            // Don't touch colliders/rigidbody here; only gameplay scripts.
            // Use type name matching to avoid hard dependency on project assemblies.
            MonoBehaviour[] mbs = go.GetComponents<MonoBehaviour>();
            if (mbs == null || mbs.Length == 0)
                return;

            for (int i = 0; i < mbs.Length; i++)
            {
                MonoBehaviour mb = mbs[i];
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n == "PlayerMovement" || n == "PlayerAttack" || n == "PlayerSkills" || n == "PlayerGoldCarrier")
                    mb.enabled = enabled;
            }
        }

        void StopButtons()
        {
            if (NetworkServer.active && NetworkClient.isConnected)
            {
                GUILayout.BeginHorizontal();
#if UNITY_WEBGL
                if (GUILayout.Button("Stop Single Player"))
                    manager.StopHost();
#else
                // stop host if host mode
                if (GUILayout.Button("停止"))
                    manager.StopHost();

                // stop client if host mode, leaving server up
                if (GUILayout.Button("断开客户端"))
                    manager.StopClient();
#endif
                GUILayout.EndHorizontal();
            }
            else if (NetworkClient.isConnected)
            {
                // stop client if client-only
                if (GUILayout.Button("断开"))
                    manager.StopClient();
            }
            else if (NetworkServer.active)
            {
                // stop server if server-only
                if (GUILayout.Button("Stop Server"))
                    manager.StopServer();
            }
        }
#endif
    }
}
