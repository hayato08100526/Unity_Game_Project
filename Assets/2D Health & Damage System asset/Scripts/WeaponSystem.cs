using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// 武器の照準・切り替え・発射を管理するスクリプト(ソロ・オンライン両対応)
// Gun_Normal(銃)に付ける
public class WeaponSystem : NetworkBehaviour
{
    [Header("武器（弾丸）設定")]
    public GameObject[] bulletPrefabs;
    public Transform firePoint;

    [Header("照準設定")]
    [Tooltip("マウスの方向へ銃を向ける")]
    public bool aimAtMouse = true;
    [Tooltip("体(Player)をマウスのある側へ向ける")]
    public bool faceBodyToMouse = true;
    [Tooltip("体の向きが逆になる場合はチェック")]
    public bool invertBodyFacing = false;
    [Tooltip("相手の銃の向きをなめらかにする速さ")]
    public float remoteAimSmoothing = 20f;

    [Header("UI設定")]
    public GameObject[] weaponSlots;
    public Image[] slotHighlightBorders;

    [Header("設定値")]
    public int startWeaponIndex = 0;

    private int currentWeaponIndex = 0;
    private float currentAimAngle = 0f;   // 狙っている角度(ワールド基準)
    private float displayedAimAngle = 0f; // 相手の銃の表示用角度
    private Transform body;

    // 狙っている角度(持ち主が書き込み、全員に同期)
    private readonly NetworkVariable<float> netAimAngle = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private bool IsOnline => IsSpawned;

    void Awake()
    {
        var pc = GetComponentInParent<PlayerController>();
        body = pc != null ? pc.transform : transform.root;
    }

    void Start()
    {
        currentWeaponIndex = startWeaponIndex;
        UpdateWeaponUI();
    }

    void Update()
    {
        // オンラインで他人のプレイヤー:同期された角度に銃を向けるだけ
        if (IsOnline && !IsOwner)
        {
            displayedAimAngle = Mathf.LerpAngle(displayedAimAngle, netAimAngle.Value,
                                                remoteAimSmoothing * Time.deltaTime);
            ApplyAim(displayedAimAngle);
            return;
        }

        if (aimAtMouse) AimAtMouse();

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) SwitchToNextWeapon();
        else if (scroll < 0f) SwitchToPreviousWeapon();

        if (Input.GetMouseButtonDown(0)) ShootCurrentWeapon();
    }

    // ===== 照準 =====

    void AimAtMouse()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 screenPos = Input.mousePosition;
        screenPos.z = Mathf.Abs(cam.transform.position.z - transform.position.z);
        Vector3 mouseWorld = cam.ScreenToWorldPoint(screenPos);

        // 体の中心からマウスへの方向(銃の根元から測るとカーソルが近いときにブレるため)
        Vector2 dir = (Vector2)(mouseWorld - body.position);
        if (dir.sqrMagnitude < 0.0001f) return;

        if (faceBodyToMouse) FaceBody(dir.x);

        currentAimAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (IsOnline) netAimAngle.Value = currentAimAngle;

        ApplyAim(currentAimAngle);
    }

    // 体をマウスのある側へ向ける(Scale Xの符号で左右反転)
    void FaceBody(float dirX)
    {
        if (Mathf.Abs(dirX) < 0.01f) return;

        float sign = dirX > 0f ? 1f : -1f;
        if (invertBodyFacing) sign = -sign;

        Vector3 s = body.localScale;
        s.x = Mathf.Abs(s.x) * sign;
        body.localScale = s;
    }

    // ワールド基準の角度に銃を向ける(親が左右反転していても正しく向くよう補正)
    void ApplyAim(float worldAngle)
    {
        float rad = worldAngle * Mathf.Deg2Rad;
        float parentSign = (transform.parent != null && transform.parent.lossyScale.x < 0f) ? -1f : 1f;
        float localAngle = Mathf.Atan2(Mathf.Sin(rad), Mathf.Cos(rad) * parentSign) * Mathf.Rad2Deg;
        transform.localRotation = Quaternion.Euler(0f, 0f, localAngle);
    }

    // ===== 武器切り替え =====

    void SwitchToNextWeapon()
    {
        if (bulletPrefabs.Length == 0) return;
        currentWeaponIndex = (currentWeaponIndex + 1) % bulletPrefabs.Length;
        UpdateWeaponUI();
    }

    void SwitchToPreviousWeapon()
    {
        if (bulletPrefabs.Length == 0) return;
        currentWeaponIndex = (currentWeaponIndex - 1 + bulletPrefabs.Length) % bulletPrefabs.Length;
        UpdateWeaponUI();
    }

    void UpdateWeaponUI()
    {
        if (IsOnline && !IsOwner) return;
        if (slotHighlightBorders == null || slotHighlightBorders.Length == 0) return;

        for (int i = 0; i < slotHighlightBorders.Length; i++)
        {
            if (slotHighlightBorders[i] != null)
                slotHighlightBorders[i].gameObject.SetActive(false);
        }

        if (currentWeaponIndex >= 0 && currentWeaponIndex < slotHighlightBorders.Length
            && slotHighlightBorders[currentWeaponIndex] != null)
        {
            slotHighlightBorders[currentWeaponIndex].gameObject.SetActive(true);
        }
    }

    // ===== 発射 =====

    void ShootCurrentWeapon()
    {
        if (firePoint == null) return;

        int i = currentWeaponIndex;
        Vector3 pos = firePoint.position;
        Quaternion rot = GetFireRotation();

        // 自分の画面にはすぐ出す
        SpawnBullet(i, pos, rot);

        // オンライン時は他の全員の画面にも同じ弾を出してもらう
        if (IsOnline) ShootRpc(i, pos, rot);
    }

    // 弾を飛ばす向き
    Quaternion GetFireRotation()
    {
        // マウス照準中は、狙った角度にそのまま飛ばす
        if (aimAtMouse) return Quaternion.Euler(0f, 0f, currentAimAngle);

        // 照準なしの場合は銃の見た目の向きに合わせる
        Quaternion rot = firePoint.rotation;
        if (firePoint.lossyScale.x < 0f)
            rot *= Quaternion.Euler(0f, 0f, 180f);
        return rot;
    }

    [Rpc(SendTo.NotMe)]
    void ShootRpc(int index, Vector3 position, Quaternion rotation)
    {
        SpawnBullet(index, position, rotation);
    }

    void SpawnBullet(int index, Vector3 position, Quaternion rotation)
    {
        if (index < 0 || index >= bulletPrefabs.Length || bulletPrefabs[index] == null) return;

        GameObject fired = Instantiate(bulletPrefabs[index], position, rotation);
        IgnoreShooter(fired);
    }

    // 撃った本人に自分の弾が当たらないようにする
    void IgnoreShooter(GameObject bullet)
    {
        Collider2D[] bulletCols = bullet.GetComponentsInChildren<Collider2D>();
        Collider2D[] shooterCols = body.GetComponentsInChildren<Collider2D>();

        foreach (Collider2D b in bulletCols)
            foreach (Collider2D s in shooterCols)
                Physics2D.IgnoreCollision(b, s);
    }
}