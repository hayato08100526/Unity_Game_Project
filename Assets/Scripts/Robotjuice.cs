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
    }

    void Update()
    {
        MuzzleFlashes();
        if (rb == null || pc == null) return;

        Vector2 v = rb.linearVelocity;
        bool grounded = pc.IsGrounded;

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