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
        const string Model = "gpt-4.1-mini"; // 4o-mini와 같은 속도·비용 등급, 복합 명령·문맥 파악이 낫다 (2026-09-06). 안 되면 "gpt-4o-mini"로 되돌릴 것
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
            // 번들 키(StreamingAssets, 캡 걸린 시연용) 우선 — 팀원 PC의 오래된 OPENAI_API_KEY 환경변수가 이기면
            // "무전이 닿지 않습니다"(401)로 죽는다 (2026-09-05 친구 PC). 환경변수는 파일이 없을 때만.
            string source = "none";
            try
            {
                var path = Path.Combine(Application.streamingAssetsPath, "radio_key.txt");
                if (File.Exists(path)) { cachedKey = File.ReadAllText(path).Trim(); source = "StreamingAssets/radio_key.txt"; }
            }
            catch (Exception) { /* 폴백 ① — 키 없음으로 처리 */ }
            if (string.IsNullOrWhiteSpace(cachedKey))
            {
                cachedKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (!string.IsNullOrWhiteSpace(cachedKey)) source = "env OPENAI_API_KEY";
            }
            if (string.IsNullOrWhiteSpace(cachedKey)) cachedKey = null;
            Debug.Log($"LlmRadio 키 출처: {source}" + (cachedKey != null ? $" (…{cachedKey.Substring(Math.Max(0, cachedKey.Length - 4))})" : ""));
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
                        : SquadOrders.NotUnderstood(FailureAck(req)); // 원인이 화면에 보여야 현장에서 고친다
                    if (req.result != UnityWebRequest.Result.Success)
                        Debug.LogWarning($"LlmRadio: {req.result} {req.responseCode} {req.error}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"LlmRadio 파싱 실패: {e.Message}");
                    result = SquadOrders.NotUnderstood("응답이 깨졌습니다. 다시 말해 주십시오.");
                }
                finally { req.Dispose(); }
                onDone(result);
            };
        }

        /// <summary>
        /// 분대 브리핑 — 분대원 한 명이 지휘관에게 상황 한 문장 보고 (무전 타임 시작·무전창 열 때).
        /// "AI 팀원과 소통하는 느낌" — 지시만 받는 게 아니라 먼저 말을 건다. 실패·키 없음이면 조용히 아무 말 없음.
        /// onLine(unitId, 문장) — unitId는 squad 안의 값만.
        /// </summary>
        public static void RequestBriefing(string squadBrief, int zoneCount,
            System.Collections.Generic.IReadOnlyList<int> squad, Action<int, string> onLine, string eventHint = null)
        {
            var key = ResolveKey();
            if (key == null || squad == null || squad.Count == 0) return;

            var zones = zoneCount >= 3 ? "0=A(왼쪽), 1=B(중앙), 2=C(오른쪽)" : "0=A(중앙 단일 거점)";
            string system =
"너는 실시간 전술 게임의 아군 분대원 중 하나다. 아래 전장 상황을 보고 지휘관(플레이어)에게 무전으로 " +
"상황 보고 한 문장을 한다. 위험 또는 기회 딱 하나만, 그리고 가능하면 제안까지 — 예: \"B가 비었습니다. 제가 찍을까요?\", \"HP 1입니다. 뒤로 빠지겠습니다.\", " +
"\"적이 A 점거 중. 둘이 같이 가면 밀 수 있습니다.\" 남은 시간·점거 게이지·현재 명령을 근거로 판단한다.\n\n" +
"전장 상황:\n" + squadBrief + "\n\n" +
(eventHint != null ? "방금 일어난 일: " + eventHint + ". 이 사건에 대해 보고한다 — 지휘관이 이미 화면으로 봤으니 사실 반복이 아니라 분대원 시점의 반응·제안으로.\n\n" : "") +
$"거점 zoneIndex: {zones}.\n\n" +
"응답 형식 (JSON 외 텍스트 금지): {\"unitId\":3,\"line\":\"보고 한 문장\"}\n" +
"unitId는 아군 분대 목록 중 보고하기에 가장 어울리는 분대원(관련 거점에 가까운 쪽, HP 낮으면 본인). " +
"line은 한국어 35자 이내, 군용 무전 말투, 약간의 성격 허용. 지휘관을 '지휘관'이라 부른다.";

            var body = new JObject
            {
                ["model"] = Model,
                ["max_tokens"] = 120,
                ["temperature"] = 0.7,
                ["response_format"] = new JObject { ["type"] = "json_object" },
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = system },
                    new JObject { ["role"] = "user", ["content"] = eventHint != null ? "사건 보고." : "상황 보고." }
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
                try
                {
                    if (req.result != UnityWebRequest.Result.Success) return; // 브리핑은 실패해도 침묵 — 게임엔 영향 없음
                    var root = JObject.Parse(req.downloadHandler.text);
                    string text = (string)(root["choices"] as JArray)?[0]?["message"]?["content"];
                    if (string.IsNullOrEmpty(text)) return;
                    int start = text.IndexOf('{'), end = text.LastIndexOf('}');
                    if (start < 0 || end <= start) return;
                    var payload = JObject.Parse(text.Substring(start, end - start + 1));
                    string line = payload["line"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(line)) return;
                    int unitId = payload["unitId"]?.Value<int>() ?? -1;
                    bool known = false;
                    foreach (var id in squad) if (id == unitId) { known = true; break; }
                    onLine(known ? unitId : squad[0], line.Trim());
                }
                catch (Exception e) { Debug.LogWarning($"LlmRadio 브리핑 파싱 실패: {e.Message}"); }
                finally { req.Dispose(); }
            };
        }

        /// <summary>
        /// 카운트다운 잡담 (2026-09-06) — 라운드 시작 3초 동안 분대원 둘이 한 마디씩 주고받는다. 지시도 보고도 아닌 잡담.
        /// "진짜 살아 있는 것 같게" — 규칙·스코어·지난 라운드에 반응한다. 실패·키 없음이면 onFail (폴백은 러너가 성격 표에서).
        /// 반환값 false = 요청조차 못 냈다(키 없음).
        /// </summary>
        public static bool RequestBanter(string context, System.Collections.Generic.IReadOnlyList<int> squad,
            Action<int, string> onLine, Action onFail)
        {
            var key = ResolveKey();
            if (key == null || squad == null || squad.Count == 0) return false;

            string system =
"너는 실시간 전술 게임의 아군 분대원들이다. 라운드 시작 카운트다운 중, 분대원 두 명이 한 마디씩 주고받는 잡담 두 줄을 쓴다. " +
"지시나 상황 보고가 아니다 — 긴장, 허세, 투정, 지난 라운드 뒷말, 이번 라운드 규칙에 대한 반응 같은 사람 냄새 나는 말. " +
"두 번째 줄은 첫 줄에 대한 대답이어야 한다. 각 줄 한국어 25자 이내, 각자의 성격 설명과 말버릇을 따른다. 서로 다른 분대원 둘.\n\n" +
context + "\n\n" +
"응답 형식 (JSON 외 텍스트 금지): {\"lines\":[{\"unitId\":3,\"line\":\"...\"},{\"unitId\":4,\"line\":\"...\"}]}";

            var body = new JObject
            {
                ["model"] = Model,
                ["max_tokens"] = 160,
                ["temperature"] = 0.9,
                ["response_format"] = new JObject { ["type"] = "json_object" },
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = system },
                    new JObject { ["role"] = "user", ["content"] = "카운트다운 시작." }
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
                int shown = 0;
                try
                {
                    if (req.result != UnityWebRequest.Result.Success) return;
                    var root = JObject.Parse(req.downloadHandler.text);
                    string text = (string)(root["choices"] as JArray)?[0]?["message"]?["content"];
                    if (string.IsNullOrEmpty(text)) return;
                    int start = text.IndexOf('{'), end = text.LastIndexOf('}');
                    if (start < 0 || end <= start) return;
                    var lines = JObject.Parse(text.Substring(start, end - start + 1))["lines"] as JArray;
                    if (lines == null) return;
                    foreach (var item in lines)
                    {
                        if (shown >= 2) break;
                        string line = item["line"]?.Value<string>();
                        int unitId = item["unitId"]?.Value<int>() ?? -1;
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        bool known = false;
                        foreach (var id in squad) if (id == unitId) { known = true; break; }
                        onLine(known ? unitId : squad[shown % squad.Count], line.Trim());
                        shown++;
                    }
                }
                catch (Exception e) { Debug.LogWarning($"LlmRadio 잡담 파싱 실패: {e.Message}"); }
                finally
                {
                    req.Dispose();
                    if (shown == 0) onFail?.Invoke();
                }
            };
            return true;
        }

        /// <summary>HTTP 실패 → 무전 응답 문구. 코드가 보여야 "키 문제인지 회선 문제인지"를 현장에서 가른다.</summary>
        static string FailureAck(UnityWebRequest req)
        {
            switch (req.responseCode)
            {
                case 401: return "무전기 인증 실패(401). API 키가 거부됐습니다.";
                case 429: return "회선 포화(429). 한도 초과, 잠시 뒤 다시.";
                case 0: return "무전이 닿지 않습니다. 네트워크 연결 실패.";
                default: return $"무전이 닿지 않습니다. 잡음뿐입니다. ({req.responseCode})";
            }
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
"{\"reason\":\"전장 판단 한 줄(내부용)\",\"understood\":true,\"ack\":\"무전 응답 한 문장\",\"acks\":[{\"unitId\":3,\"line\":\"분대원별 응답 한 문장\"}],\"orders\":[{\"unitId\":3,\"goal\":\"Zone\",\"zoneIndex\":1,\"stance\":\"Aggressive\",\"focusEnemyId\":-1,\"persist\":false}]}\n\n" +
"goal: \"Free\"(자율 판단) | \"Zone\"(지정 거점으로 — zoneIndex 필수) | \"Highland\"(가까운 고지대 선점) | " +
"\"Regroup\"(지휘관 곁으로) | \"Fallback\"(뒤로 물러남)\n" +
"stance: \"Normal\" | \"Aggressive\"(적을 찾아가 적극 교전) | \"Evasive\"(먼저 쏘지 않고 임무 우선)\n" +
"focusEnemyId: 우선 노릴 적 unitId — \"~를 노려/집중/마크\" 류에 사용. 지정 없으면 -1.\n" +
"persist: 명령 지속 범위 — false(기본)면 이번 라운드만, \"매치 내내/게임 내내/계속\" 류면 true(라운드가 바뀌어도 유지).\n\n" +
"규칙:\n" +
"- 지휘관이 언급한 분대원에게만 명령한다. 전원을 향한 말이면 전원에게.\n" +
"- \"나머지\", \"다른 애들\", \"남은 애들\", \"너희 둘\"은 그 문장에서 이름이 불리지 않은 아군 분대원 전부를 뜻한다. 이름이 불린 분대원에게는 절대 그 명령을 주지 않는다.\n" +
"- \"나\", \"날\", \"나를\", \"내 옆\", \"내 뒤\"는 지휘관 본인이다. \"날 따라와\", \"나한테 붙어\", \"내 옆으로\"는 goal \"Regroup\".\n" +
"- \"X 잡아\", \"X 잡고\", \"X 쳐\", \"X 마크\"는 그 적을 focusEnemyId로, stance \"Aggressive\".\n" +
"- 한 문장에 분대원마다 다른 명령이 있으면 (\"너굴은 까돌봇 잡고, 나머지는 날 따라와\") 쉼표·\"~고\"로 문장을 갈라 분대원별로 따로 orders에 넣는다. " +
"acks에는 명령을 받은 분대원마다 한 줄씩, 각자 자기 명령만 자기 말투로 되풀이한다 — 남의 명령을 대신 말하지 않는다 (너굴: \"어, 어... 까돌봇, 내가 잡는다.\" / 깜냥: \"지휘관 옆으로.\"). " +
"ack는 acks[0]과 같아도 된다.\n" +
"- 호칭 대응: 지휘관은 유닛을 별명(\"라니\"), 동물 이름(\"고라니\"), 기계 이름(\"돌격\"), 클래스명(\"Balance\"), 역할(\"브루저\", \"저격수\"), " +
"\"상대 지휘관\"·\"상대 플레이어\"·\"저 사람\" 등 무엇으로든 부른다 — 위 목록의 '= 다른 호칭들'과 대조해 unitId를 찾는다. 적 이름도 같은 방식.\n" +
"- \"피 없는 애\", \"가까운 애\", \"우리 거점\" 같은 표현은 위 전장 상황(HP·좌표·소유)으로 해석해 대상을 고른다.\n" +
"- \"저격수부터 노려\"처럼 적을 지목하면 해당 분대원(들)의 focusEnemyId에 그 적 unitId를 넣는다. " +
"goal은 언급 없으면 \"Free\", stance는 \"Aggressive\"가 자연스럽다.\n" +
"- 게임에 없는 세부 행동(특정 스킬, 좌표 등)은 가장 가까운 goal/stance 조합으로 해석한다.\n" +
"- reason은 명령을 정하기 전에 쓴다: 남은 시간, 거점 소유·점거 상황, 분대원 HP·위치·현재 명령을 보고 지휘관 의도를 한 줄로 판단한다. " +
"모호한 지시는 전장 상황으로 가장 그럴듯한 쪽을 고른다 — \"거점 가\"면 비었거나 뺏기는 거점, \"막아\"면 적이 점거 중인 아군 거점. question은 정말 갈릴 때만.\n" +
"- 지휘관 지시가 상황과 어긋나면(이미 아군 거점으로 가라, 봉쇄된 거점으로 가라 등) obey하되 ack에 그 사실을 한 마디 덧붙인다.\n" +
"- ack와 acks에는 상황 근거를 한 마디 곁들인다 — \"B 비었으니 내가 먼저 찍는다\", \"까돌봇 저격수라 제가 붙습니다\". 그냥 복창은 금지.\n" +
"- 해석할 수 없거나 게임과 무관한 말이면 {\"understood\":false,\"ack\":\"짧은 되물음\"}.\n" +
"- 분대원에겐 사람 같은 자율성이 있다. 응답에 \"compliance\" 필드를 넣는다: \"obey\"(기본) | " +
"\"question\"(모호해서 되묻는다 — orders 비움) | \"refuse\"(명백한 자살행위만 — HP 1로 돌격, 혼자서 적 셋이 든 거점 진입 등. " +
"orders 비우고 ack에 거부 이유 + 대안 한 문장). 거부는 드물어야 한다 — 열에 아홉은 복종.\n" +
"- ack는 응답하는 분대원(orders[0], 없으면 가장 관련된 분대원)의 '성격' 설명대로 말한다 — 말버릇 포함, 한국어 한 문장 45자 이내. " +
"ack에는 자기가 이해한 명령 내용을 자기 말투로 되풀이해 확인한다 — \"알겠습니다\"만 하지 말 것. " +
"예: 너구리 \"어, 어... A 거점으로 가라는 거지? 간다.\", 비둘기 \"앙? B로 옮기라는 말이에요? 하... 갑니다. 구구.\", 검은냥 \"B로. 이동.\". " +
"question이면 이해한 부분까지는 말하고 모호한 부분만 되묻는다 — \"A거점으로 이... 이동하라는 거지? 누가?\". " +
"compliance도 성격을 따른다: 고라니는 자주 refuse하며 돌격을 선언, 비둘기는 되묻고 툴툴대지만 obey, 검은냥은 한두 단어로 obey, " +
"까치는 명백히 틀린 명령에만 question, 너구리는 더듬으며 obey.";
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
            // 반문·불복종 (2026-09-05) — orders를 적용하지 않고 ack만 남긴다. 기존 명령은 그대로.
            string compliance = (payload["compliance"]?.Value<string>() ?? "obey").ToLowerInvariant();
            if (compliance == "refuse" || compliance == "question") { s.understood = false; s.refused = compliance == "refuse"; return s; }
            if (!s.understood) return s;

            // 유닛별 응답 (2026-09-06) — 복합 명령이면 각자 자기 몫만 말한다. 없는 유닛은 버린다.
            foreach (var ja in payload["acks"] as JArray ?? new JArray())
            {
                int aid = ja["unitId"]?.Value<int>() ?? -1;
                string line = ja["line"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(line)) continue;
                bool ok = false;
                foreach (var id in squad) if (id == aid) { ok = true; break; }
                if (ok && s.acks.Count < 3) s.acks.Add((aid, line.Trim()));
            }

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
