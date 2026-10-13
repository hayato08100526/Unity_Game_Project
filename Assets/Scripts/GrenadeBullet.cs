using System.Collections.Generic;
using UnityEngine;

// 山なりに飛んで、何かに当たるか時間が来たら爆発する弾(グレネード用)
// 撃った本人は爆風で飛ぶが、ダメージは受けない
public class GrenadeBullet : MonoBehaviour
{
    [Header("投げ方")]
    public float speed = 12f;
    [Tooltip("投げるときに足す上向きの速さ(山なりにする)")]
    public float extraUp = 3f;
    [Tooltip("重力の強さ")]
    public float gravity = 2f;
    [Tooltip("何にも当たらなかったときに爆発するまでの時間(秒)")]
    public float fuseTime = 2f;

    [Header("爆発")]
    public int damage = 15;
    public float radius = 1.5f;
    public float knockbackForce = 10f;
    [Tooltip("撃った本人が爆風で飛ぶ強さ(ダメージはなし)")]
    public float selfKnockbackForce = 8f;

    [Header("爆発の見た目")]
    public Color explosionColor = new Color(1f, 0.71f, 0.28f, 0.7f);
    public float explosionDuration = 0.3f;

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Transform owner;
    private bool exploded;
    private float explodeTimer;
    private Vector3 explosionScale;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = GetComponentInChildren<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();

        // 安全装置:何があっても、この時間がたったら必ず消える
        Destroy(gameObject, fuseTime + explosionDuration + 0.5f);
    }

    // WeaponSystem から呼ばれる
    public void SetOwner(Transform shooter)
    {
        owner = shooter;
    }

    void Start()
    {
        Invoke(nameof(Explode), fuseTime);

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = gravity;
            rb.linearVelocity = (Vector2)transform.right * speed + Vector2.up * extraUp;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (exploded || other.isTrigger) return;
        if (owner != null && other.transform.IsChildOf(owner)) return;
        Explode();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (exploded) return;
        if (owner != null && collision.transform.IsChildOf(owner)) return;
        Explode();
    }

    void Explode()
    {
        if (exploded) return;
        exploded = true;
        CancelInvoke();

        Vector2 center = transform.position;
        var alreadyHit = new HashSet<HealthSystem>();

        // 爆発で画面を揺らす
        CameraFollow.Shake(0.25f, 0.2f);

        foreach (Collider2D col in Physics2D.OverlapCircleAll(center, radius))
        {
            HealthSystem h = col.GetComponentInParent<HealthSystem>();
            if (h == null || alreadyHit.Contains(h)) continue;
            alreadyHit.Add(h);

            // 爆発の中心から外へ、少し上向きに吹き飛ばす
            Vector2 dir = (Vector2)h.transform.position - center;
            dir = dir.sqrMagnitude < 0.01f ? Vector2.up : dir.normalized;
            dir = (dir + Vector2.up * 0.5f).normalized;

            if (owner != null && h.transform == owner) h.Push(dir * selfKnockbackForce);
            else h.TakeDamage(damage, dir * knockbackForce, center);
        }

        // 弾を止めて、爆発の見た目に切り替える
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
        }
        foreach (Collider2D c in GetComponentsInChildren<Collider2D>()) c.enabled = false;

        // 爆発の演出が終わったら必ず消える
        Destroy(gameObject, explosionDuration + 0.05f);

        if (sr != null && sr.sprite != null)
        {
            float spriteSize = Mathf.Max(sr.sprite.bounds.size.x, 0.01f);
            explosionScale = Vector3.one * (radius * 2f / spriteSize);
            transform.rotation = Quaternion.identity;
            sr.color = explosionColor;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        if (!exploded || sr == null) return;

        explodeTimer += Time.deltaTime;
        float u = Mathf.Clamp01(explodeTimer / explosionDuration);
        if (explosionScale != Vector3.zero)
            transform.localScale = explosionScale * Mathf.Lerp(0.3f, 1f, u);
        sr.color = new Color(explosionColor.r, explosionColor.g, explosionColor.b, explosionColor.a * (1f - u));
    }
}