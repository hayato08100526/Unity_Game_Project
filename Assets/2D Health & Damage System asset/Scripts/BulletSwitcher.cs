using UnityEngine;

public class BulletSwitcher : MonoBehaviour
{
    [Header("切り替える弾のプレハブを登録してください")]
    public GameObject[] bulletPrefabs;

    // 現在選ばれている弾のプレハブ（他のスクリプトから参照できるようにします）
    public GameObject currentBulletPrefab { get; private set; }

    private int currentBulletIndex = 0;

    void Start()
    {
        // 最初は配列の0番目の弾をセット
        if (bulletPrefabs.Length > 0)
        {
            currentBulletPrefab = bulletPrefabs[currentBulletIndex];
        }
    }

    void Update()
    {
        // マウスの中央ボタン（ホイールクリック）で切り替え
        if (Input.GetMouseButtonDown(2))
        {
            SwitchToNextBullet();
        }
    }

    void SwitchToNextBullet()
    {
        if (bulletPrefabs.Length == 0) return;

        currentBulletIndex++;

        // 登録されている弾の数を超えたら最初に戻る
        if (currentBulletIndex >= bulletPrefabs.Length)
        {
            currentBulletIndex = 0;
        }

        currentBulletPrefab = bulletPrefabs[currentBulletIndex];
        Debug.Log("弾を切り替えました！ 現在の弾: " + currentBulletPrefab.name);
    }
}