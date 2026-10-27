using UnityEngine;

// LASER:撃った瞬間に、まっすぐ光線を伸ばして当てる武器(弾が飛ぶ時間がない)
// 空のオブジェクトにこのスクリプトと WeaponInfo を付けて、プレハブにして使う
public class LaserBeam : MonoBehaviour
{
    public int damage = 16;
    public float knockbackForce = 7f;
    public float maxLength = 30f;
    [Tooltip("光線が消えるまでの時間(秒)")]
    public float fadeTime = 0.22f;
    public Color color = new Color32(0x4F, 0xE3, 0xF0, 0xFF);

    private Transform owner;
    private LineRenderer core, glow;
    private float age;
    private Vector2 start, end;

    // WeaponSystem から呼ばれる(撃った本人)
    public void SetOwner(Transform shooter)
    {
        owner = shooter;
    }

    void Start()
    {
        start = transform.position;
        Vector2 dir = transform.right;
        end = start + dir * maxLength;

        // ① まず、光線を止める床・壁までの距離を調べる(ロボット・撃った本人・すり抜け足場・トリガーは無視)
        float stop = maxLength;
        RaycastHit2D[] hits = Physics2D.RaycastAll(start, dir, maxLength);
        foreach (RaycastHit2D h in hits)
        {
            if (h.collider == null || h.collider.isTrigger) continue;
            if (owner != null && h.collider.transform.IsChildOf(owner)) continue;
            if (h.collider.GetComponentInParent<HealthSystem>() != null) continue;
            if (h.collider.GetComponent<PlatformEffector2D>() != null) continue;   // 一方通行の足場は通り抜ける
            if (h.distance < stop) stop = h.distance;
        }

        // ② その手前にいるロボットを、当たり判定の箱と光線で直接調べる
        //    (レイヤーや物理の設定に関係なく、見た目どおりに当たる)
        HealthSystem victim = null;
        float victimDist = stop;
        Ray2DHelper ray = new Ray2DHelper(start, dir);
        foreach (HealthSystem hs in FindObjectsByType<HealthSystem>(FindObjectsSortMode.None))
        {
            if (hs == null || hs.currentHealth <= 0) continue;
            if (owner != null && hs.transform == owner) continue;
            Collider2D col = hs.GetComponent<Collider2D>();
            Bounds b = col != null ? col.bounds : new Bounds(hs.transform.position, new Vector3(0.8f, 1.6f, 0f));
            b.Expand(0.15f);   // 光線の太さの分だけ少し広げる
            if (ray.Hits(b, out float d) && d < victimDist)
            {
                victimDist = d;
                victim = hs;
            }
        }

        end = start + dir * (victim != null ? victimDist : stop);
        if (victim != null)
        {
            Vector2 kb = (dir + Vector2.up * 0.4f).normalized * knockbackForce;
            victim.TakeDamageFrom(owner, damage, kb, end);
        }

        glow = MakeLine("Glow", 0.5f, new Color(color.r, color.g, color.b, 0.35f), 29);
        core = MakeLine("Core", 0.16f, Color.white, 30);
        CameraFollow.Shake(0.08f, 0.08f);
    }

    LineRenderer MakeLine(string n, float width, Color c, int order)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = lr.endWidth = width;
        Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        lr.material = new Material(sh);
        lr.startColor = lr.endColor = c;
        lr.sortingOrder = order;
        lr.numCapVertices = 2;
        return lr;
    }

    void Update()
    {
        age += Time.deltaTime;
        float u = Mathf.Clamp01(age / fadeTime);   // 0 → 1
        if (core != null)
        {
            float w = Mathf.Lerp(0.16f, 0f, u);
            core.startWidth = core.endWidth = w;
            glow.startWidth = glow.endWidth = Mathf.Lerp(0.5f, 0.05f, u);
            Color g = glow.startColor;
            glow.startColor = glow.endColor = new Color(g.r, g.g, g.b, 0.35f * (1f - u));
        }
        if (age >= fadeTime) Destroy(gameObject);
    }
}

// 2D の光線と箱が交わるか(交わるなら、光線の出発点からの距離を返す)
public struct Ray2DHelper
{
    private readonly Vector2 o, d;
    public Ray2DHelper(Vector2 origin, Vector2 dir) { o = origin; d = dir.normalized; }

    public bool Hits(Bounds b, out float dist)
    {
        dist = 0f;
        float tmin = 0f, tmax = float.MaxValue;
        for (int axis = 0; axis < 2; axis++)
        {
            float oo = axis == 0 ? o.x : o.y, dd = axis == 0 ? d.x : d.y;
            float mn = axis == 0 ? b.min.x : b.min.y, mx = axis == 0 ? b.max.x : b.max.y;
            if (Mathf.Abs(dd) < 1e-6f)
            {
                if (oo < mn || oo > mx) return false;
                continue;
            }
            float t1 = (mn - oo) / dd, t2 = (mx - oo) / dd;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
            tmin = Mathf.Max(tmin, t1);
            tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        dist = tmin;
        return true;
    }
}