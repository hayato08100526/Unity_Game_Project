using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

// 武器の照準・切り替え・発射・熱を管理するスクリプト(ソロ・オンライン・CPU対応)
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

    [Header("熱と持ち替え")]
    [Tooltip("持ち替えにかかる時間(秒)。この間は撃てない")]
    public float switchTime = 0.25f;
    [Tooltip("手に持っている武器が1秒に冷える量")]
    public float coolRateHeld = 15f;
    [Tooltip("しまっている武器が1秒に冷える量")]
    public float coolRateHolstered = 30f;
    [Tooltip("オーバーヒートしたとき撃てなくなる時間(秒)")]
    public float overheatLockTime = 30f;
    [Tooltip("オーバーヒートが明けたときの熱")]
    public float heatAfterOverheat = 0f;

    [Header("CPU")]
    [Tooltip("CPU が操作するときはオン(CpuController が自動でオンにする)")]
    public bool isCpu = false;

    [Header("部位破壊の効果")]
    [Tooltip("頭が壊れているとき、撃つたびにずれる角度(度)")]
    public float headBrokenAimError = 8f;
    [Tooltip("腕が壊れているときの持ち替え時間(秒)")]
    public float armBrokenSwitchTime = 0.4f;
    [Tooltip("腕が壊れているときの連射間隔の倍率")]
    public float armBrokenFireIntervalMul = 1.3f;

    [Header("ROUTE(線を描く武器)")]
    [Tooltip("描ける線の最大の長さ(マス)")]
    public float maxRouteLength = 12f;
    [Tooltip("線の点を置く間隔(マス)")]
    public float routePointSpacing = 0.3f;
    [Tooltip("線を描いている間の移動速度の倍率")]
    public float routeDrawSpeedMul = 0.5f;
    public Color routeLineColor = new Color(0.31f, 0.89f, 0.94f, 0.7f);

    private bool drawingRoute;
    private readonly List<Vector3> routePoints = new List<Vector3>();
    private float routeLength;
    private LineRenderer routeLine;
    private PlayerController owningController;

    public bool IsDrawingRoute => drawingRoute;

    private HealthSystem health;
    private bool ArmBroken => health != null && health.ArmBroken;
    private bool HeadBroken => health != null && health.HeadBroken;
    private float CurrentSwitchTime => ArmBroken ? armBrokenSwitchTime : switchTime;

    private int currentWeaponIndex = 0;
    private float currentAimAngle = 0f;   // 狙っている角度(ワールド基準)
    private float displayedAimAngle = 0f; // 相手の銃の表示用角度
    private Transform body;

    private float[] heat;
    private float[] lockedUntil;
    private float[] nextShotTime;
    private float switchEndTime = -1f;

    // 狙っている角度(持ち主が書き込み、全員に同期)
    private readonly NetworkVariable<float> netAimAngle = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private bool IsOnline => IsSpawned;

    // ===== 画面表示や CPU が読む情報 =====

    public int WeaponCount => bulletPrefabs != null ? bulletPrefabs.Length : 0;
    public int CurrentIndex => currentWeaponIndex;
    public bool IsSwitching => Time.time < switchEndTime;
    public float SwitchProgress => CurrentSwitchTime <= 0f ? 1f : Mathf.Clamp01(1f - (switchEndTime - Time.time) / CurrentSwitchTime);

    public float GetHeat(int i)
    {
        EnsureArrays();
        return (i >= 0 && i < heat.Length) ? heat[i] : 0f;
    }

    public bool IsLocked(int i)
    {
        EnsureArrays();
        return i >= 0 && i < lockedUntil.Length && Time.time < lockedUntil[i];
    }

    // オーバーヒートが明けるまでの残り秒数
    public float GetLockRemaining(int i)
    {
        if (!IsLocked(i)) return 0f;
        return lockedUntil[i] - Time.time;
    }

    public string GetWeaponName(int i)
    {
        if (i < 0 || i >= WeaponCount || bulletPrefabs[i] == null) return "-";
        WeaponInfo info = bulletPrefabs[i].GetComponent<WeaponInfo>();
        if (info != null && !string.IsNullOrEmpty(info.displayName)) return info.displayName;
        return bulletPrefabs[i].name.ToUpper();
    }

    // いちばん冷えている武器(オーバーヒート中は除く)
    public int CoolestWeapon()
    {
        EnsureArrays();
        int best = currentWeaponIndex;
        float bestHeat = float.MaxValue;
        for (int i = 0; i < WeaponCount; i++)
        {
            if (IsLocked(i)) continue;
            if (heat[i] < bestHeat)
            {
                bestHeat = heat[i];
                best = i;
            }
        }
        return best;
    }

    float HeatPerShot(int i)
    {
        WeaponInfo info = bulletPrefabs[i] != null ? bulletPrefabs[i].GetComponent<WeaponInfo>() : null;
        return info != null ? info.heatPerShot : 10f;
    }

    float FireInterval(int i)
    {
        WeaponInfo info = bulletPrefabs[i] != null ? bulletPrefabs[i].GetComponent<WeaponInfo>() : null;
        return info != null ? info.fireInterval : 0.15f;
    }

    void EnsureArrays()
    {
        int n = WeaponCount;
        if (heat != null && heat.Length == n) return;
        heat = new float[n];
        lockedUntil = new float[n];
        nextShotTime = new float[n];
        for (int i = 0; i < n; i++) lockedUntil[i] = -1f;
    }

    // ===== 毎フレーム =====

    void Awake()
    {
        var pc = GetComponentInParent<PlayerController>();
        owningController = pc;
        body = pc != null ? pc.transform : transform.root;
        health = GetComponentInParent<HealthSystem>();
        EnsureArrays();
    }

    void Start()
    {
        currentWeaponIndex = Mathf.Clamp(startWeaponIndex, 0, Mathf.Max(0, WeaponCount - 1));
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

        UpdateHeat();

        // CPU はマウスやキーを読まない(CpuController から操作される)
        if (isCpu) return;

        if (aimAtMouse) AimAtMouse();

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) SwitchToNextWeapon();
        else if (scroll < 0f) SwitchToPreviousWeapon();

        // 数字キー 1〜9 で武器を直接選ぶ
        for (int k = 0; k < 9 && k < WeaponCount; k++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + k)) SetWeapon(k);
        }

        if (IsRouteWeapon(currentWeaponIndex))
        {
            // ROUTE:押している間に線を描いて、離したら撃つ
            HandleRouteInput();
        }
        else
        {
            CancelRoute();
            // 左クリックを押している間、連射間隔ごとに撃つ
            if (Input.GetMouseButton(0)) ShootCurrentWeapon();
        }
    }

    void OnDisable()
    {
        CancelRoute();
    }

    // ===== ROUTE(線を描いて撃つ) =====

    bool IsRouteWeapon(int i)
    {
        return i >= 0 && i < WeaponCount && bulletPrefabs[i] != null
               && bulletPrefabs[i].GetComponent<RouteBullet>() != null;
    }

    void HandleRouteInput()
    {
        // 押した瞬間:描き始める
        if (Input.GetMouseButtonDown(0) && CanShoot())
        {
            drawingRoute = true;
            routePoints.Clear();
            routeLength = 0f;
            routePoints.Add(MouseWorld());
            SetSpeedMul(routeDrawSpeedMul);
        }

        if (!drawingRoute) return;

        // 押している間:マウスの動きに合わせて点を足す(最大の長さまで)
        if (Input.GetMouseButton(0))
        {
            Vector3 m = MouseWorld();
            Vector3 last = routePoints[routePoints.Count - 1];
            float d = Vector3.Distance(last, m);
            if (d >= routePointSpacing && routeLength < maxRouteLength)
            {
                float allow = Mathf.Min(d, maxRouteLength - routeLength);
                routePoints.Add(last + (m - last).normalized * allow);
                routeLength += allow;
            }
            UpdateRouteLine();
        }

        // 離した瞬間:撃つ
        if (Input.GetMouseButtonUp(0))
        {
            var pts = new List<Vector3>(routePoints);
            CancelRoute();
            FireRoute(pts);
        }
    }

    void FireRoute(List<Vector3> points)
    {
        if (firePoint == null || !CanShoot()) return;

        int i = currentWeaponIndex;
        ApplyShotCost(i);

        GameObject fired = Instantiate(bulletPrefabs[i], firePoint.position, Quaternion.Euler(0f, 0f, currentAimAngle));
        IgnoreShooter(fired);
        fired.SendMessage("SetOwner", body, SendMessageOptions.DontRequireReceiver);
        fired.SendMessage("SetPath", points.ToArray(), SendMessageOptions.DontRequireReceiver);
    }

    void CancelRoute()
    {
        if (!drawingRoute && (routeLine == null || !routeLine.enabled)) return;
        drawingRoute = false;
        if (routeLine != null) routeLine.enabled = false;
        SetSpeedMul(1f);
    }

    void SetSpeedMul(float value)
    {
        if (owningController != null) owningController.externalSpeedMul = value;
    }

    Vector3 MouseWorld()
    {
        Camera cam = Camera.main;
        if (cam == null) return body.position;
        Vector3 screenPos = Input.mousePosition;
        screenPos.z = Mathf.Abs(cam.transform.position.z);
        Vector3 w = cam.ScreenToWorldPoint(screenPos);
        w.z = 0f;
        return w;
    }

    void UpdateRouteLine()
    {
        if (routeLine == null)
        {
            var go = new GameObject("RouteLine");
            routeLine = go.AddComponent<LineRenderer>();
            routeLine.useWorldSpace = true;
            routeLine.startWidth = 0.07f;
            routeLine.endWidth = 0.07f;
            routeLine.numCapVertices = 2;
            routeLine.sortingOrder = 50;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null) routeLine.material = new Material(shader);

            routeLine.startColor = routeLineColor;
            routeLine.endColor = routeLineColor;
        }

        routeLine.enabled = true;
        routeLine.positionCount = routePoints.Count + 1;
        routeLine.SetPosition(0, firePoint != null ? firePoint.position : body.position);
        for (int k = 0; k < routePoints.Count; k++) routeLine.SetPosition(k + 1, routePoints[k]);
    }

    void UpdateHeat()
    {
        EnsureArrays();
        for (int i = 0; i < heat.Length; i++)
        {
            if (lockedUntil[i] > 0f)
            {
                if (Time.time < lockedUntil[i]) continue; // オーバーヒート中は冷えない
                lockedUntil[i] = -1f;
                heat[i] = heatAfterOverheat;               // 明けたら 50 から再開
            }

            float rate = (i == currentWeaponIndex) ? coolRateHeld : coolRateHolstered;
            heat[i] = Mathf.Max(0f, heat[i] - rate * Time.deltaTime);
        }
    }

    // ===== CPU 用の操作 =====

    // 指定した位置を狙う
    public void CpuAimAt(Vector2 worldPos)
    {
        Vector2 dir = worldPos - (Vector2)body.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        if (faceBodyToMouse) FaceBody(dir.x);

        currentAimAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        ApplyAim(currentAimAngle);
    }

    // 今の武器で撃つ
    public void CpuShoot()
    {
        ShootCurrentWeapon();
    }

    // 相手の位置を指定して撃つ(ROUTE のときは、相手の上を回り込む線を自動で描く)
    public void CpuShootAt(Vector2 target)
    {
        if (IsRouteWeapon(currentWeaponIndex) && firePoint != null)
        {
            Vector2 start = firePoint.position;
            Vector2 apex = new Vector2((start.x + target.x) / 2f, Mathf.Max(start.y, target.y) + 3f);
            Vector2 after = target + (target - apex).normalized * 2f;
            FireRoute(new List<Vector3> { apex, target, after });
            return;
        }
        ShootCurrentWeapon();
    }

    // 武器を持ち替える
    public void CpuSetWeapon(int index)
    {
        SetWeapon(index);
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

    void SetWeapon(int index)
    {
        if (WeaponCount == 0) return;
        index = Mathf.Clamp(index, 0, WeaponCount - 1);
        if (index == currentWeaponIndex) return;

        CancelRoute();
        currentWeaponIndex = index;
        switchEndTime = Time.time + CurrentSwitchTime; // 持ち替え中は撃てない
        UpdateWeaponUI();
    }

    void SwitchToNextWeapon()
    {
        if (WeaponCount == 0) return;
        SetWeapon((currentWeaponIndex + 1) % WeaponCount);
    }

    void SwitchToPreviousWeapon()
    {
        if (WeaponCount == 0) return;
        SetWeapon((currentWeaponIndex - 1 + WeaponCount) % WeaponCount);
    }

    void UpdateWeaponUI()
    {
        // CPU の持ち替えでプレイヤーの武器表示が変わらないようにする
        if (isCpu) return;
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

    public bool CanShoot()
    {
        EnsureArrays();
        int i = currentWeaponIndex;
        if (i < 0 || i >= WeaponCount) return false;
        if (IsSwitching) return false;
        if (IsLocked(i)) return false;
        return Time.time >= nextShotTime[i];
    }

    // 撃つたびの熱と連射間隔(100 でオーバーヒート)
    void ApplyShotCost(int i)
    {
        heat[i] += HeatPerShot(i);
        nextShotTime[i] = Time.time + FireInterval(i) * (ArmBroken ? armBrokenFireIntervalMul : 1f);

        if (heat[i] >= 100f)
        {
            heat[i] = 100f;
            lockedUntil[i] = Time.time + overheatLockTime;

            // 頭上に OVERHEAT を出す
            if (MatchManager.Instance != null)
                MatchManager.Instance.ShowPopup(body, "OVERHEAT", new Color32(0xFF, 0x5A, 0x5A, 0xFF), false);
        }
    }

    void ShootCurrentWeapon()
    {
        if (firePoint == null) return;
        if (!CanShoot()) return;

        int i = currentWeaponIndex;
        Vector3 pos = firePoint.position;
        Quaternion rot = GetFireRotation();

        // 熱と連射間隔
        ApplyShotCost(i);

        // 頭が壊れていると狙いがブレる
        if (HeadBroken) rot *= Quaternion.Euler(0f, 0f, Random.Range(-headBrokenAimError, headBrokenAimError));

        // 自分の画面にはすぐ出す
        SpawnBullet(i, pos, rot);

        // オンライン時は他の全員の画面にも同じ弾を出してもらう
        if (IsOnline) ShootRpc(i, pos, rot);
    }

    // 弾を飛ばす向き
    Quaternion GetFireRotation()
    {
        // マウス照準中(と CPU)は、狙った角度にそのまま飛ばす
        if (aimAtMouse || isCpu) return Quaternion.Euler(0f, 0f, currentAimAngle);

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

        GameObject prefab = bulletPrefabs[index];

        // ShotPattern が付いている弾は、扇状に何発もばらまく(ショットガン)
        ShotPattern pattern = prefab.GetComponent<ShotPattern>();
        int count = pattern != null ? Mathf.Max(1, pattern.pelletCount) : 1;
        float spread = pattern != null ? pattern.spreadAngle : 0f;
        float jitter = pattern != null ? pattern.randomJitter : 0f;

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : (float)i / (count - 1) - 0.5f;
            float angle = t * spread + Random.Range(-jitter, jitter);
            Quaternion rot = rotation * Quaternion.Euler(0f, 0f, angle);

            GameObject fired = Instantiate(prefab, position, rot);
            IgnoreShooter(fired);
            fired.SendMessage("SetOwner", body, SendMessageOptions.DontRequireReceiver);
        }
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