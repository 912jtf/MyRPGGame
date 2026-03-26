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
            DualAuto,            // user clicked "Dual Start": try client first, then auto-host if timeout
            DualHostWaiting,
            DualClientConnecting,
            DualInGame
        }

        StartMode _mode;
        float _dualStartClickedTime;

        [Header("Easeplan HUD")]
        [Tooltip("双人开始：先尝试作为 Client 连接；若超时未连上则自动开 Host 等待对方。")]
        public bool dualAutoHostIfConnectFails = true;
        [Min(0.2f)]
        public float dualConnectTimeoutSeconds = 2.0f;
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
                    _mode = StartMode.SingleHost;
                    manager.StartHost();
                }

                if (GUILayout.Button("双人开始"))
                {
                    _mode = StartMode.DualAuto;
                    _dualStartClickedTime = Time.realtimeSinceStartup;
                    manager.StartClient();
                }
            }
            else
            {
                // Connecting
                GUILayout.Label($"连接中: {manager.networkAddress}  via {Transport.active}");

                // 双人开始：连接超时则自动开 Host
                if (_mode == StartMode.DualAuto && dualAutoHostIfConnectFails)
                {
                    float elapsed = Time.realtimeSinceStartup - _dualStartClickedTime;
                    float remain = Mathf.Max(0f, dualConnectTimeoutSeconds - elapsed);
                    GUILayout.Label($"双人开始：正在尝试加入... ({remain:F1}s)");
                    if (elapsed >= dualConnectTimeoutSeconds && !NetworkClient.isConnected)
                    {
                        manager.StopClient();
                        _mode = StartMode.DualHostWaiting;
                        manager.StartHost();
                    }
                }

                if (GUILayout.Button("取消"))
                {
                    _mode = StartMode.None;
                    manager.StopClient();
                }
            }
        }

        void StatusLabels()
        {
            // Update mode transitions
            if (_mode == StartMode.DualAuto)
            {
                if (NetworkClient.isConnected && !NetworkServer.active)
                    _mode = StartMode.DualClientConnecting;
                else if (NetworkServer.active && NetworkClient.active)
                    _mode = StartMode.DualHostWaiting;
            }

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
                    GUILayout.Label($"双人开始：等待对方加入... ({connected}/2)");
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
                    GUILayout.Label("双人开始：已连接，等待对方准备...");
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
