// PlayerState.cs
// 余計な [cite...] などをすべて消して、これだけにします
public enum PlayerState
{
    Idle,      // 待機
    Move,      // 移動
    Jump,      // ジャンプ中
    Dash,      // ダッシュ（回避）
    WallCling, // 壁張り付き
    Stun       // 行動不能（電撃など）
}