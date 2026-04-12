using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float speed = 20f;
    public int damage = 1;

    void Start()
    {
        // 弾が生成された瞬間の「右方向」に向かって飛んでいく
        GetComponent<Rigidbody2D>().linearVelocity = transform.right * speed;

        // 画面外に消えた時のために3秒で自動消去
        Destroy(gameObject, 3f);
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
            Destroy(gameObject);
        }
    }
}