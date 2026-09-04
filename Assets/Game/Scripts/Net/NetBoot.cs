using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
#if SEOYUGI_RELAY
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
#endif

namespace SeoYuGi.Net
{
    /// <summary>
    /// 네트워크 부트스트랩 — NetworkManager를 코드로 생성 (씬 배선 불필요, 프로젝트 철학 준수).
    /// Relay(조인 코드) 우선, 같은 공유기 안에서는 LAN 직접 IP 폴백.
    /// Relay 패키지가 아직 리졸브 안 됐으면 SEOYUGI_RELAY 미정의 → Relay 경로만 비활성.
    /// </summary>
    public static class NetBoot
    {
        public const int MaxPlayers = 6;
        const ushort LanPort = 7777;

        /// <summary>호스트가 클라에게 알려줄 조인 코드 (Relay 성공 시).</summary>
        public static string JoinCode { get; private set; }

        public static bool IsOnline => NetworkManager.Singleton != null &&
                                       (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient);
        public static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        /// <summary>NetworkManager + UnityTransport 코드 생성 — 이미 있으면 재사용.</summary>
        public static NetworkManager Ensure()
        {
            if (NetworkManager.Singleton != null) return NetworkManager.Singleton;

            var go = new GameObject("@NetworkManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var transport = go.AddComponent<UnityTransport>();
            var nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false // 단일 씬 — 씬 핸드셰이크는 접속 실패의 흔한 원인
            };
            return nm;
        }

        /// <summary>Relay 호스트 — 성공 시 JoinCode 채워짐.</summary>
        public static async Task<bool> HostRelayAsync()
        {
#if SEOYUGI_RELAY
            try
            {
                var nm = Ensure();
                await SignInAsync();
                var alloc = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
                JoinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
                nm.GetComponent<UnityTransport>()
                    .SetRelayServerData(new RelayServerData(alloc, "dtls"));
                return nm.StartHost();
            }
            catch (Exception e)
            {
                Debug.LogError($"Relay 호스트 실패: {e.Message}");
                return false;
            }
#else
            Debug.LogError("Relay 패키지 미리졸브 — Unity 에디터에서 패키지 리졸브 후 다시");
            await Task.CompletedTask;
            return false;
#endif
        }

        /// <summary>Relay 참가 — 조인 코드로.</summary>
        public static async Task<bool> JoinRelayAsync(string code)
        {
#if SEOYUGI_RELAY
            try
            {
                var nm = Ensure();
                await SignInAsync();
                var join = await RelayService.Instance.JoinAllocationAsync(code.Trim().ToUpperInvariant());
                nm.GetComponent<UnityTransport>()
                    .SetRelayServerData(new RelayServerData(join, "dtls"));
                return nm.StartClient();
            }
            catch (Exception e)
            {
                Debug.LogError($"Relay 참가 실패: {e.Message}");
                return false;
            }
#else
            Debug.LogError("Relay 패키지 미리졸브 — Unity 에디터에서 패키지 리졸브 후 다시");
            await Task.CompletedTask;
            return false;
#endif
        }

        /// <summary>LAN 폴백 — UGS/Relay 없이 같은 네트워크에서 직접 접속.</summary>
        public static bool HostLan()
        {
            var nm = Ensure();
            nm.GetComponent<UnityTransport>().SetConnectionData("0.0.0.0", LanPort);
            JoinCode = null;
            return nm.StartHost();
        }

        public static bool JoinLan(string ip)
        {
            var nm = Ensure();
            nm.GetComponent<UnityTransport>().SetConnectionData(ip.Trim(), LanPort);
            return nm.StartClient();
        }

        public static void Shutdown()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            JoinCode = null;
        }

#if SEOYUGI_RELAY
        static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
#endif
    }
}
