using UnityEngine;

// 弾のプレハブに付けると、1回の射撃で複数の弾を扇状にばらまく(ショットガン用)
public class ShotPattern : MonoBehaviour
{
    [Tooltip("1回で撃つ弾の数")]
    [Min(1)] public int pelletCount = 5;

    [Tooltip("いちばん外側の弾どうしの角度(度)")]
    public float spreadAngle = 30f;

    [Tooltip("弾ごとの角度を少しランダムにずらす(度)")]
    public float randomJitter = 3f;
}