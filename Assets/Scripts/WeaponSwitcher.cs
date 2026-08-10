using UnityEngine;

/// <summary>
/// 武器切り替えのコンテキストクラス(Stateパターン)。
/// 「今どの武器Stateがアクティブか」を管理し、
/// マウスホイールでState遷移(Exit → Enter)を行う。
/// 発射などの具体的な挙動は各WeaponState(WeaponBaseの派生クラス)側の責任。
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class WeaponSlot
    {
        public string weaponName;
        public GameObject weaponObject; // WeaponBase派生コンポーネントを持つ子オブジェクト
    }

    [Header("武器リスト（インスペクターで登録）")]
    public WeaponSlot[] weapons;

    [Header("現在の武器インデックス")]
    public int currentWeaponIndex = 0;

    private IWeaponState currentState;

    void Start()
    {
        SwitchWeapon(currentWeaponIndex);
    }

    void Update()
    {
        float scroll = Input.mouseScrollDelta.y;

        if (scroll > 0f) NextWeapon();
        else if (scroll < 0f) PreviousWeapon();

        // 現在アクティブなStateの毎フレーム処理(発射入力の受付など)
        currentState?.Tick();
    }

    void NextWeapon()
    {
        if (weapons == null || weapons.Length == 0) return;
        SwitchWeapon((currentWeaponIndex + 1) % weapons.Length);
    }

    void PreviousWeapon()
    {
        if (weapons == null || weapons.Length == 0) return;
        SwitchWeapon((currentWeaponIndex - 1 + weapons.Length) % weapons.Length);
    }

    void SwitchWeapon(int index)
    {
        if (weapons == null || weapons.Length == 0) return;

        // 現在のStateを抜ける
        currentState?.Exit();

        // 見た目の切り替え(選ばれた武器だけ表示)
        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i].weaponObject != null)
                weapons[i].weaponObject.SetActive(i == index);
        }

        currentWeaponIndex = index;

        // 新しいStateを取得してEnter
        GameObject newWeaponObj = weapons[index].weaponObject;
        currentState = newWeaponObj != null ? newWeaponObj.GetComponent<IWeaponState>() : null;
        currentState?.Enter();

        if (currentState == null && newWeaponObj != null)
        {
            Debug.LogWarning(newWeaponObj.name + " に IWeaponState を実装したコンポーネント(WeaponBase派生)が付いていません");
        }

        Debug.Log("武器を切り替えました: " + weapons[index].weaponName);
    }
}