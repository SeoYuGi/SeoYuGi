using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using SeoYuGi.Battle;
using UnityEngine;
using UnityEngine.Networking;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 자유 서술 무전 — 지휘관의 자연어를 LLM이 SquadOrders로 해석한다 (기획: AI를 게임 메커니즘 안에).
    /// OpenAI API 사용 — 음성 명령(STT·오디오 입력)까지 한 계정으로 가기 위해 교체 (2026-09-05).
    ///
    /// 폴백 3중: ① 키 없음 → HasKey=false, 무전창이 입력줄 자체를 잠근다.
    /// ② 타임아웃·네트워크·HTTP 오류 → NotUnderstood — 기존 명령을 건드리지 않는다.
    /// ③ 응답 JSON 파싱 실패·이상값 → 동일. 프리셋은 어떤 경우에도 로컬로 동작하므로
    /// 심사장에서 네트워크가 죽어도 지휘관 모드 전체는 성립한다.
    ///
    /// 키는 환경변수(개발) → StreamingAssets/radio_key.txt(시연 빌드 — 빌드에 자동 포함) 순.
    /// </summary>
    public static class LlmRadio
    {
        const string Endpoint = "https://api.openai.com/v1/chat/completions";
        const string Model = "gpt-4o-mini"; // 명령 해석은 단순 분류 — 속도·비용 우선
        const int TimeoutSeconds = 10;

        static string cachedKey;
        static bool keyResolved;

        public static bool HasKey => ResolveKey() != null;

        /// <summary>VoiceRadio(음성 전사)가 같은 키를 쓴다.</summary>
        public static string ApiKey => ResolveKey();

        static string ResolveKey()
        {
            if (keyResolved) return cachedKey;
            keyResolved = true;
            cachedKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrWhiteSpace(cachedKey))
            {
                try
                {
                    var path = Path.Combine(Application.streamingAssetsPath, "radio_key.txt");
                    if (File.Exists(path)) cachedKey = File.ReadAllText(path).Trim();
                }
                catch (Exception) { /* 폴백 ① — 키 없음으로 처리 */ }
            }
            if (string.IsNullOrWhiteSpace(cachedKey)) cachedKey = null;
            return cachedKey;
        }

        /// <summary>
        /// 자연어 무전 발신. onDone은 메인 스레드에서 정확히 1회 불린다 — 실패는 전부 NotUnderstood.
        /// squadBrief = 전장 상황 텍스트(분대·적 편성·거점), squad/enemies = 유효 unitId 목록(응답 검증용).
        /// </summary>
        public static void Request(string playerText, string squadBrief, int zoneCount,
            System.Collections.Generic.IReadOnlyList<int> squad,
            System.Collections.Generic.IReadOnlyList<int> enemies, Action<SquadOrders> onDone)
        {
            var key = ResolveKey();
            if (key == null)
            {
                onDone(SquadOrders.NotUnderstood("무전기가 꺼져 있습니다."));
                return;
            }

            var body = new JObject
            {
                ["model"] = Model,
                ["max_tokens"] = 512,
                ["temperature"] = 0.2,
                ["response_format"] = new JObject { ["type"] = "json_object" }, // JSON 강제
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = BuildSystemPrompt(squadBrief, zoneCount) },
                    new JObject { ["role"] = "user", ["content"] = playerText }
                }
            };

            var req = new UnityWebRequest(Endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString())),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds
            };
            req.SetRequestHeader("content-type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);

            req.SendWebRequest().completed += _ =>
            {
                SquadOrders result;
                try
                {
                    result = req.result == UnityWebRequest.Result.Success
                        ? ParseResponse(req.downloadHandler.text, squad, enemies, zoneCount)
                        : SquadOrders.NotUnderstood("무전이 닿지 않습니다 — 잡음뿐입니다.");
                    if (req.result != UnityWebRequest.Result.Success)
                        Debug.LogWarning($"LlmRadio: {req.result} {req.responseCode} {req.error}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"LlmRadio 파싱 실패: {e.Message}");
                    result = SquadOrders.NotUnderstood("응답이 깨졌습니다 — 다시 말해 주십시오.");
                }
                finally { req.Dispose(); }
                onDone(result);
            };
        }

        static string BuildSystemPrompt(string squadBrief, int zoneCount)
        {
            var zones = zoneCount >= 3 ? "0=A(왼쪽), 1=B(중앙), 2=C(오른쪽)" : "0=A(중앙 단일 거점)";
            return
"너는 실시간 전술 게임의 아군 분대 무전병이다. 지휘관(플레이어)의 자연어 무전을 듣고, " +
"분대원 봇에게 내릴 상시 명령을 JSON 하나로만 응답한다.\n\n" +
"현재 전장 상황 (unitId: 호출명·HP·위치, 거점 소유):\n" + squadBrief + "\n\n" +
$"거점 zoneIndex: {zones} — 총 {zoneCount}개.\n\n" +
"응답 형식 (JSON 외 텍스트 금지):\n" +
"{\"understood\":true,\"ack\":\"무전 응답 한 문장\",\"orders\":[{\"unitId\":3,\"goal\":\"Zone\",\"zoneIndex\":1,\"stance\":\"Aggressive\",\"focusEnemyId\":-1,\"persist\":false}]}\n\n" +
"goal: \"Free\"(자율 판단) | \"Zone\"(지정 거점으로 — zoneIndex 필수) | \"Highland\"(가까운 고지대 선점) | " +
"\"Regroup\"(지휘관 곁으로) | \"Fallback\"(뒤로 물러남)\n" +
"stance: \"Normal\" | \"Aggressive\"(적을 찾아가 적극 교전) | \"Evasive\"(먼저 쏘지 않고 임무 우선)\n" +
"focusEnemyId: 우선 노릴 적 unitId — \"~를 노려/집중/마크\" 류에 사용. 지정 없으면 -1.\n" +
"persist: 명령 지속 범위 — false(기본)면 이번 라운드만, \"매치 내내/게임 내내/계속\" 류면 true(라운드가 바뀌어도 유지).\n\n" +
"규칙:\n" +
"- 지휘관이 언급한 분대원에게만 명령한다. 전원을 향한 말이면 전원에게.\n" +
"- \"피 없는 애\", \"가까운 애\", \"우리 거점\" 같은 표현은 위 전장 상황(HP·좌표·소유)으로 해석해 대상을 고른다.\n" +
"- \"저격수부터 노려\"처럼 적을 지목하면 해당 분대원(들)의 focusEnemyId에 그 적 unitId를 넣는다. " +
"goal은 언급 없으면 \"Free\", stance는 \"Aggressive\"가 자연스럽다.\n" +
"- 게임에 없는 세부 행동(특정 스킬, 좌표 등)은 가장 가까운 goal/stance 조합으로 해석한다.\n" +
"- 해석할 수 없거나 게임과 무관한 말이면 {\"understood\":false,\"ack\":\"짧은 되물음\"}.\n" +
"- ack는 분대원이 무전으로 답하는 한국어 한 문장, 40자 이내. 군용 무전 말투.";
        }

        static SquadOrders ParseResponse(string json, System.Collections.Generic.IReadOnlyList<int> squad,
            System.Collections.Generic.IReadOnlyList<int> enemies, int zoneCount)
        {
            var root = JObject.Parse(json);
            var choice = (root["choices"] as JArray)?[0];
            if ((string)choice?["finish_reason"] == "content_filter")
                return SquadOrders.NotUnderstood("그 무전에는 응답할 수 없습니다.");

            string text = (string)choice?["message"]?["content"];
            if (string.IsNullOrEmpty(text)) throw new FormatException("content 없음");

            // json_object 모드라 보통 그대로 JSON — 그래도 첫 { … 끝 } 만 취해 방어
            int start = text.IndexOf('{'), end = text.LastIndexOf('}');
            if (start < 0 || end <= start) throw new FormatException("JSON 없음");
            var payload = JObject.Parse(text.Substring(start, end - start + 1));

            var s = new SquadOrders
            {
                understood = payload["understood"]?.Value<bool>() ?? false,
                ack = payload["ack"]?.Value<string>() ?? ""
            };
            if (string.IsNullOrEmpty(s.ack)) s.ack = s.understood ? "수신했습니다." : "다시 말해 주십시오.";
            if (!s.understood) return s;

            foreach (var jo in payload["orders"] as JArray ?? new JArray())
            {
                int unitId = jo["unitId"]?.Value<int>() ?? -1;
                bool known = false;
                foreach (var id in squad) if (id == unitId) { known = true; break; }
                if (!known) continue; // 없는 유닛·적 유닛 명령은 버린다

                var o = UnitOrder.Free(unitId);
                if (Enum.TryParse((string)jo["goal"], true, out OrderGoal goal)) o.goal = goal;
                if (Enum.TryParse((string)jo["stance"], true, out OrderStance stance)) o.stance = stance;
                o.zoneIndex = jo["zoneIndex"]?.Value<int>() ?? -1;
                if (o.goal == OrderGoal.Zone &&
                    (o.zoneIndex < 0 || o.zoneIndex >= zoneCount)) o.goal = OrderGoal.Free; // 이상값 → 자율

                int focus = jo["focusEnemyId"]?.Value<int>() ?? -1;
                bool validEnemy = false;
                foreach (var e in enemies) if (e == focus) { validEnemy = true; break; }
                o.focusEnemyId = validEnemy ? focus : -1; // 아군·유령 id 지목은 버린다

                o.persistent = jo["persist"]?.Value<bool>() ?? false; // "매치 내내" — 라운드 백지화를 견딘다

                s.orders.Add(o);
            }
            return s;
        }
    }
}
