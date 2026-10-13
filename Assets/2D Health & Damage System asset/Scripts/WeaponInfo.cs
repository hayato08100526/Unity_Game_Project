using UnityEngine;

// 弾のプレハブに付けて、その武器の名前・熱・連射の間隔を決める
public class WeaponInfo : MonoBehaviour
{
    [Tooltip("画面に表示する武器の名前")]
    public string displayName = "NORMAL";

    [Tooltip("1回撃つごとに溜まる熱(100 でオーバーヒート)")]
    public float heatPerShot = 8f;

    [Tooltip("次に撃てるまでの時間(秒)")]
    public float fireInterval = 0.15f;
}