using System.Collections.Generic;
using UnityEngine;

// 描いた線の上をなぞって飛ぶ弾(ROUTE 用)
// 線の終わりまで来たら、少しだけまっすぐ進んで消える
public class RouteBullet : MonoBehaviour
{
    public float speed = 10f;
    public int damage = 14;
    public float knockbackForce = 5f;
    [Tooltip("線の終わりから、まっすぐ進み続ける時間(秒)")]
    public float overshootTime = 0.4f;
    [Tooltip("安全装置:この時間がたったら必ず消える(秒)")]
    public float maxLifetime = 6f;

    private Rigidbody2D rb;
    private Transform owner;
    private readonly List<Vector3> path = new List<Vector3>();
    private int index;
    private Vector2 lastDir = Vector2.right;
    private float overshootLeft = -1f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
        }
        Destroy(gameObject, maxLifetime);
    }

    void Start()
    {
        // 線がもらえなかったときは、向いている方向にまっすぐ飛ぶ
        if (path.Count == 0) lastDir = transform.right;
    }

    // WeaponSystem から呼ばれる
    public void SetOwner(Transform shooter)
    {
        owner = shooter;
    }

    // WeaponSystem から呼ばれる:なぞる線の点
    public void SetPath(Vector3[] points)
    {
        path.Clear();
        path.Add(transform.position);
        path.AddRange(points);
        index = 1;
    }

    void FixedUpdate()
    {
        Vector2 pos = rb != null ? rb.position : (Vector2)transform.position;
        float step = speed * Time.fixedDeltaTime;

        if (index < path.Count)
        {
            // 線の点を順番にたどる
            while (step > 0f && index < path.Count)
            {
                Vector2 target = path[index];
                Vector2 d = target - pos;
                float dist = d.magnitude;
                if (dist <= step)
                {
                    pos = target;
                    step -= dist;
                    if (dist > 0.0001f) lastDir = d / dist;
                    index++;
                }
                else
                {
                    lastDir = d / dist;
                    pos += lastDir * step;
                    step = 0f;
                }
            }
        }
        else
        {
            // 線の終わり:少しだけまっすぐ進んで消える
            if (overshootLeft < 0f) overshootLeft = overshootTime;
            overshootLeft -= Time.fixedDeltaTime;
            pos += lastDir * step;
            if (overshootLeft <= 0f)
            {
                Destroy(gameObject);
                return;
            }
        }

        if (rb != null) rb.MovePosition(pos);
        else transform.position = pos;

        if (lastDir.sqrMagnitude > 0.0001f) transform.right = lastDir;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        HealthSystem target = other.GetComponentInParent<HealthSystem>();
        if (target == null) return;
        if (owner != null && target.transform == owner) return;

        Vector2 knockback = (lastDir + Vector2.up * 0.5f).normalized * knockbackForce;
        target.TakeDamage(damage, knockback, transform.position);
        Destroy(gameObject);
    }
}