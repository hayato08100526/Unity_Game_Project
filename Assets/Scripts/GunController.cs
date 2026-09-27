using UnityEngine;
using Unity.Netcode;

public class GunController : NetworkBehaviour
{
    [Header("弾の設定")]
    public GameObject[] bulletPrefabs;   // WeaponWheelのSlotsと同じ順番にする

    public Transform firePoint;
    public float fireRate = 0.2f;
    private float nextFireTime = 0f;

    [Header("色設定")]
    public Color[] weaponColors;

    [Header("参照")]
    public WeaponWheel wheel;            // Inspectorでドラッグ

    int Index => wheel != null ? wheel.CurrentIndex : 0;
    bool IsOnline => IsSpawned;

    void Update()
    {
        // オンラインで他人のプレイヤーなら何もしない(向きはNetworkTransformが同期する)
        if (IsOnline && !IsOwner) return;

        LookAtMouse();

        // ホイールが開いている間は撃たない
        if (wheel != null && wheel.IsOpen) return;

        if (Input.GetMouseButton(0) && Time.time > nextFireTime)
        {
            Shoot();
            nextFireTime = Time.time + fireRate;
        }
    }

    void LookAtMouse()
    {
        if (Camera.main == null) return;

        Vector3 screen_point = Input.mousePosition;
        screen_point.z = 10.0f;
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(screen_point);
        Vector2 direction = (Vector2)mousePosition - (Vector2)transform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void Shoot()
    {
        if (bulletPrefabs.Length == 0 || firePoint == null) return;

        int i = Mathf.Clamp(Index, 0, bulletPrefabs.Length - 1);

        // 自分の画面にはすぐ出す
        SpawnBullet(i, firePoint.position, firePoint.rotation);

        // オンライン時は他の全員の画面にも同じ弾を出してもらう
        if (IsOnline) ShootRpc(i, firePoint.position, firePoint.rotation);
    }

    [Rpc(SendTo.NotMe)]
    void ShootRpc(int index, Vector3 position, Quaternion rotation)
    {
        SpawnBullet(index, position, rotation);
    }

    void SpawnBullet(int i, Vector3 position, Quaternion rotation)
    {
        if (i < 0 || i >= bulletPrefabs.Length) return;

        GameObject fired = Instantiate(bulletPrefabs[i], position, rotation);

        if (weaponColors.Length > i)
            foreach (SpriteRenderer sr in fired.GetComponentsInChildren<SpriteRenderer>())
                sr.color = weaponColors[i];
    }
}