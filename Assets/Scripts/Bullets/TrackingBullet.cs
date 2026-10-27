using UnityEngine;

// TRACK の弾:いちばん近い相手を追いかけて曲がる。ロボットに当たるとダメージ、壁や床で消える
public class TrackingBullet : MonoBehaviour
{
    public float speed = 10f;
    public int damage = 7;
    [Tooltip("当たった相手を吹き飛ばす強さ")]
    public float knockbackForce = 5f;
    [Tooltip("1秒に曲がれる角度(大きいほど、よく追いかける)")]
    public float turnSpeed = 160f;
    [Tooltip("撃ってから追いかけ始めるまでの時間(秒)")]
    public float homingDelay = 0.12f;
    [Tooltip("弾が消えるまでの時間(秒)")]
    public float lifetime = 4f;

    private Rigidbody2D rb;
    private Transform owner;
    private Transform target;
    private float born;

    public void SetOwner(Transform shooter) { owner = shooter; }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // 「Used By Effector」が付いていると、エフェクターがない弾は当たり判定が動かないので外す
        foreach (Collider2D c in GetComponents<Collider2D>()) c.usedByEffector = false;
    }

    void Start()
    {
        born = Time.time;
        rb.linearVelocity = transform.right * speed;
        Destroy(gameObject, lifetime);
    }

    void FixedUpdate()
    {
        if (Time.time - born < homingDelay) return;

        if (target == null || !target.gameObject.activeInHierarchy) target = FindTarget();
        if (target == null) return;

        // 相手のほうへ、少しずつ向きを変える
        Vector2 to = (Vector2)target.position + Vector2.up * 0.4f - rb.position;
        Vector2 v = rb.linearVelocity;
        float cur = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        float want = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
        float ang = Mathf.MoveTowardsAngle(cur, want, turnSpeed * Time.fixedDeltaTime);
        Vector2 nd = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
        rb.linearVelocity = nd * speed;
        transform.right = nd;
    }

    Transform FindTarget()
    {
        Transform best = null;
        float bestD = float.MaxValue;
        foreach (HealthSystem h in FindObjectsByType<HealthSystem>(FindObjectsSortMode.None))
        {
            if (h == null || (owner != null && h.transform == owner) || h.currentHealth <= 0) continue;
            float d = ((Vector2)h.transform.position - rb.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = h.transform; }
        }
        return best;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        HealthSystem hs = other.GetComponentInParent<HealthSystem>();
        if (hs != null)
        {
            if (owner != null && hs.transform == owner) return;
            Vector2 dir = rb.linearVelocity.sqrMagnitude > 0.01f ? rb.linearVelocity.normalized : (Vector2)transform.right;
            Vector2 kb = (dir + Vector2.up * 0.5f).normalized * knockbackForce;
            hs.TakeDamageFrom(owner, damage, kb, transform.position);
            Destroy(gameObject);
            return;
        }
        // 床・壁(すり抜け足場以外)に当たったら消える
        if (!other.isTrigger && other.GetComponent<PlatformEffector2D>() == null) Destroy(gameObject);
    }
}