using UnityEngine;

public class BoundBullet : MonoBehaviour
{
    public float speed = 20f;
    public int damage = 1;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // 弾が生成された瞬間の「右方向」に向かって飛んでいく
        GetComponent<Rigidbody2D>().linearVelocity = transform.right * speed;

        // 画面外に消えた時のために3秒で自動消去
        Destroy(gameObject, 3f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        // 敵（Enemyタグ）に当たった時の処理
        if (collision.CompareTag("Enemy"))
        {
            // ログを出して弾を消す（敵のHP減少は後ほど！）
            Debug.Log("敵にヒット！");
            Destroy(gameObject);
        }

        // 壁（Wallタグなど）に当たっても消えるようにする場合
        if (collision.CompareTag("Wall"))
        {
            Debug.Log("壁にヒット");
            // 衝突点の法線を取得
            Vector2 contactPoint = collision.ClosestPoint(transform.position);
            Vector2 toCollider = contactPoint - (Vector2)transform.position;
            Vector2 nowVelocity = GetComponent<Rigidbody2D>().linearVelocity;
            if (Mathf.Abs(toCollider.x) > Mathf.Abs(toCollider.y))
            {
                GetComponent<Rigidbody2D>().linearVelocity = new Vector2(-nowVelocity.x, nowVelocity.y).normalized * speed;
            }
            else
            {
                GetComponent<Rigidbody2D>().linearVelocity = new Vector2(nowVelocity.x, -nowVelocity.y).normalized * speed;
            }
            
        }
    }
}
