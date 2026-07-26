using UnityEngine;
using TMPro;

public class GunController : MonoBehaviour
{
    [Header("弾の設定")]
    public GameObject[] bulletPrefabs;
    private int currentBulletIndex = 0;

    public Transform firePoint;
    public float fireRate = 0.2f;
    private float nextFireTime = 0f;

    [Header("UIと弾のカラー設定")]
    // ★追加：UI全体をまとめた親オブジェクトを登録する枠
    public GameObject weaponUIPanel;
    public TextMeshProUGUI[] weaponTexts;
    public Color normalColor = Color.gray;
    public Color[] weaponColors;

    void Start()
    {
        UpdateUI();

        // ★追加：ゲーム開始時はUIを非表示（隠す）にしておく
        if (weaponUIPanel != null)
        {
            weaponUIPanel.SetActive(false);
        }
    }

    void Update()
    {
        LookAtMouse();

        // ★変更：マウスの中央ボタンを押した時に、UIの表示/非表示を切り替える
        if (Input.GetMouseButtonDown(2))
        {
            if (weaponUIPanel != null)
            {
                // 今の状態の逆にする（表示中なら隠す、隠れていたら表示する）
                bool isActive = weaponUIPanel.activeSelf;
                weaponUIPanel.SetActive(!isActive);
            }
        }

        // ★追加：UIが表示されている時だけ、マウスのホイール回転で弾を切り替える
        if (weaponUIPanel != null && weaponUIPanel.activeSelf)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (scroll > 0f)
            {
                SwitchBullet(1); // 上に回すと次の弾へ
            }
            else if (scroll < 0f)
            {
                SwitchBullet(-1); // 下に回すと前の弾へ
            }
        }

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

    // ★変更：ホイールの上下に合わせて切り替える方向を指定できるようにした
    void SwitchBullet(int direction)
    {
        if (bulletPrefabs.Length == 0) return;

        currentBulletIndex += direction;

        // 配列の範囲を超えたらループさせる
        if (currentBulletIndex >= bulletPrefabs.Length)
        {
            currentBulletIndex = 0;
        }
        else if (currentBulletIndex < 0)
        {
            currentBulletIndex = bulletPrefabs.Length - 1;
        }

        UpdateUI();
    }

    void Shoot()
    {
        if (bulletPrefabs.Length == 0) return;

        GameObject currentBullet = bulletPrefabs[currentBulletIndex];
        GameObject firedBullet = Instantiate(currentBullet, firePoint.position, firePoint.rotation);

        // ★変更：弾の親オブジェクトだけでなく、子オブジェクトの画像もすべて探して色を変える
        SpriteRenderer[] renderers = firedBullet.GetComponentsInChildren<SpriteRenderer>();
        foreach (SpriteRenderer sr in renderers)
        {
            if (weaponColors.Length > currentBulletIndex)
            {
                sr.color = weaponColors[currentBulletIndex];
            }
        }
    }

    void UpdateUI()
    {
        for (int i = 0; i < weaponTexts.Length; i++)
        {
            if (weaponTexts[i] != null)
            {
                weaponTexts[i].color = normalColor;
            }
        }

        if (weaponTexts.Length > currentBulletIndex && weaponTexts[currentBulletIndex] != null)
        {
            if (weaponColors.Length > currentBulletIndex)
            {
                weaponTexts[currentBulletIndex].color = weaponColors[currentBulletIndex];
            }
        }
    }
}