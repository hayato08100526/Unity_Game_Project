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

        // 光線の先:最初にぶつかったもの(撃った本人と、すり抜け足場は無視)
        RaycastHit2D[] hits = Physics2D.RaycastAll(start, dir, maxLength);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit2D h in hits)
        {
            if (h.collider == null || h.collider.isTrigger) continue;
            if (owner != null && h.collider.transform.IsChildOf(owner)) continue;
            if (h.collider.GetComponent<PlatformEffector2D>() != null) continue;   // 一方通行の足場は通り抜ける

            end = h.point;
            HealthSystem target = h.collider.GetComponentInParent<HealthSystem>();
            if (target != null && (owner == null || target.transform != owner))
            {
                Vector2 kb = (dir + Vector2.up * 0.4f).normalized * knockbackForce;
                target.TakeDamageFrom(owner, damage, kb, h.point);
            }
            break;   // 床・壁・相手のどれかで止まる
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