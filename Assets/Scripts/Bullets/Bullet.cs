using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float speed = 20f;
    public int damage = 1;

    [Tooltip("当たった相手を吹き飛ばす強さ")]
    public float knockbackForce = 5f;

    private Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Start()
    {
        // 弾が生成された瞬間の「右方向」に向かって飛んでいく
        rb.linearVelocity = transform.right * speed;

        // 画面外に消えた時のために3秒で自動消去
        Destroy(gameObject, 3f);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // プレイヤーに当たった時(撃った本人にはWeaponSystem側で当たらないようにしている)
        HealthSystem target = collision.GetComponentInParent<HealthSystem>();
        if (target != null)
        {
            Vector2 dir = rb.linearVelocity.sqrMagnitude > 0.01f
                ? rb.linearVelocity.normalized
                : (Vector2)transform.right;

            // 弾の進行方向に、少し上向きに吹き飛ばす
            Vector2 knockback = (dir + Vector2.up * 0.5f).normalized * knockbackForce;

            // オンラインではHostの画面の弾だけが有効(HealthSystem側で判定)
            target.TakeDamage(damage, knockback);
            Destroy(gameObject);
            return;
        }

        // 敵(Enemyタグ)に当たった時の処理
        if (collision.CompareTag("Enemy"))
        {
            Debug.Log("敵にヒット！");
            Destroy(gameObject);
        }

        // 壁に当たったら消える
        if (collision.CompareTag("Wall"))
        {
            Destroy(gameObject);
        }
    }
}