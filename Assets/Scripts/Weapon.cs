using UnityEngine;

/// <summary>
/// 汎用の「銃」コンポーネント。
/// Bullet / BoundBullet / TrackingBullet はどれも
/// Instantiate(prefab, position, rotation) するだけで動く設計になっているため、
/// 弾の種類ごとにクラスを分ける必要はない。
/// 同じこのスクリプトを銃オブジェクトごとにアタッチし、
/// Inspectorの Bullet Prefab だけ差し替えれば、それぞれ別の銃として機能する。
/// </summary>
public class Weapon : MonoBehaviour, IWeaponState
{
    [Header("この銃の設定")]
    public Transform firePoint;      // 弾の発射位置
    public GameObject bulletPrefab;  // Bullet / BoundBullet / TrackingBullet のいずれかを設定
    public float fireRate = 0.3f;    // 連射間隔(秒)

    private float nextFireTime = 0f;

    public void Enter()
    {
        // この銃に切り替わった瞬間、すぐ撃てるようにリセット
        nextFireTime = 0f;
    }

    public void Exit()
    {
        // この銃から離れる瞬間の処理(今は特になし)
    }

    public void Tick()
    {
        if (Input.GetButton("Fire1") && Time.time >= nextFireTime)
        {
            Fire();
            nextFireTime = Time.time + fireRate;
        }
    }

    private void Fire()
    {
        if (bulletPrefab != null && firePoint != null)
        {
            Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        }
    }
}