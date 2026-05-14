using UnityEngine;

public class TrackingBullet : MonoBehaviour
{
    public float speed = 20f;
    public int damage = 1;
    GameObject enemy;
    float timer;
    float trackThreshold = 0.1f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        enemy = GameObject.FindWithTag("Enemy");
        GetComponent<Rigidbody2D>().linearVelocity = transform.right * speed;

        Destroy(gameObject, 3f);
    }

    // Update is called once per frame
    void Update()
    {
        
        if (timer > trackThreshold) 
        {
            Vector3 toEnemy = enemy.transform.position - this.transform.position;
            GetComponent<Rigidbody2D>().linearVelocity = (GetComponent<Rigidbody2D>().linearVelocity.normalized + (Vector2)toEnemy*0.05f).normalized * speed;
        }
        else
        {
            timer += Time.deltaTime;
        }
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
