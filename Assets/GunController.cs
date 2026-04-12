using UnityEngine;

public class GunController : MonoBehaviour
{
    public GameObject bulletPrefab; // 弾のプレハブ
    public Transform firePoint;     // 弾が出る場所
    public float fireRate = 0.2f;   // 連射速度（秒）

    private float nextFireTime = 0f;

    void Update()
    {
        // 1. マウスの方向を向く処理
        LookAtMouse();

        // 2. 左クリック（押しっぱなし対応）で発射
        if (Input.GetMouseButton(0) && Time.time > nextFireTime)
        {
            Shoot();
            nextFireTime = Time.time + fireRate; // 次に撃てるまでの時間をセット
        }
    }

    void LookAtMouse()
    {
        // マウスのスクリーン座標をゲーム内の世界座標に変換
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        // プレイヤーからマウスへの方向を計算
        Vector2 direction = (Vector2)mousePosition - (Vector2)transform.position;

        // その方向への角度（Rotation）を計算して適用
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void Shoot()
    {
        // 弾を生成！
        Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
    }
}