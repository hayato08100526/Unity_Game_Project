using UnityEngine;
using UnityEngine.UI;

// image_4.png のような武器切り替えシステムを管理するスクリプト
public class WeaponSystem : MonoBehaviour
{
    [Header("武器（弾丸）設定")]
    // 4つの異なる弾丸プレハブをここに登録します
    public GameObject[] bulletPrefabs;
    // 弾丸が生成される位置
    public Transform firePoint;

    [Header("UI設定")]
    // 4つの武器UIスロット（image_4.pngのパネル全体）
    public GameObject[] weaponSlots;
    // 各スロットが選択されたときに強調表示するための外枠（Image）
    public Image[] slotHighlightBorders;

    [Header("設定値")]
    // 初期選択武器のインデックス (0 = Bullet, 1 = Charge, 2 = Bound, 3 = Track)
    public int startWeaponIndex = 0;

    // 現在選択中の武器のインデックス
    private int currentWeaponIndex = 0;

    void Start()
    {
        // 初期武器をセットアップ
        currentWeaponIndex = startWeaponIndex;
        UpdateWeaponUI();
    }

    void Update()
    {
        // マウス中央クリック（ホイールクリック）入力を検知
        // 0 = 左クリック, 1 = 右クリック, 2 = 中央クリック
        if (Input.GetMouseButtonDown(2))
        {
            SwitchToNextWeapon();
        }

        // 発射処理（例：左クリック）
        if (Input.GetMouseButtonDown(0))
        {
            ShootCurrentWeapon();
        }
    }

    // 次の武器へ切り替える関数
    void SwitchToNextWeapon()
    {
        // インデックスを増やし、配列のサイズを超えたら0に戻す（ループさせる）
        currentWeaponIndex = (currentWeaponIndex + 1) % bulletPrefabs.Length;
        UpdateWeaponUI();
    }

    // UIの強調表示を更新する関数
    void UpdateWeaponUI()
    {
        // 全てのスロットのハイライトを一旦オフに
        for (int i = 0; i < slotHighlightBorders.Length; i++)
        {
            slotHighlightBorders[i].gameObject.SetActive(false);
        }

        // 現在選択中の武器スロットのハイライトだけをオンに
        slotHighlightBorders[currentWeaponIndex].gameObject.SetActive(true);
    }

    // 現在選択中の武器を発射する関数
    void ShootCurrentWeapon()
    {
        if (bulletPrefabs[currentWeaponIndex] != null)
        {
            // インスペクターで登録された弾丸プレハブを生成
            Instantiate(bulletPrefabs[currentWeaponIndex], firePoint.position, firePoint.rotation);
        }
    }
}