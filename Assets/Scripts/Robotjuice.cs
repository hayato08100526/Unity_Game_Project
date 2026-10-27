using System.Collections.Generic;
using UnityEngine;

// ロボットの「見た目の気持ちよさ」を足す係(どこにも付けなくてよい。ゲームが始まると自動で全員に付く)
//  ・撃ったときの銃口の光(マズルフラッシュ)
//  ・ダッシュしたときの残像
//  ・走るときの足元の土ぼこり / ジャンプと着地の煙
//  ・KO の瞬間のスローモーションと画面の白い光(CPU 戦だけ)
public class RobotJuice : MonoBehaviour
{
    static readonly Color Cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
    static readonly Color Amber = new Color32(0xFF, 0xB5, 0x47, 0xFF);
    static readonly Color Dust = new Color(0.62f, 0.67f, 0.74f, 0.75f);

    private PlayerController pc;
    private Rigidbody2D rb;
    private HealthSystem hs;
    private Color team;

    private readonly Dictionary<WeaponSystem, float> lastShots = new Dictionary<WeaponSystem, float>();
    private bool wasGrounded = true;
    private float maxFallSpeed;
    private float nextGhost, nextStep;
    private int lastHp = -1;

    // ================= 自動で付ける =================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Manager.instance != null) return;
        GameObject go = new GameObject("RobotJuiceManager");
        DontDestroyOnLoad(go);
        go.AddComponent<Manager>();
    }

    void Start()
    {
        pc = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody2D>();
        hs = GetComponent<HealthSystem>();
        // CPU と、オンラインで参加した人(P2)はオレンジ
        bool orange = pc != null && (pc.isCpu || (pc.IsSpawned && pc.OwnerClientId != 0));
        team = orange ? Amber : Cyan;
        if (hs != null) lastHp = hs.currentHealth;
        lastPos = transform.position;
    }

    // 自分が操作しているロボットか(CPU 戦なら Player、オンラインなら自分の方)
    public bool IsLocalHuman => pc != null && !pc.isCpu && (!pc.IsSpawned || pc.IsOwner);

    // ===== 反動:撃つと銃が後ろに下がって、すぐ戻る =====
    private readonly Dictionary<WeaponSystem, Vector3> gunBase = new Dictionary<WeaponSystem, Vector3>();
    private readonly Dictionary<WeaponSystem, float> gunKick = new Dictionary<WeaponSystem, float>();

    void LateUpdate()
    {
        foreach (var kv in gunKick)
        {
            WeaponSystem ws = kv.Key;
            if (ws == null || !gunBase.ContainsKey(ws)) continue;
            float k = kv.Value;
            Vector3 back = ws.transform.localRotation * Vector3.left;
            ws.transform.localPosition = gunBase[ws] + back * (0.18f * k);
        }
        var keys = new List<WeaponSystem>(gunKick.Keys);
        foreach (var ws in keys) gunKick[ws] = Mathf.MoveTowards(gunKick[ws], 0f, Time.deltaTime * 9f);
    }

    void Kick(WeaponSystem ws, float strength)
    {
        if (!gunBase.ContainsKey(ws)) gunBase[ws] = ws.transform.localPosition;
        gunKick[ws] = Mathf.Clamp01(strength);
    }

    // 動きの演出用(相手の画面のロボットは物理で動いていないので、位置の変化から速さを出す)
    private Vector2 lastPos;
    private Vector2 prevVel;
    private bool wasDashing;
    private float nextThrust;

    bool IsRemote => pc != null && pc.IsSpawned && !pc.IsOwner;

    bool GroundedNow()
    {
        if (!IsRemote) return pc.IsGrounded;
        Vector2 f = Feet();
        foreach (RaycastHit2D h in Physics2D.RaycastAll(f + Vector2.up * 0.05f, Vector2.down, 0.2f))
            if (h.collider != null && !h.collider.isTrigger && !h.collider.transform.IsChildOf(transform)) return true;
        return false;
    }

    void Update()
    {
        MuzzleFlashes();
        if (rb == null || pc == null) return;

        Vector2 pos = transform.position;
        Vector2 v = IsRemote ? (Time.deltaTime > 0f ? (pos - lastPos) / Time.deltaTime : Vector2.zero) : rb.linearVelocity;
        lastPos = pos;
        bool grounded = GroundedNow();

        MoveFx(v, grounded);
        prevVel = v;

        // ダッシュの残像(普通に走るより、ずっと速く動いているとき)
        if (Mathf.Abs(v.x) > pc.moveSpeed * 1.35f && Time.time >= nextGhost)
        {
            nextGhost = Time.time + 0.03f;
            SpawnGhost();
        }

        // 走るときの足元の土ぼこり
        if (grounded && Mathf.Abs(v.x) > 3f && Time.time >= nextStep)
        {
            nextStep = Time.time + 0.16f;
            Puff(Feet(), new Vector2(-Mathf.Sign(v.x) * 1.2f, 0.6f), 0.7f, 0.25f);
        }

        // ジャンプした瞬間 / 着地した瞬間の煙
        if (!grounded) maxFallSpeed = Mathf.Max(maxFallSpeed, -v.y);
        if (wasGrounded && !grounded && v.y > 4f)
        {
            for (int s = -1; s <= 1; s += 2) Puff(Feet(), new Vector2(s * 1.8f, 0.4f), 0.9f, 0.3f);
        }
        if (!wasGrounded && grounded)
        {
            if (maxFallSpeed > 11f)
                Fx.MakeShaped(Feet(), Vector2.zero, new Color(1f, 1f, 1f, 0.7f), new Vector2(1.4f, 0.32f), 0.28f, 21, Fx.Ring(), 2f);
            if (maxFallSpeed > 7f)
            {
                int n = maxFallSpeed > 14f ? 5 : 3;
                for (int i = 0; i < n; i++)
                    for (int s = -1; s <= 1; s += 2)
                        Puff(Feet(), new Vector2(s * Random.Range(1.5f, 4f), Random.Range(0.2f, 1f)), Random.Range(0.9f, 1.4f), 0.35f);
            }
            maxFallSpeed = 0f;
        }
        wasGrounded = grounded;

        // KO の瞬間(HP が 0 になった)
        if (hs != null)
        {
            if (lastHp > 0 && hs.currentHealth <= 0) Manager.KoMoment(team);
            lastHp = hs.currentHealth;
        }
    }

    Vector2 Feet()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) return new Vector2(col.bounds.center.x, col.bounds.min.y + 0.05f);
        return (Vector2)transform.position + Vector2.down * 0.5f;
    }

    // ================= マズルフラッシュ =================
    void MuzzleFlashes()
    {
        foreach (WeaponSystem ws in GetComponentsInChildren<WeaponSystem>())
        {
            if (!lastShots.TryGetValue(ws, out float last))
            {
                lastShots[ws] = ws.LastShotTime;
                continue;
            }
            if (ws.LastShotTime > last + 0.0001f)
            {
                lastShots[ws] = ws.LastShotTime;
                // 反動(重い武器ほど大きく)。自分の弾は画面も少しゆれる
                float heavy = Mathf.Clamp01(HeatOf(ws) / 45f);
                Kick(ws, 0.5f + 0.5f * heavy);
                if (IsLocalHuman)
                {
                    CameraFollow.Shake(0.03f + 0.07f * heavy, 0.07f);
                    Manager.OnLocalShot(heavy);
                }
                Transform fp = ws.firePoint != null ? ws.firePoint : ws.transform;
                Vector2 dir = fp.right;
                Fx.Make(fp.position, Vector2.zero, Color.white, 0.55f, 0.06f, 0f, 23, Fx.Soft(), 0f);
                Fx.Make(fp.position, Vector2.zero, new Color(team.r, team.g, team.b, 0.8f), 1.1f, 0.09f, 0f, 22, Fx.Soft(), 0f);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 d = Quaternion.Euler(0, 0, Random.Range(-30f, 30f)) * dir;
                    Fx.Make(fp.position, d * Random.Range(4f, 8f), i == 0 ? Color.white : team, 0.12f, Random.Range(0.08f, 0.14f), 0f, 23, Fx.Dot(), 1f);
                }
            }
        }
    }

    static float HeatOf(WeaponSystem ws)
    {
        int i = ws.CurrentIndex;
        if (ws.bulletPrefabs == null || i < 0 || i >= ws.bulletPrefabs.Length || ws.bulletPrefabs[i] == null) return 10f;
        WeaponInfo info = ws.bulletPrefabs[i].GetComponent<WeaponInfo>();
        return info != null ? info.heatPerShot : 10f;
    }

    // ================= ダッシュ・空中ジャンプ・壁キックの演出 =================
    void MoveFx(Vector2 v, bool grounded)
    {
        if (hs != null && hs.currentHealth <= 0) { wasDashing = false; return; }
        Vector2 center = (Vector2)transform.position + Vector2.up * 0.4f;

        // ダッシュ:始まった瞬間に衝撃の輪と、うしろへ流れる線。ダッシュ中は背中からジェット
        bool dashing = Mathf.Abs(v.x) > pc.moveSpeed * 1.6f;
        if (dashing)
        {
            float dir = Mathf.Sign(v.x);
            if (!wasDashing)
            {
                SoundManager.Play("dash", IsLocalHuman ? 0.8f : 0.5f, 0.08f);
                Fx.MakeShaped(center, Vector2.zero, new Color(1f, 1f, 1f, 0.8f), new Vector2(0.5f, 1.4f), 0.22f, 21, Fx.Ring(), 2.2f);
                for (int i = 0; i < 7; i++)
                {
                    Vector2 p = center + new Vector2(-dir * Random.Range(0f, 0.6f), Random.Range(-0.7f, 0.7f));
                    Color c = i % 3 == 0 ? Color.white : team;
                    Fx.MakeShaped(p, new Vector2(-dir * Random.Range(9f, 14f), 0f), A(c, 0.85f), new Vector2(Random.Range(0.9f, 1.6f), 0.07f), Random.Range(0.15f, 0.25f), 21, Fx.Streak(), 0f);
                }
                if (grounded)
                    for (int i = 0; i < 4; i++)
                        Puff(Feet(), new Vector2(-dir * Random.Range(2f, 5f), Random.Range(0.3f, 1.2f)), Random.Range(1f, 1.5f), 0.35f);
            }
            if (Time.time >= nextThrust)
            {
                nextThrust = Time.time + 0.025f;
                Vector2 back = center + new Vector2(-dir * 0.45f, Random.Range(-0.15f, 0.1f));
                Fx.MakeShaped(back, new Vector2(-dir * Random.Range(3f, 6f), Random.Range(-0.5f, 0.5f)), Random.value < 0.4f ? new Color(1f, 0.95f, 0.8f, 0.9f) : A(team, 0.9f),
                              new Vector2(0.45f, 0.22f), 0.14f, 4, Fx.Flame(), 0f, dir > 0 ? 0f : 180f);
            }
        }
        wasDashing = dashing;

        // 空中で急に上向きになった = 空中ジャンプか壁キック
        bool upKick = !grounded && v.y > 4f && v.y - prevVel.y > 6f;
        if (upKick)
        {
            bool wallKick = Mathf.Abs(v.x) > 6f && (Mathf.Abs(prevVel.x) < 2f || Mathf.Sign(prevVel.x) != Mathf.Sign(v.x));
            if (wallKick)
            {
                // 壁を蹴った側に火花と縦の輪
                float wallDir = -Mathf.Sign(v.x);
                Vector2 wp = center + new Vector2(wallDir * 0.45f, 0f);
                Fx.MakeShaped(wp, Vector2.zero, new Color(1f, 1f, 1f, 0.85f), new Vector2(0.35f, 1.2f), 0.2f, 21, Fx.Ring(), 1.6f);
                for (int i = 0; i < 8; i++)
                {
                    Vector2 d = new Vector2(-wallDir * Random.Range(0.3f, 1f), Random.Range(-1f, 1f)).normalized;
                    Fx.Make(wp, d * Random.Range(4f, 9f), i % 2 == 0 ? Color.white : team, 0.1f, Random.Range(0.12f, 0.22f), 6f, 23, Fx.Dot(), 0f);
                }
            }
            else
            {
                // 足元に、平たい衝撃の輪と、下向きの噴射
                Vector2 f = Feet();
                Fx.MakeShaped(f, Vector2.zero, A(team, 0.9f), new Vector2(1.2f, 0.3f), 0.25f, 21, Fx.Ring(), 1.8f);
                for (int i = 0; i < 3; i++)
                    Fx.MakeShaped(f + new Vector2(Random.Range(-0.2f, 0.2f), 0f), new Vector2(Random.Range(-1f, 1f), -Random.Range(3f, 6f)),
                                  i == 0 ? new Color(1f, 0.95f, 0.8f, 0.9f) : A(team, 0.85f), new Vector2(0.4f, 0.22f), 0.16f, 4, Fx.Flame(), 0f, 90f);
            }
        }
    }

    static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

    // ================= 残像 =================
    void SpawnGhost()
    {
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>())
        {
            if (!sr.enabled || sr.sprite == null || !sr.gameObject.activeInHierarchy) continue;
            GameObject g = new GameObject("Ghost");
            g.transform.SetPositionAndRotation(sr.transform.position, sr.transform.rotation);
            g.transform.localScale = sr.transform.lossyScale;
            SpriteRenderer gs = g.AddComponent<SpriteRenderer>();
            gs.sprite = sr.sprite;
            gs.flipX = sr.flipX;
            gs.flipY = sr.flipY;
            gs.sortingLayerID = sr.sortingLayerID;
            gs.sortingOrder = sr.sortingOrder - 3;
            gs.color = new Color(team.r, team.g, team.b, 0.45f);
            Fx fx = g.AddComponent<Fx>();
            fx.Init(Vector2.zero, 0.2f, 0f, false);
        }
    }

    // ================= 土ぼこり =================
    static void Puff(Vector2 pos, Vector2 vel, float size, float life)
    {
        Fx.Make(pos + new Vector2(Random.Range(-0.15f, 0.15f), 0f), vel, Dust, 0.25f * size, life, -1.5f, 4, Fx.Soft(), 1.6f);
    }

    // ================= 小さな光・煙の粒 =================
    public class Fx : MonoBehaviour
    {
        private static Sprite soft, dot;
        private Vector2 vel;
        private float life, maxLife, grav, grow;
        private SpriteRenderer sr;
        private Color baseColor;
        private Vector3 baseScale;
        private bool shrink = true;

        public static Sprite Soft()
        {
            if (soft == null)
            {
                const int S = 32;
                Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                        float a = Mathf.Clamp01(1f - d);
                        t.SetPixel(x, y, new Color(1, 1, 1, a * a));
                    }
                t.Apply();
                soft = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);   // 1 マスの丸
            }
            return soft;
        }

        public static Sprite Dot()
        {
            if (dot == null)
            {
                Texture2D t = new Texture2D(4, 4);
                Color[] px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = Color.white;
                t.SetPixels(px);
                t.filterMode = FilterMode.Point;
                t.Apply();
                dot = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);   // 1 マスの四角
            }
            return dot;
        }

        private static Sprite ring, streak, flame;

        // 縦横の大きさがちがう粒(輪・線・炎)。angle で向きを変える
        public static void MakeShaped(Vector2 pos, Vector2 vel, Color c, Vector2 size, float life, int order, Sprite sp, float grow, float angle = 0f)
        {
            GameObject go = new GameObject("Fx");
            go.transform.position = new Vector3(pos.x, pos.y, -1f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.color = c;
            r.sortingOrder = order;
            Fx fx = go.AddComponent<Fx>();
            fx.grow = grow;
            fx.Init(vel, life, 0f, grow <= 0f);
        }

        // 輪(衝撃波)
        public static Sprite Ring()
        {
            if (ring == null)
            {
                const int S = 64;
                Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, false);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                        float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) * 9f);
                        t.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                t.Apply();
                ring = Sprite.Create(t, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
            }
            return ring;
        }

        // 流れる線(真ん中が明るく、両はしが消える)
        public static Sprite Streak()
        {
            if (streak == null)
            {
                const int W = 64, H = 8;
                Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = x / (W - 1f), vv = Mathf.Abs(y + 0.5f - H / 2f) / (H / 2f);
                        float a = Mathf.Sin(u * Mathf.PI) * (1f - vv * vv);
                        t.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                t.Apply();
                streak = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
            }
            return streak;
        }

        // 炎(しずくの形。右が太く、左へ細くなる)
        public static Sprite Flame()
        {
            if (flame == null)
            {
                const int W = 32, H = 16;
                Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, false);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = x / (W - 1f);                       // 0 = 先 / 1 = 根もと
                        float half = Mathf.Lerp(0.08f, 1f, Mathf.Sqrt(u)) * (1f - Mathf.Pow(u, 6f));
                        float vv = Mathf.Abs(y + 0.5f - H / 2f) / (H / 2f);
                        float a = Mathf.Clamp01((half - vv) * 4f) * Mathf.Clamp01(u * 3f);
                        t.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                t.Apply();
                flame = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), W);
            }
            return flame;
        }

        public static void Make(Vector2 pos, Vector2 vel, Color c, float size, float life, float grav, int order, Sprite sp, float grow)
        {
            GameObject go = new GameObject("Fx");
            go.transform.position = new Vector3(pos.x, pos.y, -1f);
            go.transform.localScale = Vector3.one * size;
            SpriteRenderer r = go.AddComponent<SpriteRenderer>();
            r.sprite = sp;
            r.color = c;
            r.sortingOrder = order;
            Fx fx = go.AddComponent<Fx>();
            fx.grow = grow;
            fx.Init(vel, life, grav, grow <= 0f);
        }

        public void Init(Vector2 v, float l, float g, bool shrinkOut)
        {
            sr = GetComponent<SpriteRenderer>();
            baseColor = sr.color;
            baseScale = transform.localScale;
            vel = v;
            life = maxLife = l;
            grav = g;
            shrink = shrinkOut;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f) { Destroy(gameObject); return; }
            vel.y -= grav * dt;
            vel *= 1f - Mathf.Min(1f, 4f * dt);   // だんだん遅くなる
            transform.position += (Vector3)(vel * dt);
            float u = 1f - life / maxLife;        // 0 → 1
            if (shrink) transform.localScale = baseScale * (1f - u * 0.6f);
            else if (grow > 0f) transform.localScale = baseScale * (1f + u * grow);
            sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * (1f - u));
        }
    }

    // ================= 全員に付ける係 + KO の演出 =================
    public class Manager : MonoBehaviour
    {
        public static Manager instance;
        private float nextScan;
        private float flashStart = -10f;
        private Color flashColor = Color.white;
        private float slowUntil = -1f;
        private const float SlowScale = 0.25f;
        private float baseFixed = 0.02f;

        void Awake() { instance = this; }

        void Update()
        {
            // 新しく出てきたロボット(オンラインで後から入った人も)に付ける
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.5f;
                foreach (PlayerController p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                    if (p.GetComponent<RobotJuice>() == null) p.gameObject.AddComponent<RobotJuice>();
            }

            // スローモーションを元に戻す(ポーズ中などで 0 になっていたら、さわらない)
            if (slowUntil > 0f && Time.unscaledTime >= slowUntil)
            {
                slowUntil = -1f;
                if (Mathf.Approximately(Time.timeScale, SlowScale)) Time.timeScale = 1f;
                Time.fixedDeltaTime = baseFixed;
            }
        }

        public static void KoMoment(Color team)
        {
            if (instance == null) return;
            instance.flashStart = Time.unscaledTime;
            instance.flashColor = Color.Lerp(Color.white, team, 0.3f);

            // スローは CPU 戦だけ(オンラインでは相手とずれるのでやらない)
            bool hitStopping = Mathf.Approximately(Time.timeScale, HitStopScale);   // 最後の一撃のヒットストップ中でも OK
            if (MatchManager.Instance != null && !MatchManager.Instance.IsOnlineMode && (Time.timeScale >= 0.99f || hitStopping))
            {
                instance.hitStopUntil = -1f;
                if (instance.slowUntil < 0f) instance.baseFixed = Time.fixedDeltaTime;
                Time.timeScale = SlowScale;
                Time.fixedDeltaTime = instance.baseFixed * SlowScale;   // スロー中も動きがカクカクしないように
                instance.slowUntil = Time.unscaledTime + 0.55f;
            }
        }

        // ===== 照準(マウスの位置に出す十字)とヒットマーカー =====
        private float shotKick;            // 撃った直後は十字が広がる
        private float hitConfirmAt = -10f; // 自分の弾が当たった時刻
        private struct Marker { public Vector3 world; public float t; }
        private readonly List<Marker> markers = new List<Marker>();
        private bool cursorHidden;

        public static void OnLocalShot(float heavy)
        {
            if (instance != null) instance.shotKick = Mathf.Max(instance.shotKick, 0.6f + 0.4f * heavy);
        }

        // 自分の弾が当たったとき(HealthSystem から呼ばれる)
        public static void HitConfirm(Vector2 worldPos)
        {
            if (instance == null) return;
            instance.hitConfirmAt = Time.unscaledTime;
            instance.markers.Add(new Marker { world = worldPos, t = Time.unscaledTime });
        }

        bool ShowCrosshair()
        {
            MatchManager mm = MatchManager.Instance;
            return mm != null && mm.IsFighting && !mm.IsPaused && !WeaponSystem.UsingGamepad;
        }

        void DrawCrosshair()
        {
            bool show = ShowCrosshair();
            if (show != cursorHidden)
            {
                cursorHidden = show;
                Cursor.visible = !show;   // 試合中は、ふつうのマウスの矢印を隠す
            }

            // ヒットマーカー(当たった場所に × が出て、少し大きくなりながら消える)
            Camera cam = Camera.main;
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - markers[i].t;
                if (age > 0.3f || cam == null) { markers.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(markers[i].world);
                Vector2 c = new Vector2(sp.x, Screen.height - sp.y);
                float u = age / 0.3f;
                float s = Screen.height * (0.014f + 0.01f * u);
                DrawX(c, s, 3f, new Color(1f, 1f, 1f, 1f - u));
            }

            if (!show) return;
            shotKick = Mathf.MoveTowards(shotKick, 0f, Time.unscaledDeltaTime * 4f);
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            float unit = Screen.height / 100f;
            float gap = unit * (0.9f + 1.6f * shotKick);
            float len = unit * 1.3f;
            float th = Mathf.Max(2f, unit * 0.3f);
            bool hit = Time.unscaledTime - hitConfirmAt < 0.18f;
            Color col = hit ? new Color32(0xFF, 0x5A, 0x5A, 0xFF) : new Color(1f, 1f, 1f, 0.9f);
            Color shadow = new Color(0f, 0f, 0f, 0.5f);
            for (int pass = 0; pass < 2; pass++)
            {
                Color cc = pass == 0 ? shadow : col;
                float o = pass == 0 ? 1.5f : 0f;
                Box(new Rect(m.x - th / 2f + o, m.y - gap - len + o, th, len), cc);
                Box(new Rect(m.x - th / 2f + o, m.y + gap + o, th, len), cc);
                Box(new Rect(m.x - gap - len + o, m.y - th / 2f + o, len, th), cc);
                Box(new Rect(m.x + gap + o, m.y - th / 2f + o, len, th), cc);
                Box(new Rect(m.x - th / 2f + o, m.y - th / 2f + o, th, th), cc);
            }
            if (hit) DrawX(m, gap + len * 0.6f, 2f, col);
        }

        void Box(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        void DrawX(Vector2 c, float size, float thick, Color col)
        {
            Matrix4x4 keep = GUI.matrix;
            for (int k = 0; k < 2; k++)
            {
                GUIUtility.RotateAroundPivot(k == 0 ? 45f : -45f, c);
                Box(new Rect(c.x - size, c.y - thick / 2f, size * 0.7f, thick), col);
                Box(new Rect(c.x + size * 0.3f, c.y - thick / 2f, size * 0.7f, thick), col);
                GUI.matrix = keep;
            }
        }

        void OnDisable()
        {
            if (cursorHidden) Cursor.visible = true;
        }

        // ===== ヒットストップ =====
        private float hitStopUntil = -1f;
        private const float HitStopScale = 0.02f;

        public static void HitStop(float seconds)
        {
            if (instance == null) return;
            if (MatchManager.Instance == null || MatchManager.Instance.IsOnlineMode) return;   // オンラインではやらない
            if (Time.timeScale < 0.99f && !Mathf.Approximately(Time.timeScale, HitStopScale)) return;   // ポーズ中・KO スロー中はやらない
            Time.timeScale = HitStopScale;
            instance.hitStopUntil = Mathf.Max(instance.hitStopUntil, Time.unscaledTime + seconds);
        }

        void LateUpdate()
        {
            if (hitStopUntil > 0f && Time.unscaledTime >= hitStopUntil)
            {
                hitStopUntil = -1f;
                if (Mathf.Approximately(Time.timeScale, HitStopScale)) Time.timeScale = 1f;
            }
        }

        void OnGUI()
        {
            GUI.depth = -50;
            DrawCrosshair();
            float u = (Time.unscaledTime - flashStart) / 0.3f;
            if (u < 0f || u > 1f) return;
            GUI.depth = -100;
            Color prev = GUI.color;
            GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0.55f * (1f - u));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}