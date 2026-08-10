/// <summary>
/// 武器1種類 = 1つのStateとして扱うためのインターフェース。
/// WeaponSwitcher がこのインターフェースを通してState遷移(Enter/Exit)と
/// 毎フレームの処理(Tick)を呼び出す。
/// </summary>
public interface IWeaponState
{
    /// <summary>このStateに入った瞬間(=この武器に切り替わった瞬間)に1回呼ばれる</summary>
    void Enter();

    /// <summary>このStateから抜ける瞬間(=別の武器に切り替わる瞬間)に1回呼ばれる</summary>
    void Exit();

    /// <summary>このStateがアクティブな間、毎フレーム呼ばれる(発射入力の処理など)</summary>
    void Tick();
}