using UnityEngine;
using TMPro;

public class GunController : MonoBehaviour
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

    // WeaponWheelが持っている番号をそのまま使う
    int Index => wheel != null ? wheel.CurrentIndex : 0;

    void Update()
    {
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
        Vector3 screen_point = Input.mousePosition;
        screen_point.z = 10.0f;
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(screen_point);
        Vector2 direction = (Vector2)mousePosition - (Vector2)transform.position;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);
    }

    void Shoot()
    {
        if (bulletPrefabs.Length == 0) return;

        int i = Mathf.Clamp(Index, 0, bulletPrefabs.Length - 1);
        GameObject fired = Instantiate(bulletPrefabs[i], firePoint.position, firePoint.rotation);

        if (weaponColors.Length > i)
            foreach (SpriteRenderer sr in fired.GetComponentsInChildren<SpriteRenderer>())
                sr.color = weaponColors[i];
    }
}
