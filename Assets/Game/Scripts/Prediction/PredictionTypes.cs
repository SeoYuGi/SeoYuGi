using System;
using System.Collections.Generic;

namespace SeoYuGi.Prediction
{
    public enum TeamId { Human = 0, Machine = 1 }

    public enum ActionType { Move, Attack, Heavy, Guard, Decoy }

    [Serializable]
    public struct Cell : IEquatable<Cell>
    {
        public int X;
        public int Y;

        public Cell(int x, int y) { X = x; Y = y; }

        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => X * 397 ^ Y;
        public override string ToString() => $"({X},{Y})";
    }

    public struct ActionEvent
    {
        public int ActorId;
        public TeamId Team;
        public ActionType Type;
        public Cell From;
        public Cell To;
        public float Time; // 라운드 시작 기준 초
    }

    public struct CellProb
    {
        public Cell Cell;
        public float Prob;

        public CellProb(Cell cell, float prob) { Cell = cell; Prob = prob; }
    }

    public class PredictionConfig
    {
        public int MapWidth = 9;
        public int MapHeight = 9;
        public List<Cell> ZoneCells = new List<Cell>();

        // 예측 점수 가중치: 방향 마르코프 vs 칸 선호 vs 거점 인력
        public float MarkovWeight = 0.55f;
        public float VisitWeight = 0.30f;
        public float ZonePullWeight = 0.15f;

        // 과거 기록 감쇠(라운드가 길어져도 최근 습관이 우세하도록)
        public float DecayPerObservation = 0.995f;

        // R3에서 "거점 근처" 문맥으로 취급하는 체비쇼프 거리
        public int NearZoneRange = 2;

        public float DecoyProbMultiplier = 0.25f;
    }
}
