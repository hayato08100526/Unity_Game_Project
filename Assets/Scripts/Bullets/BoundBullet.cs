using UnityEngine;

// BOUND の弾:床や壁に当たると跳ね返る。ロボットに当たるとダメージ
// (弾の Collider は Is Trigger のままでよい。跳ね返りはこのスクリプトが計算する)
public class BoundBullet : MonoBehaviour
{
    public float speed = 20f;
    public int damage = 6;
    [Tooltip("当たった相手を吹き飛ばす強さ")]
    public float knockbackForce = 6f;
    [Tooltip("跳ね返る回数。これを超えると消える")]
    public int maxBounces = 3;
    [Tooltip("弾が消えるまでの時間(秒)")]
    public float lifetime = 3f;

    private Rigidbody2D rb;
    private Transform owner;
    private int bounces;
    private float radius = 0.15f;

    public void SetOwner(Transform shooter) { owner = shooter; }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // 「Used By Effector」が付いていると、エフェクターがない弾は当たり判定が動かないので外す
        foreach (Collider2D c in GetComponents<Collider2D>()) c.usedByEffector = false;
        var col = GetComponent<CircleCollider2D>();
        if (col != null) radius = col.radius * Mathf.Abs(transform.lossyScale.x);
    }

    void Start()
    {
        rb.linearVelocity = transform.right * speed;
        Destroy(gameObject, lifetime);
    }

    void FixedUpdate()
    {
        // この先で床・壁にぶつかるなら、跳ね返す
        Vector2 v = rb.linearVelocity;
        float dist = v.magnitude * Time.fixedDeltaTime + radius;
        if (dist <= 0.0001f) return;
        foreach (RaycastHit2D h in Physics2D.RaycastAll(rb.position, v.normalized, dist))
        {
            if (h.collider == null || h.collider.isTrigger) continue;
            if (h.collider.GetComponentInParent<HealthSystem>() != null) continue;   // ロボットは OnTriggerEnter2D で
            if (h.collider.GetComponent<PlatformEffector2D>() != null && v.y > 0f) continue;   // すり抜け足場は下からは通る

            bounces++;
            if (bounces > maxBounces) { Destroy(gameObject); return; }
            rb.linearVelocity = Vector2.Reflect(v, h.normal);
            rb.position = h.point + h.normal * (radius + 0.02f);
            transform.right = rb.linearVelocity;
            return;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        HealthSystem target = other.GetComponentInParent<HealthSystem>();
        if (target == null) return;
        if (owner != null && target.transform == owner) return;   // 自分には当たらない

        Vector2 dir = rb.linearVelocity.sqrMagnitude > 0.01f ? rb.linearVelocity.normalized : (Vector2)transform.right;
        Vector2 kb = (dir + Vector2.up * 0.5f).normalized * knockbackForce;
        target.TakeDamageFrom(owner, damage, kb, transform.position);
        Destroy(gameObject);
    }
}