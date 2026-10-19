using System.Collections.Generic;
using UnityEngine;

// 足場の上に出るアイテム(REPAIR = HP 回復 / COOLANT = 武器の熱を 0 に)
// ItemSpawner が自動で作るので、シーンに置く必要はない
public class ItemPickup : MonoBehaviour
{
    public enum Kind { Repair, Coolant }

    public static readonly List<ItemPickup> Active = new List<ItemPickup>();

    public Kind kind;
    [HideInInspector] public int netId = -1;   // オンラインでの番号(2人の画面で同じアイテムを指す)
    public int repairAmount = 30;
    public float lifeTime = 10f;

    private float born;
    private Vector3 basePos;
    private SpriteRenderer sr;

    private static Sprite repairSprite, coolantSprite;

    static readonly Color RepairColor = new Color32(0x5A, 0xF0, 0x8A, 0xFF);
    static readonly Color CoolantColor = new Color32(0x6F, 0xA8, 0xFF, 0xFF);

    public static ItemPickup Create(Kind kind, Vector2 pos)
    {
        GameObject go = new GameObject(kind == Kind.Repair ? "Item_Repair" : "Item_Coolant");
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        var item = go.AddComponent<ItemPickup>();
        item.kind = kind;

        item.sr = go.AddComponent<SpriteRenderer>();
        item.sr.sprite = kind == Kind.Repair ? RepairSprite() : CoolantSprite();
        item.sr.sortingOrder = 8;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.45f;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;   // 当たり判定のため。落ちない

        return item;
    }

    void Start()
    {
        born = Time.time;
        basePos = transform.position;
        Active.Add(this);
    }

    void OnDestroy()
    {
        Active.Remove(this);
    }

    void Update()
    {
        float age = Time.time - born;
        if (age >= lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        // オンライン(ホスト):相手のロボットは物理で当たらないことがあるので、距離で「さわった」を調べる
        if (netId >= 0 && MatchManager.Instance != null)
        {
            foreach (HealthSystem h in FindObjectsByType<HealthSystem>(FindObjectsSortMode.None))
            {
                if (h.currentHealth <= 0 || !h.IsSpawned || !h.IsServer) continue;
                Collider2D c = h.GetComponent<Collider2D>();
                Vector2 p = c != null ? (Vector2)c.bounds.center : (Vector2)h.transform.position;
                float r = c != null ? Mathf.Max(c.bounds.extents.x, c.bounds.extents.y) + 0.45f : 1f;
                if (Vector2.Distance(p, basePos) < r)
                {
                    MatchManager.Instance.OnlineItemTouched(this, h);
                    break;
                }
            }
        }

        // ふわふわ上下 + 最後の 3 秒は点滅
        transform.position = basePos + Vector3.up * Mathf.Sin(age * 3f) * 0.12f;
        bool blink = lifeTime - age < 3f && Mathf.FloorToInt(age * 8f) % 2 == 0;
        sr.enabled = !blink;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        HealthSystem who = other.GetComponentInParent<HealthSystem>();
        if (who == null || who.currentHealth <= 0) return;

        // オンライン:取ったかどうかはホストが決めて、2人の画面に知らせる
        if (netId >= 0)
        {
            if (MatchManager.Instance != null) MatchManager.Instance.OnlineItemTouched(this, who);
            return;
        }

        string text;
        Color color;
        if (kind == Kind.Repair)
        {
            who.currentHealth = Mathf.Min(who.maxHealth, who.currentHealth + repairAmount);
            text = "REPAIR +" + repairAmount;
            color = RepairColor;
        }
        else
        {
            foreach (WeaponSystem w in who.GetComponentsInChildren<WeaponSystem>())
                w.CoolAll();
            text = "COOLANT";
            color = CoolantColor;
        }

        if (MatchManager.Instance != null) MatchManager.Instance.ShowPopup(who.transform, text, color, false);
        SoundManager.Play("switch", 1f);
        Destroy(gameObject);
    }

    // オンラインで取られたとき(2人の画面で呼ばれる)。HP と熱の変更は、権限のある画面だけが行う
    public void ApplyOnline(HealthSystem who)
    {
        string text;
        Color color;
        if (kind == Kind.Repair)
        {
            if (who.IsServer) who.ServerHeal(repairAmount);
            text = "REPAIR +" + repairAmount;
            color = RepairColor;
        }
        else
        {
            if (who.IsOwner)
                foreach (WeaponSystem w in who.GetComponentsInChildren<WeaponSystem>())
                    w.CoolAll();
            text = "COOLANT";
            color = CoolantColor;
        }
        if (MatchManager.Instance != null) MatchManager.Instance.ShowPopup(who.transform, text, color, false);
        SoundManager.Play("switch", 1f);
        Destroy(gameObject);
    }

    // ===== 絵(プログラムで作る 16x16 のドット絵) =====

    static Sprite RepairSprite()
    {
        if (repairSprite == null) repairSprite = MakeIcon(RepairColor, true);
        return repairSprite;
    }

    static Sprite CoolantSprite()
    {
        if (coolantSprite == null) coolantSprite = MakeIcon(CoolantColor, false);
        return coolantSprite;
    }

    static Sprite MakeIcon(Color c, bool cross)
    {
        const int N = 16;
        Texture2D tex = new Texture2D(N, N);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0, 0, 0, 0);
        Color dark = new Color(0.03f, 0.04f, 0.05f, 1f);

        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool corner = (x < 2 || x > N - 3) && (y < 2 || y > N - 3);
                bool edge = x == 0 || y == 0 || x == N - 1 || y == N - 1;
                Color px = corner ? clear : (edge ? c : dark);

                int cx = x - N / 2, cy = y - N / 2;
                if (cross)
                {
                    // + の形
                    if ((Mathf.Abs(cx + 0.5f) < 1.6f && Mathf.Abs(cy + 0.5f) < 5f) ||
                        (Mathf.Abs(cy + 0.5f) < 1.6f && Mathf.Abs(cx + 0.5f) < 5f)) px = c;
                }
                else
                {
                    // ❄ っぽい形(縦・横・斜めの線)
                    float ax = Mathf.Abs(cx + 0.5f), ay = Mathf.Abs(cy + 0.5f);
                    bool onLine = (ax < 0.8f && ay < 5.5f) || (ay < 0.8f && ax < 5.5f) || (Mathf.Abs(ax - ay) < 0.8f && ax < 4.2f);
                    if (onLine) px = c;
                }
                tex.SetPixel(x, y, px);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 18f); // 約 0.9 マス
    }
}