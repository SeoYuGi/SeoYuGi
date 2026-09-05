using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 분대원 성격 (2026-09-05) — 무전 말투와 복종 성향. "지휘로 컨트롤은 되지만 살아 있는 느낌"이 목표.
    ///
    /// 너구리 = 멍청·우직·말더듬, 말은 잘 듣는다.      고라니 = 막가파, 말 안 듣고 돌격. "끼에엑".
    /// 검은냥 = 충직·차분, 필요한 말만 ("네." "확인.").  비둘기 = 성격 드러움, 툴툴대며 되묻지만 따른다. "구구".
    /// 까치 = 영리, 잘 듣고 명백히 틀린 것에만 반문.
    ///
    /// 프리셋(LLM 없음)은 여기 표로 말투·복종 주사위를 굴리고, 자유 무전은 PromptBlock을 LLM에 넘겨 같은 성격으로 답하게 한다.
    /// 기계팀은 같은 클래스 동물의 성격을 "모방"한다 — 카운트다운에 스캔 대사, 말끝에 모방 표식.
    /// 순수 C# — 뷰·네트 비의존.
    /// </summary>
    public static class Personas
    {
        public enum Reply { Obey, Grumble, Refuse } // Grumble = 툴툴대지만 따른다, Refuse = 명령 무시하고 자기 식대로

        public struct Persona
        {
            public string trait;        // LLM용 성격 설명 (한 줄)
            public string[] prefixes;   // 말 앞 말버릇
            public string[] suffixes;   // 말 뒤 말버릇
            public float refuseChance;
            public float grumbleChance;
        }

        static readonly Dictionary<UnitClass, Persona> Table = new Dictionary<UnitClass, Persona>
        {
            [UnitClass.Tank] = new Persona
            {
                trait = "멍청하고 우직함. 말을 더듬는다(어, 어... / 그, 그럼요). 명령은 무조건 잘 듣는다. 반문 없음.",
                prefixes = new[] { "어, 어... ", "음, 그... ", "네, 네! " },
                suffixes = new[] { " 그, 그럼요.", " 하, 할게요.", "" },
                refuseChance = 0f, grumbleChance = 0f,
            },
            [UnitClass.Balance] = new Persona
            {
                trait = "막가파. 말을 잘 안 듣고 무조건 돌격하려 든다. 말버릇 \"끼에엑!\". 열에 셋은 명령을 무시하고 돌격을 선언한다(compliance refuse).",
                prefixes = new[] { "끼에엑! ", "끼엑! ", "" },
                suffixes = new[] { " 끼에엑!", " 가자아!", " 끼엑." },
                refuseChance = 0.3f, grumbleChance = 0f,
            },
            [UnitClass.Assassin] = new Persona
            {
                trait = "충직하고 차분함. 필요한 말만 짧게 한다. \"네.\" \"확인.\" \"이동합니다.\" 수준. 반문 거의 없음.",
                prefixes = new[] { "" },
                suffixes = new[] { "" },
                refuseChance = 0f, grumbleChance = 0f,
            },
            [UnitClass.Grenadier] = new Persona
            {
                trait = "성격이 드러움. \"엥?\" \"앙?\" 하며 지휘관에게 자꾸 되묻고 툴툴대지만 결국 따른다(compliance는 obey, ack만 투덜). 말버릇 \"국.구국.\" \"구구.\"",
                prefixes = new[] { "엥? ", "앙? ", "하... " },
                suffixes = new[] { " 구구.", " 국.구국.", " ...구구." },
                refuseChance = 0f, grumbleChance = 0.6f,
            },
            [UnitClass.Sniper] = new Persona
            {
                trait = "영리하고 침착함. 말을 잘 듣고, 명백히 틀린 명령(이미 잃은 거점, 자살행위)에만 짧게 반문한다(question). 군용 무전처럼 정확하게.",
                prefixes = new[] { "", "확인. " },
                suffixes = new[] { ". 이해했습니다.", " 정확히.", "" },
                refuseChance = 0f, grumbleChance = 0f,
            },
        };

        static readonly string[] TerseLines = { "네.", "확인.", "이동합니다.", "알겠습니다." };

        public static Persona For(UnitClass cls) => Table.TryGetValue(cls, out var p) ? p : Table[UnitClass.Balance];

        /// <summary>복종 주사위 — 프리셋 경로용 (LLM은 PromptBlock으로 스스로 판단).</summary>
        public static Reply Roll(UnitClass cls, Random rng)
        {
            var p = For(cls);
            double r = rng.NextDouble();
            if (r < p.refuseChance) return Reply.Refuse;
            if (r < p.refuseChance + p.grumbleChance) return Reply.Grumble;
            return Reply.Obey;
        }

        /// <summary>기본 응답을 성격 말투로. team 1(기계)은 같은 동물을 모방하는 표식을 단다.</summary>
        public static string Speak(UnitClass cls, int team, string ack, Random rng)
        {
            string line;
            if (cls == UnitClass.Assassin) line = TerseLines[rng.Next(TerseLines.Length)];
            else
            {
                var p = For(cls);
                line = p.prefixes[rng.Next(p.prefixes.Length)] + ack + p.suffixes[rng.Next(p.suffixes.Length)];
            }
            return team == 1 ? "[모방] " + line : line;
        }

        /// <summary>툴툴 — 되묻지만 따른다 (비둘기).</summary>
        public static string GrumbleLine(UnitClass cls, int team, string ack, Random rng)
        {
            string[] forms = { $"엥? 꼭 그래야 해요? ...알았어요. {ack} 구구.", $"앙? 지금요? 하... {ack} 국.구국.", $"또요? ...{ack} 구구." };
            string line = forms[rng.Next(forms.Length)];
            return team == 1 ? "[모방] " + line : line;
        }

        /// <summary>불복종 — 명령 무시하고 돌격 선언 (고라니).</summary>
        public static string RefuseLine(UnitClass cls, int team, Random rng)
        {
            string[] forms = { "끼에엑! 그런 거 몰라, 그냥 박는다!", "끼엑! 됐어, 앞에 적 있잖아! 돌격!", "끼에엑! 내 맘대로 할게!" };
            string line = forms[rng.Next(forms.Length)];
            return team == 1 ? "[모방] " + line : line;
        }

        /// <summary>LLM 시스템 프롬프트용 — 분대원 한 명의 성격 설명.</summary>
        public static string PromptBlock(UnitClass cls) => For(cls).trait;

        /// <summary>기계팀 카운트다운 대사 — 상대 동물을 스캔해 모방한다는 컨셉.</summary>
        public static string MimicLine(UnitClass cls) =>
            $"스캔 완료. 상대 {ClassNames.For(0, cls)} 패턴 모방 개시.";
    }
}
